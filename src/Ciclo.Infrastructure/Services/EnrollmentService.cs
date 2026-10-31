using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Core.Notifications;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Notifications;
using Ciclo.Infrastructure.Security;

namespace Ciclo.Infrastructure.Services;

public interface IEnrollmentService
{
    // Period management
    Task<List<EnrollmentPeriodDto>> GetPeriodsAsync(bool includeInactive, Guid tenantId);
    Task<EnrollmentPeriodDto> CreatePeriodAsync(CreateEnrollmentPeriodRequest request, Guid tenantId, UserRole role);
    Task ClosePeriodAsync(Guid periodId, Guid tenantId, UserRole role);

    // Enrollment CRUD
    Task<EnrollmentDto> CreateEnrollmentAsync(CreateEnrollmentRequest request, Guid tenantId, UserRole role);
    Task<PagedResponse<EnrollmentDto>> GetEnrollmentsAsync(string? status, Guid? periodId, int page, int pageSize, Guid tenantId, Guid userId, UserRole role);
    Task<EnrollmentDto> GetEnrollmentByIdAsync(Guid id, Guid tenantId, Guid userId, UserRole role);

    // State machine transitions
    Task<EnrollmentDto> SubmitAsync(Guid enrollmentId, Guid tenantId, Guid userId, UserRole role);
    Task<EnrollmentDto> ApproveAsync(Guid enrollmentId, Guid tenantId, Guid userId, UserRole role);
    Task RejectAsync(Guid enrollmentId, RejectEnrollmentRequest request, Guid tenantId, Guid userId, UserRole role);
    Task CancelAsync(Guid enrollmentId, Guid tenantId, Guid userId, UserRole role);
    Task<EnrollmentDto> ReopenAsync(Guid enrollmentId, Guid tenantId, Guid userId, UserRole role);
}

public class EnrollmentService : IEnrollmentService
{
    private readonly AppDbContext _db;
    private readonly IAuditTrail _audit;

    public EnrollmentService(AppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    // ── State Machine ──────────────────────────────────────────────

    private static readonly Dictionary<EnrollmentStatus, HashSet<EnrollmentStatus>> Transitions = new()
    {
        // Spec 50 FR-007 permite os dois destinos a partir de Rascunho: Pendente quando a
        // documentação está em ordem, DocumentacaoPendente quando falta documento obrigatório.
        [EnrollmentStatus.Rascunho] =
        [
            EnrollmentStatus.Pendente,
            EnrollmentStatus.DocumentacaoPendente
        ],
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
        // M12 - reabertura: de um estado terminal so se volta para Pendente, e apenas por
        // `ReopenAsync` (Admin). Nao existe caminho direto de Aprovado para Cancelado nem de
        // Rejeitado para Aprovado: reabrir e uma decisao explicita, com autor registrado.
        // O destino e decidido por `DocumentCompliance` (Pendente ou DocumentacaoPendente), como
        // no submit: a tabela precisa aceitar os dois ou a reabertura falharia justamente quando
        // falta documento — o caso em que ela e mais necessaria.
        [EnrollmentStatus.Aprovado] = [EnrollmentStatus.Pendente, EnrollmentStatus.DocumentacaoPendente],
        [EnrollmentStatus.Rejeitado] = [EnrollmentStatus.Pendente, EnrollmentStatus.DocumentacaoPendente],
        [EnrollmentStatus.Cancelado] = [EnrollmentStatus.Pendente, EnrollmentStatus.DocumentacaoPendente],
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

    /// <summary>
    /// B1: a resposta NÃO é paginada — e agora diz isso pelo tipo.
    ///
    /// Antes o método devolvia <c>PagedResponse</c> sem paginar, com <c>page = 1</c> e
    /// <c>pageSize = total</c> (0 quando vazio): um envelope falso, que qualquer cliente leria
    /// como "há páginas" quando não há. O número de períodos por colégio é pequeno e limitado
    /// pelo calendário — paginar seria complexidade sem uso.
    /// </summary>
    public async Task<List<EnrollmentPeriodDto>> GetPeriodsAsync(bool includeInactive, Guid tenantId)
    {
        IQueryable<EnrollmentPeriod> query = _db.EnrollmentPeriods;

        if (!includeInactive)
            query = query.Where(p => p.IsActive);

        return await query
            .OrderByDescending(p => p.AnoLetivo)
            .Select(p => new EnrollmentPeriodDto(
                p.Id, p.Nome, p.DataInicio, p.DataFim, p.AnoLetivo, p.IsActive))
            .ToListAsync();
    }

    public async Task<EnrollmentPeriodDto> CreatePeriodAsync(CreateEnrollmentPeriodRequest request, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.EnrollmentPeriodManage, role);

        if (string.IsNullOrWhiteSpace(request.Nome))
            throw new EnrollmentException("period_name_required", 400);
        if (request.DataFim <= request.DataInicio)
            throw new EnrollmentException("invalid_period_dates", 400);
        var now = DateTime.UtcNow;
        if (request.AnoLetivo < now.Year)
            throw new EnrollmentException("ano_letivo_must_be_current_or_future", 400);

        // M6: sem teto, um ano absurdo era aceito e virava período "válido".
        if (request.AnoLetivo > now.Year + 1)
            throw new EnrollmentException("ano_letivo_invalid", 400);

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

    public async Task ClosePeriodAsync(Guid periodId, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.EnrollmentPeriodManage, role);

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

    public async Task<EnrollmentDto> CreateEnrollmentAsync(CreateEnrollmentRequest request, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.EnrollmentWrite, role);

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
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        IQueryable<Enrollment> query = _db.Enrollments;

        // B9: a regra de vínculo tem um dono único.
        query = query.VisibleTo(userId, role);

        // Filtro inválido não pode ser ignorado em silêncio: devolver a lista inteira faria a tela
        // mostrar "todas" quando o operador pediu um status que não existe. TryParse sozinho aceita
        // valor numérico fora do enum ("99"), por isso IsDefined.
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<EnrollmentStatus>(status.Trim(), ignoreCase: true, out var parsedStatus)
                || !Enum.IsDefined(parsedStatus))
                throw new EnrollmentException("status_invalid", 400);

            query = query.Where(e => e.Status == parsedStatus);
        }

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

        // B9: a regra de vínculo tem um dono único.
        await StudentAccess.EnsureLinkedAsync(_db, enrollment.StudentId, userId, role);

        // Spec 60 FR-004 / E4 — a visão de documentação vive aqui desde o A4: a API separada de
        // rematrícula foi removida por ser uma segunda superfície sobre a MESMA tabela, e esta é a
        // única parte dela que tinha informação própria. `IsExpired` distingue "reaproveitado" de
        // "venceu — precisa reenviar", então a lista vem dos documentos APROVADOS (inclusive
        // vencidos), e não apenas dos válidos.
        var compliance = await DocumentCompliance.EvaluateAsync(_db, enrollment.StudentId);
        var validDocumentIds = compliance.ValidDocuments.Select(document => document.Id).ToHashSet();

        var carriedDocuments = compliance.ApprovedDocuments
            .Select(document => new CarriedDocumentDto(
                document.Id,
                document.DocumentType.Nome,
                document.DataValidade,
                IsExpired: !validDocumentIds.Contains(document.Id)))
            .ToList();

        var missingDocumentTypes = compliance.MissingRequiredTypes
            .Select(type => type.Nome)
            .ToList();

        return MapToDto(enrollment) with
        {
            CarriedDocuments = carriedDocuments,
            MissingDocumentTypes = missingDocumentTypes,
        };
    }

    // ── State Machine Transitions ──────────────────────────────────

    public async Task<EnrollmentDto> SubmitAsync(Guid enrollmentId, Guid tenantId, Guid userId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.EnrollmentWrite, role);

        var enrollment = await _db.Enrollments
            .Include(e => e.Student)
            .Include(e => e.Period)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId);

        if (enrollment is null)
            throw new EnrollmentException("enrollment_not_found", 404);

        // Spec 50 FR-007: se falta documento obrigatório, o destino é DocumentacaoPendente — e não
        // Pendente. Sem isto o estado existia na máquina de estados mas era inalcançável em
        // produção (só o seeder o criava), e a escola perdia o sinal de que precisava cobrar
        // documento antes de aprovar.
        var compliance = await DocumentCompliance.EvaluateAsync(_db, enrollment.StudentId);
        var target = compliance.HasMissingRequiredDocuments
            ? EnrollmentStatus.DocumentacaoPendente
            : EnrollmentStatus.Pendente;

        if (!CanTransition(enrollment.Status, target))
            throw InvalidTransition(enrollment.Status, target);

        ApplyStatus(enrollment, target, userId);

        await _db.SaveChangesAsync();
        return MapToDto(enrollment);
    }

    public async Task<EnrollmentDto> ApproveAsync(Guid enrollmentId, Guid tenantId, Guid userId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.EnrollmentReview, role);

        var enrollment = await _db.Enrollments
            .Include(e => e.Student)
            .Include(e => e.Period)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId);

        if (enrollment is null)
            throw new EnrollmentException("enrollment_not_found", 404);

        if (!CanTransition(enrollment.Status, EnrollmentStatus.Aprovado))
            throw InvalidTransition(enrollment.Status, EnrollmentStatus.Aprovado);

        // Spec 50 FR-008: aprovar exige a documentação obrigatória em dia. É o que dá sentido a
        // DocumentacaoPendente — sem esta checagem, o estado só serviria de aviso.
        var compliance = await DocumentCompliance.EvaluateAsync(_db, enrollment.StudentId);
        if (compliance.HasMissingRequiredDocuments)
            throw new EnrollmentException("pending_required_documents", 400);

        var previousStatus = enrollment.Status.ToString();

        ApplyStatus(enrollment, EnrollmentStatus.Aprovado, userId);
        enrollment.ApprovedAt = DateTime.UtcNow;

        // Side effect: update student
        enrollment.Student.Status = StudentStatus.Ativo;
        enrollment.Student.AnoLetivo = enrollment.Period.AnoLetivo;

        _audit.Record(new AuditEntry(
            AuditActions.EnrollmentApproved,
            AuditTargetTypes.Enrollment,
            enrollment.Id,
            new { Status = previousStatus },
            new { Status = enrollment.Status.ToString(), ApprovedAt = enrollment.ApprovedAt }));

        await _db.SaveChangesAsync();
        return MapToDto(enrollment);
    }

    public async Task RejectAsync(Guid enrollmentId, RejectEnrollmentRequest request, Guid tenantId, Guid userId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.EnrollmentReview, role);

        var enrollment = await _db.Enrollments
            .Include(e => e.Student)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId);

        if (enrollment is null)
            throw new EnrollmentException("enrollment_not_found", 404);

        if (!CanTransition(enrollment.Status, EnrollmentStatus.Rejeitado))
            throw InvalidTransition(enrollment.Status, EnrollmentStatus.Rejeitado);

        if (string.IsNullOrWhiteSpace(request.Motivo) || request.Motivo.Length < 10 || request.Motivo.Length > 500)
            throw new EnrollmentException("motivo_required_10_to_500_chars", 400);

        var previousStatus = enrollment.Status.ToString();

        ApplyStatus(enrollment, EnrollmentStatus.Rejeitado, userId);
        enrollment.MotivoRejeicao = request.Motivo;

        _audit.Record(new AuditEntry(
            AuditActions.EnrollmentRejected,
            AuditTargetTypes.Enrollment,
            enrollment.Id,
            new { Status = previousStatus },
            new { Status = enrollment.Status.ToString(), MotivoRejeicao = request.Motivo }));

        await _db.SaveChangesAsync();
    }

    public async Task CancelAsync(Guid enrollmentId, Guid tenantId, Guid userId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.EnrollmentCancel, role);

        var enrollment = await _db.Enrollments
            .Include(e => e.Student)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId);

        if (enrollment is null)
            throw new EnrollmentException("enrollment_not_found", 404);

        if (enrollment.Status == EnrollmentStatus.Aprovado)
            throw new EnrollmentException("cannot_cancel_approved_enrollment", 400);

        if (!CanTransition(enrollment.Status, EnrollmentStatus.Cancelado))
            throw InvalidTransition(enrollment.Status, EnrollmentStatus.Cancelado);

        var previousStatus = enrollment.Status.ToString();

        ApplyStatus(enrollment, EnrollmentStatus.Cancelado, userId);

        _audit.Record(new AuditEntry(
            AuditActions.EnrollmentCancelled,
            AuditTargetTypes.Enrollment,
            enrollment.Id,
            new { Status = previousStatus },
            new { Status = enrollment.Status.ToString() }));

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// M12 - reabre uma matricula em estado terminal. Decisao de politica: **somente Admin**
    /// (o controller exige o papel). Staff continua aprovando, rejeitando e cancelando o fluxo
    /// normal; desfazer uma decisao ja tomada e ato administrativo.
    ///
    /// O destino e o mesmo do submit (Pendente ou DocumentacaoPendente) porque quem decide se a
    /// documentacao esta em dia e `DocumentCompliance`, nao a tela. `ApprovedAt` e
    /// `MotivoRejeicao` pertencem ao estado anterior e sao limpos — mante-los deixaria uma
    /// matricula "Pendente" carregando o motivo de uma rejeicao que nao vale mais.
    /// </summary>
    public async Task<EnrollmentDto> ReopenAsync(Guid enrollmentId, Guid tenantId, Guid userId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.EnrollmentReopen, role);

        var enrollment = await _db.Enrollments
            .Include(e => e.Student)
            .Include(e => e.Period)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId);

        if (enrollment is null)
            throw new EnrollmentException("enrollment_not_found", 404);

        // Reabrir so faz sentido a partir de um estado terminal; nos demais, o fluxo normal
        // (submit/approve) ja e o caminho correto.
        if (enrollment.Status is not (EnrollmentStatus.Aprovado or EnrollmentStatus.Rejeitado or EnrollmentStatus.Cancelado))
            throw new EnrollmentException("enrollment_not_reopenable", 400);

        var compliance = await DocumentCompliance.EvaluateAsync(_db, enrollment.StudentId);
        var target = compliance.HasMissingRequiredDocuments
            ? EnrollmentStatus.DocumentacaoPendente
            : EnrollmentStatus.Pendente;

        if (!CanTransition(enrollment.Status, target))
            throw InvalidTransition(enrollment.Status, target);

        ApplyStatus(enrollment, target, userId);
        enrollment.ApprovedAt = null;
        enrollment.MotivoRejeicao = null;

        await _db.SaveChangesAsync();
        return MapToDto(enrollment);
    }

    // ── Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Ponto unico de transicao de status (M12). Antes o status era atribuido direto em cinco
    /// lugares e nao havia registro de quem mudou nem quando.
    /// </summary>
    private static void ApplyStatus(Enrollment enrollment, EnrollmentStatus target, Guid userId)
    {
        enrollment.Status = target;
        enrollment.StatusChangedAt = DateTime.UtcNow;
        enrollment.StatusChangedBy = userId;
    }

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
