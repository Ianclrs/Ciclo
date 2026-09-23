using Microsoft.EntityFrameworkCore;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;

namespace Ciclo.Infrastructure.Services;

public interface IStudentService
{
    Task<PagedResponse<StudentDto>> GetAllAsync(string? search, string? turma, string? status, int page, int pageSize, Guid tenantId, Guid userId, UserRole role);
    Task<StudentDto> GetByIdAsync(Guid id, Guid tenantId, Guid userId, UserRole role);
    Task<StudentDto> CreateAsync(CreateStudentRequest request, Guid tenantId);
    Task<StudentDto> UpdateAsync(Guid id, UpdateStudentRequest request, Guid tenantId);
    Task DeleteAsync(Guid id, Guid tenantId);
    Task LinkParentAsync(Guid studentId, Guid parentId, Guid tenantId);
    Task UnlinkParentAsync(Guid studentId, Guid parentId, Guid tenantId);
    Task<List<EnrollmentDto>> GetEnrollmentHistoryAsync(Guid studentId, Guid tenantId, Guid userId, UserRole role);
}

public class StudentService : IStudentService
{
    private readonly AppDbContext _db;

    public StudentService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResponse<StudentDto>> GetAllAsync(
        string? search, string? turma, string? status, int page, int pageSize,
        Guid tenantId, Guid userId, UserRole role)
    {
        // Normalize pagination
        if (page < 1) page = 1;
        if (pageSize > 100) pageSize = 100;
        if (pageSize < 1) pageSize = 20;

        IQueryable<Student> query = _db.Students;

        // Parent: only linked students
        if (role == UserRole.Parent)
        {
            query = query.Where(s => s.StudentParents.Any(sp => sp.ParentId == userId));
        }

        // Filters
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s => s.Nome.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(turma))
        {
            query = query.Where(s => s.Turma == turma);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<StudentStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                query = query.Where(s => s.Status == parsedStatus);
            }
        }

        var total = await query.CountAsync();

        var students = await query
            .OrderBy(s => s.Nome)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new StudentDto(
                s.Id,
                s.Nome,
                s.DataNascimento,
                s.Cpf,
                s.Turma,
                s.AnoLetivo,
                s.Status.ToString(),
                s.Observacoes,
                s.Foto,
                s.CreatedAt,
                s.StudentParents.Select(sp => new ParentLinkDto(
                    sp.ParentId,
                    sp.Parent.Name,
                    sp.Parent.Email ?? string.Empty
                )).ToList()
            ))
            .ToListAsync();

        return new PagedResponse<StudentDto>(students, total, page, pageSize);
    }

    public async Task<StudentDto> GetByIdAsync(Guid id, Guid tenantId, Guid userId, UserRole role)
    {
        var student = await _db.Students
            .Include(s => s.StudentParents)
            .ThenInclude(sp => sp.Parent)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (student is null)
            throw new StudentException("student_not_found", 404);

        // Parent: must be linked
        if (role == UserRole.Parent)
        {
            if (!student.StudentParents.Any(sp => sp.ParentId == userId))
                throw new StudentException("not_linked_to_student", 403);
        }

        return MapToDto(student);
    }

    public async Task<StudentDto> CreateAsync(CreateStudentRequest request, Guid tenantId)
    {
        ValidateFields(request.Nome, request.DataNascimento, request.Turma, request.AnoLetivo);
        ValidateFoto(request.Foto);

        // CPF duplicate check
        if (!string.IsNullOrWhiteSpace(request.Cpf))
        {
            var cpfExists = await _db.Students
                .AnyAsync(s => s.Cpf == request.Cpf);
            if (cpfExists)
                throw new StudentException("cpf_already_exists", 409);
        }

        var student = new Student
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = request.Nome,
            DataNascimento = request.DataNascimento,
            Cpf = request.Cpf,
            Turma = request.Turma,
            AnoLetivo = request.AnoLetivo,
            Status = StudentStatus.Ativo,
            Observacoes = request.Observacoes,
            Foto = request.Foto,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Students.Add(student);
        await _db.SaveChangesAsync();

        return MapToDto(student);
    }

    public async Task<StudentDto> UpdateAsync(Guid id, UpdateStudentRequest request, Guid tenantId)
    {
        var student = await _db.Students
            .FirstOrDefaultAsync(s => s.Id == id);

        if (student is null)
            throw new StudentException("student_not_found", 404);

        ValidateFields(request.Nome, request.DataNascimento, request.Turma, request.AnoLetivo);
        ValidateFoto(request.Foto);

        // CPF duplicate check (exclude self)
        if (!string.IsNullOrWhiteSpace(request.Cpf))
        {
            var cpfExists = await _db.Students
                .AnyAsync(s => s.Cpf == request.Cpf && s.Id != id);
            if (cpfExists)
                throw new StudentException("cpf_already_exists", 409);
        }

        student.Nome = request.Nome;
        student.DataNascimento = request.DataNascimento;
        student.Cpf = request.Cpf;
        student.Turma = request.Turma;
        student.AnoLetivo = request.AnoLetivo;
        student.Observacoes = request.Observacoes;
        student.Foto = request.Foto;
        student.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return MapToDto(student);
    }

    public async Task DeleteAsync(Guid id, Guid tenantId)
    {
        var student = await _db.Students
            .FirstOrDefaultAsync(s => s.Id == id);

        if (student is null)
            throw new StudentException("student_not_found", 404);

        // Idempotent: already inactive
        if (student.Status == StudentStatus.Inativo)
            return;

        // Check for active (approved) enrollment before soft-deleting
        var hasActiveEnrollment = await _db.Enrollments
            .AnyAsync(e => e.StudentId == id && e.Status == EnrollmentStatus.Aprovado);
        if (hasActiveEnrollment)
            throw new StudentException("student_has_active_enrollment", 409);

        student.Status = StudentStatus.Inativo;
        student.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }

    public async Task LinkParentAsync(Guid studentId, Guid parentId, Guid tenantId)
    {
        var student = await _db.Students
            .FirstOrDefaultAsync(s => s.Id == studentId);

        if (student is null)
            throw new StudentException("student_not_found", 404);

        var parent = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == parentId);

        if (parent is null)
            throw new StudentException("parent_not_found", 404);

        if (parent.TenantId != tenantId)
            throw new StudentException("parent_belongs_to_different_tenant", 400);

        if (parent.Role != UserRole.Parent)
            throw new StudentException("user_is_not_parent", 400);

        var alreadyLinked = await _db.StudentParents
            .AnyAsync(sp => sp.StudentId == studentId && sp.ParentId == parentId);

        if (alreadyLinked)
            throw new StudentException("parent_already_linked", 409);

        var link = new StudentParent
        {
            StudentId = studentId,
            ParentId = parentId
        };

        _db.StudentParents.Add(link);
        await _db.SaveChangesAsync();
    }

    public async Task UnlinkParentAsync(Guid studentId, Guid parentId, Guid tenantId)
    {
        var link = await _db.StudentParents
            .FirstOrDefaultAsync(sp => sp.StudentId == studentId && sp.ParentId == parentId);

        if (link is null)
            throw new StudentException("link_not_found", 404);

        _db.StudentParents.Remove(link);
        await _db.SaveChangesAsync();
    }

    public async Task<List<EnrollmentDto>> GetEnrollmentHistoryAsync(Guid studentId, Guid tenantId, Guid userId, UserRole role)
    {
        // Parent: must be linked to student
        if (role == UserRole.Parent)
        {
            var isLinked = await _db.StudentParents
                .AnyAsync(sp => sp.StudentId == studentId && sp.ParentId == userId);
            if (!isLinked)
                throw new StudentException("not_linked_to_student", 403);
        }

        var enrollments = await _db.Enrollments
            .Include(e => e.Period)
            .Where(e => e.StudentId == studentId)
            .OrderByDescending(e => e.Period.AnoLetivo)
            .ThenByDescending(e => e.CreatedAt)
            .Select(e => new EnrollmentDto(
                e.Id,
                e.StudentId,
                e.Student.Nome,
                e.EnrollmentPeriodId,
                e.Period.Nome,
                e.Status.ToString(),
                e.MotivoRejeicao,
                e.CreatedAt,
                e.ApprovedAt))
            .ToListAsync();

        return enrollments;
    }

    /// <summary>Tamanho máximo da foto como data URL (~150 KB de imagem).</summary>
    private const int MaxFotoLength = 200_000;

    /// <summary>
    /// A foto chega do cliente como data URL Base64: valida tamanho e formato antes de persistir.
    /// </summary>
    private static void ValidateFoto(string? foto)
    {
        if (foto is null)
            return;
        if (foto.Length > MaxFotoLength)
            throw new StudentException("foto_too_large", 400);
        if (!foto.StartsWith("data:image/", StringComparison.Ordinal))
            throw new StudentException("foto_invalid_format", 400);
    }

    private static void ValidateFields(string nome, DateTime dataNascimento, string turma, int anoLetivo)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new StudentException("nome_required", 400);
        if (nome.Length > 200)
            throw new StudentException("nome_too_long", 400);
        if (dataNascimento >= DateTime.UtcNow)
            throw new StudentException("data_nascimento_must_be_past", 400);
        if (string.IsNullOrWhiteSpace(turma))
            throw new StudentException("turma_required", 400);
        if (turma.Length > 50)
            throw new StudentException("turma_too_long", 400);
        if (anoLetivo < 2000)
            throw new StudentException("ano_letivo_invalid", 400);
    }

    private static StudentDto MapToDto(Student s)
    {
        return new StudentDto(
            s.Id,
            s.Nome,
            s.DataNascimento,
            s.Cpf,
            s.Turma,
            s.AnoLetivo,
            s.Status.ToString(),
            s.Observacoes,
            s.Foto,
            s.CreatedAt,
            s.StudentParents.Select(sp => new ParentLinkDto(
                sp.ParentId,
                sp.Parent.Name,
                sp.Parent.Email ?? string.Empty
            )).ToList()
        );
    }
}

public class StudentException : Exception
{
    public int StatusCode { get; }

    public StudentException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}
