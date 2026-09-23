using Microsoft.EntityFrameworkCore;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;

namespace Ciclo.Infrastructure.Services;

public interface IEnrollmentService
{
    // Period management
    Task<PagedResponse<EnrollmentPeriodDto>> GetPeriodsAsync(bool includeInactive, Guid tenantId);
    Task<EnrollmentPeriodDto> CreatePeriodAsync(CreateEnrollmentPeriodRequest request, Guid tenantId);
    Task ClosePeriodAsync(Guid periodId, Guid tenantId);

    // Enrollment CRUD
    Task<EnrollmentDto> CreateEnrollmentAsync(CreateEnrollmentRequest request, Guid tenantId);
    Task<PagedResponse<EnrollmentDto>> GetEnrollmentsAsync(string? status, Guid? periodId, int page, int pageSize, Guid tenantId, Guid userId, UserRole role);
    Task<EnrollmentDto> GetEnrollmentByIdAsync(Guid id, Guid tenantId, Guid userId, UserRole role);

    // State machine transitions
    Task<EnrollmentDto> SubmitAsync(Guid enrollmentId, Guid tenantId);
    Task<EnrollmentDto> ApproveAsync(Guid enrollmentId, Guid tenantId);
    Task RejectAsync(Guid enrollmentId, RejectEnrollmentRequest request, Guid tenantId);
    Task CancelAsync(Guid enrollmentId, Guid tenantId);
}

public class EnrollmentService : IEnrollmentService
{
    private readonly AppDbContext _db;

    public EnrollmentService(AppDbContext db)
    {
        _db = db;
    }

    // ── State Machine ──────────────────────────────────────────────

    private static readonly Dictionary<EnrollmentStatus, HashSet<EnrollmentStatus>> Transitions = new()
    {
        [EnrollmentStatus.Rascunho] = [EnrollmentStatus.Pendente],
        [EnrollmentStatus.Pendente] =
        [
            EnrollmentStatus.DocumentacaoPendente,
            EnrollmentStatus.Aprovado,
            EnrollmentStatus.Rejeitado,
            EnrollmentStatus.Cancelado
        ],
        [EnrollmentStatus.DocumentacaoPendente] =
        [
            EnrollmentStatus.Pendente,
            EnrollmentStatus.Aprovado,
            EnrollmentStatus.Rejeitado,
            EnrollmentStatus.Cancelado
        ],
        // Rejeitado, Cancelado: terminal (no outgoing transitions)
    };

    public static bool CanTransition(EnrollmentStatus from, EnrollmentStatus to)
    {
        return Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    public static List<string> GetAllowedTransitions(EnrollmentStatus from)
    {
        return Transitions.TryGetValue(from, out var allowed)
            ? allowed.Select(t => t.ToString()).ToList()
            : [];
    }

    // ── Period Management ──────────────────────────────────────────

    public async Task<PagedResponse<EnrollmentPeriodDto>> GetPeriodsAsync(bool includeInactive, Guid tenantId)
    {
        IQueryable<EnrollmentPeriod> query = _db.EnrollmentPeriods;

        if (!includeInactive)
            query = query.Where(p => p.IsActive);

        var total = await query.CountAsync();

        var periods = await query
            .OrderByDescending(p => p.AnoLetivo)
            .Select(p => new EnrollmentPeriodDto(
                p.Id, p.Nome, p.DataInicio, p.DataFim, p.AnoLetivo, p.IsActive))
            .ToListAsync();

        return new PagedResponse<EnrollmentPeriodDto>(periods, total, 1, total);
    }

    public async Task<EnrollmentPeriodDto> CreatePeriodAsync(CreateEnrollmentPeriodRequest request, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(request.Nome))
            throw new EnrollmentException("period_name_required", 400);
        if (request.DataFim <= request.DataInicio)
            throw new EnrollmentException("invalid_period_dates", 400);
        if (request.AnoLetivo < DateTime.UtcNow.Year)
            throw new EnrollmentException("ano_letivo_must_be_current_or_future", 400);

        // Check overlapping active periods in same tenant + year
        var overlap = await _db.EnrollmentPeriods
            .AnyAsync(p => p.AnoLetivo == request.AnoLetivo && p.IsActive);
        if (overlap)
            throw new EnrollmentException("overlapping_periods", 400);

        var period = new EnrollmentPeriod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = request.Nome,
            DataInicio = request.DataInicio,
            DataFim = request.DataFim,
            AnoLetivo = request.AnoLetivo,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.EnrollmentPeriods.Add(period);
        await _db.SaveChangesAsync();

        return new EnrollmentPeriodDto(period.Id, period.Nome, period.DataInicio, period.DataFim, period.AnoLetivo, period.IsActive);
    }

    public async Task ClosePeriodAsync(Guid periodId, Guid tenantId)
    {
        var period = await _db.EnrollmentPeriods
            .FirstOrDefaultAsync(p => p.Id == periodId);

        if (period is null)
            throw new EnrollmentException("period_not_found", 404);

        if (!period.IsActive)
            return; // idempotent

        period.IsActive = false;
        await _db.SaveChangesAsync();
    }

    // ── Enrollment CRUD ────────────────────────────────────────────

    public async Task<EnrollmentDto> CreateEnrollmentAsync(CreateEnrollmentRequest request, Guid tenantId)
    {
        var student = await _db.Students
            .FirstOrDefaultAsync(s => s.Id == request.StudentId);

        if (student is null)
            throw new EnrollmentException("student_not_found", 404);

        if (student.Status == StudentStatus.Inativo)
            throw new EnrollmentException("student_inactive", 400);

        if (student.Status == StudentStatus.Transferido)
            throw new EnrollmentException("student_transferred", 400);

        var period = await _db.EnrollmentPeriods
            .FirstOrDefaultAsync(p => p.Id == request.EnrollmentPeriodId);

        if (period is null)
            throw new EnrollmentException("period_not_found", 404);

        if (!period.IsActive)
            throw new EnrollmentException("enrollment_period_closed", 400);

        var now = DateTime.UtcNow;
        if (now < period.DataInicio || now > period.DataFim)
            throw new EnrollmentException("enrollment_period_out_of_window", 400);

        // Check duplicate
        var exists = await _db.Enrollments
            .AnyAsync(e => e.StudentId == request.StudentId && e.EnrollmentPeriodId == request.EnrollmentPeriodId);
        if (exists)
            throw new EnrollmentException("enrollment_already_exists", 409);

        var enrollment = new Enrollment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StudentId = request.StudentId,
            EnrollmentPeriodId = request.EnrollmentPeriodId,
            Status = EnrollmentStatus.Rascunho,
            CreatedAt = DateTime.UtcNow
        };

        _db.Enrollments.Add(enrollment);
        await _db.SaveChangesAsync();

        return new EnrollmentDto(
            enrollment.Id,
            student.Id,
            student.Nome,
            period.Id,
            period.Nome,
            enrollment.Status.ToString(),
            enrollment.MotivoRejeicao,
            enrollment.CreatedAt,
            enrollment.ApprovedAt);
    }

    public async Task<PagedResponse<EnrollmentDto>> GetEnrollmentsAsync(
        string? status, Guid? periodId, int page, int pageSize,
        Guid tenantId, Guid userId, UserRole role)
    {
        if (page < 1) page = 1;
        if (pageSize > 100) pageSize = 100;
        if (pageSize < 1) pageSize = 20;

        IQueryable<Enrollment> query = _db.Enrollments;

        // Parent: only see linked children's enrollments
        if (role == UserRole.Parent)
        {
            query = query.Where(e => e.Student.StudentParents.Any(sp => sp.ParentId == userId));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<EnrollmentStatus>(status, ignoreCase: true, out var parsedStatus))
            query = query.Where(e => e.Status == parsedStatus);

        if (periodId.HasValue)
            query = query.Where(e => e.EnrollmentPeriodId == periodId.Value);

        var total = await query.CountAsync();

        var enrollments = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EnrollmentDto(
                e.Id,
                e.StudentId,
                e.Student.Nome,
                e.EnrollmentPeriodId,
                e.Period.Nome,
                e.Status.ToString(),
                e.MotivoRejeicao,
                e.CreatedAt,
                e.ApprovedAt,
                e.Student.Foto))
            .ToListAsync();

        return new PagedResponse<EnrollmentDto>(enrollments, total, page, pageSize);
    }

    public async Task<EnrollmentDto> GetEnrollmentByIdAsync(Guid id, Guid tenantId, Guid userId, UserRole role)
    {
        var enrollment = await _db.Enrollments
            .Include(e => e.Student)
            .ThenInclude(s => s.StudentParents)
            .Include(e => e.Period)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (enrollment is null)
            throw new EnrollmentException("enrollment_not_found", 404);

        if (role == UserRole.Parent)
        {
            if (!enrollment.Student.StudentParents.Any(sp => sp.ParentId == userId))
                throw new EnrollmentException("not_linked_to_student", 403);
        }

        return MapToDto(enrollment);
    }

    // ── State Machine Transitions ──────────────────────────────────

    public async Task<EnrollmentDto> SubmitAsync(Guid enrollmentId, Guid tenantId)
    {
        var enrollment = await _db.Enrollments
            .Include(e => e.Student)
            .Include(e => e.Period)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId);

        if (enrollment is null)
            throw new EnrollmentException("enrollment_not_found", 404);

        if (!CanTransition(enrollment.Status, EnrollmentStatus.Pendente))
            throw InvalidTransition(enrollment.Status, EnrollmentStatus.Pendente);

        // TODO: Spec 70 — check for pending required documents.
        // If pending docs exist: transition to DocumentacaoPendente.
        // For now, always go to Pendente.
        enrollment.Status = EnrollmentStatus.Pendente;

        await _db.SaveChangesAsync();
        return MapToDto(enrollment);
    }

    public async Task<EnrollmentDto> ApproveAsync(Guid enrollmentId, Guid tenantId)
    {
        var enrollment = await _db.Enrollments
            .Include(e => e.Student)
            .Include(e => e.Period)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId);

        if (enrollment is null)
            throw new EnrollmentException("enrollment_not_found", 404);

        if (!CanTransition(enrollment.Status, EnrollmentStatus.Aprovado))
            throw InvalidTransition(enrollment.Status, EnrollmentStatus.Aprovado);

        // TODO: Spec 70 — check for pending required documents.
        // If pending, return 400 with pending_required_documents error.

        enrollment.Status = EnrollmentStatus.Aprovado;
        enrollment.ApprovedAt = DateTime.UtcNow;

        // Side effect: update student
        enrollment.Student.Status = StudentStatus.Ativo;
        enrollment.Student.AnoLetivo = enrollment.Period.AnoLetivo;

        await _db.SaveChangesAsync();
        return MapToDto(enrollment);
    }

    public async Task RejectAsync(Guid enrollmentId, RejectEnrollmentRequest request, Guid tenantId)
    {
        var enrollment = await _db.Enrollments
            .Include(e => e.Student)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId);

        if (enrollment is null)
            throw new EnrollmentException("enrollment_not_found", 404);

        if (!CanTransition(enrollment.Status, EnrollmentStatus.Rejeitado))
            throw InvalidTransition(enrollment.Status, EnrollmentStatus.Rejeitado);

        if (string.IsNullOrWhiteSpace(request.Motivo) || request.Motivo.Length < 10 || request.Motivo.Length > 500)
            throw new EnrollmentException("motivo_required_10_to_500_chars", 400);

        enrollment.Status = EnrollmentStatus.Rejeitado;
        enrollment.MotivoRejeicao = request.Motivo;

        await _db.SaveChangesAsync();
    }

    public async Task CancelAsync(Guid enrollmentId, Guid tenantId)
    {
        var enrollment = await _db.Enrollments
            .Include(e => e.Student)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId);

        if (enrollment is null)
            throw new EnrollmentException("enrollment_not_found", 404);

        if (enrollment.Status == EnrollmentStatus.Aprovado)
            throw new EnrollmentException("cannot_cancel_approved_enrollment", 400);

        if (!CanTransition(enrollment.Status, EnrollmentStatus.Cancelado))
            throw InvalidTransition(enrollment.Status, EnrollmentStatus.Cancelado);

        enrollment.Status = EnrollmentStatus.Cancelado;

        await _db.SaveChangesAsync();
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static EnrollmentException InvalidTransition(EnrollmentStatus from, EnrollmentStatus to)
    {
        var allowed = GetAllowedTransitions(from);
        return new EnrollmentException(
            $"invalid_transition:{from}->{to}:{string.Join(",", allowed)}", 400);
    }

    private static EnrollmentDto MapToDto(Enrollment e)
    {
        return new EnrollmentDto(
            e.Id,
            e.StudentId,
            e.Student.Nome,
            e.EnrollmentPeriodId,
            e.Period.Nome,
            e.Status.ToString(),
            e.MotivoRejeicao,
            e.CreatedAt,
            e.ApprovedAt);
    }

}

public class EnrollmentException : Exception
{
    public int StatusCode { get; }

    public EnrollmentException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}
