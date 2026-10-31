using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Ciclo.Core.Entities;
using Ciclo.Core.Validation;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Security;
using Ciclo.Infrastructure.Storage;

namespace Ciclo.Infrastructure.Services;

public interface IStudentService
{
    Task<PagedResponse<StudentDto>> GetAllAsync(string? search, string? turma, string? status, int page, int pageSize, Guid tenantId, Guid userId, UserRole role);
    Task<StudentDto> GetByIdAsync(Guid id, Guid tenantId, Guid userId, UserRole role);
    Task<StudentDto> CreateAsync(CreateStudentRequest request, Guid tenantId, UserRole role);
    Task<StudentDto> UpdateAsync(Guid id, UpdateStudentRequest request, Guid tenantId, UserRole role);
    Task DeleteAsync(Guid id, Guid tenantId, UserRole role);
    Task LinkParentAsync(Guid studentId, Guid parentId, Guid tenantId, UserRole role);
    Task UnlinkParentAsync(Guid studentId, Guid parentId, Guid tenantId, UserRole role);
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
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        IQueryable<Student> query = _db.Students;

        // B9: a regra de vínculo tem um dono único (StudentAccess).
        query = query.VisibleTo(userId, role);

        // Filters
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();
        var normalizedTurma = string.IsNullOrWhiteSpace(turma) ? null : turma.Trim().ToLowerInvariant();

        // As expressões traduzidas ficam em `StudentQueries` (dono único, provado contra Npgsql).
        if (term is not null)
            query = query.WhereNameContains(term);

        if (normalizedTurma is not null)
            query = query.WhereTurma(normalizedTurma);

        if (!string.IsNullOrWhiteSpace(status))
        {
            // Filtro inválido não pode ser ignorado em silêncio: devolver a lista inteira faria a
            // tela mostrar "todos" quando o operador pediu um status que não existe.
            // TryParse sozinho ainda aceita valor numérico fora do enum ("99"), por isso IsDefined.
            if (!Enum.TryParse<StudentStatus>(status.Trim(), ignoreCase: true, out var parsedStatus)
                || !Enum.IsDefined(parsedStatus))
                throw new StudentException("status_invalid", 400);

            query = query.Where(s => s.Status == parsedStatus);
        }

        var total = await query.CountAsync();

        var students = await query
            .OrderBy(s => s.Nome)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto)
            .ToListAsync();

        return new PagedResponse<StudentDto>(students, total, page, pageSize);
    }

    public async Task<StudentDto> GetByIdAsync(Guid id, Guid tenantId, Guid userId, UserRole role)
    {
        await StudentAccess.EnsureLinkedAsync(_db, id, userId, role);

        var student = await ProjectAsync(id);

        if (student is null)
            throw new StudentException("student_not_found", 404);

        return student;
    }

    public async Task<StudentDto> CreateAsync(CreateStudentRequest request, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.StudentWrite, role);

        ValidateFields(request.Nome, request.DataNascimento, request.Turma, request.AnoLetivo);
        ValidateFoto(request.Foto);
        var cpf = NormalizeOptionalCpf(request.Cpf);

        // CPF duplicate check
        if (cpf is not null)
        {
            var cpfExists = await _db.Students
                .AnyAsync(s => s.Cpf == cpf);
            if (cpfExists)
                throw new StudentException("cpf_already_exists", 409);
        }

        var student = new Student
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = request.Nome,
            DataNascimento = request.DataNascimento,
            Cpf = cpf,
            Turma = request.Turma,
            AnoLetivo = request.AnoLetivo,
            Status = StudentStatus.Ativo,
            Observacoes = request.Observacoes,
            Foto = request.Foto,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Students.Add(student);
        await ResolveAcademicStructureAsync(student, default);
        await _db.SaveChangesAsync();

        return await ProjectAsync(student.Id)
            ?? throw new StudentException("student_not_found", 404);
    }

    public async Task<StudentDto> UpdateAsync(Guid id, UpdateStudentRequest request, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.StudentWrite, role);

        var student = await _db.Students
            .FirstOrDefaultAsync(s => s.Id == id);

        if (student is null)
            throw new StudentException("student_not_found", 404);

        ValidateFields(request.Nome, request.DataNascimento, request.Turma, request.AnoLetivo);
        ValidateFoto(request.Foto);
        var cpf = NormalizeOptionalCpf(request.Cpf);

        // CPF duplicate check (exclude self)
        if (cpf is not null)
        {
            var cpfExists = await _db.Students
                .AnyAsync(s => s.Cpf == cpf && s.Id != id);
            if (cpfExists)
                throw new StudentException("cpf_already_exists", 409);
        }

        student.Nome = request.Nome;
        student.DataNascimento = request.DataNascimento;
        student.Cpf = cpf;
        student.Turma = request.Turma;
        student.AnoLetivo = request.AnoLetivo;
        student.Observacoes = request.Observacoes;
        student.Foto = request.Foto;
        student.UpdatedAt = DateTime.UtcNow;

        await ResolveAcademicStructureAsync(student, default);
        await _db.SaveChangesAsync();

        return await ProjectAsync(student.Id)
            ?? throw new StudentException("student_not_found", 404);
    }

    public async Task DeleteAsync(Guid id, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.StudentDelete, role);

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

    public async Task LinkParentAsync(Guid studentId, Guid parentId, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.StudentWrite, role);

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

    public async Task UnlinkParentAsync(Guid studentId, Guid parentId, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.StudentWrite, role);

        var link = await _db.StudentParents
            .FirstOrDefaultAsync(sp => sp.StudentId == studentId && sp.ParentId == parentId);

        if (link is null)
            throw new StudentException("link_not_found", 404);

        _db.StudentParents.Remove(link);
        await _db.SaveChangesAsync();
    }

    public async Task<List<EnrollmentDto>> GetEnrollmentHistoryAsync(Guid studentId, Guid tenantId, Guid userId, UserRole role)
    {
        await StudentAccess.EnsureLinkedAsync(_db, studentId, userId, role);

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

    /// <summary>
    /// Spec 150 — turma e ano chegam como texto no contrato atual, mas passam a ser persistidos
    /// como referência. Turma vazia ou ano não positivo deixam os dois vínculos nulos, sem criar
    /// estrutura. Roda antes do <c>SaveChangesAsync</c> da operação: falha aqui não deixa aluno
    /// gravado pela metade.
    /// </summary>
    private async Task ResolveAcademicStructureAsync(Student student, CancellationToken cancellationToken)
    {
        var turma = student.Turma.Trim();
        student.Turma = turma;

        if (student.AnoLetivo <= 0 || turma.Length == 0)
        {
            student.AcademicYearId = null;
            student.SchoolClassId = null;
            return;
        }

        var academicYear = await _db.AcademicYears
            .FirstOrDefaultAsync(y => y.Year == student.AnoLetivo, cancellationToken);

        if (academicYear is null)
        {
            academicYear = new AcademicYear
            {
                Id = Guid.NewGuid(),
                TenantId = student.TenantId,
                Year = student.AnoLetivo,
                StartDate = new DateOnly(student.AnoLetivo, 1, 1),
                EndDate = new DateOnly(student.AnoLetivo, 12, 31),
            };
            _db.AcademicYears.Add(academicYear);
        }

        var schoolClass = await _db.SchoolClasses
            .FirstOrDefaultAsync(
                c => c.AcademicYearId == academicYear.Id && c.Name == turma,
                cancellationToken);

        if (schoolClass is null)
        {
            schoolClass = new SchoolClass
            {
                Id = Guid.NewGuid(),
                TenantId = student.TenantId,
                Name = turma,
                AcademicYearId = academicYear.Id,
                AcademicYear = academicYear,
            };
            _db.SchoolClasses.Add(schoolClass);
        }

        student.AcademicYearId = academicYear.Id;
        student.SchoolClassId = schoolClass.Id;
    }

    /// <summary>
    /// Tamanho máximo da foto como data URL (~150 KB de imagem). Mantido por compatibilidade;
    /// o limite real é <see cref="ImageDataUrl.MaxLength"/>.
    /// </summary>
    private const int MaxFotoLength = ImageDataUrl.MaxLength;

    /// <summary>
    /// A foto chega do cliente como data URL Base64: valida tamanho, formato (lista fechada:
    /// PNG, JPEG ou WebP) e a assinatura do conteúdo. SVG fica de fora — ele executa script
    /// quando aberto no navegador e o prefixo `data:image/` o aceitava (M6).
    /// </summary>
    private static void ValidateFoto(string? foto)
    {
        if (foto is null)
            return;
        if (foto.Length > MaxFotoLength)
            throw new StudentException("foto_too_large", 400);
        if (!ImageDataUrl.IsSupported(foto))
            throw new StudentException("foto_invalid_format", 400);
    }

    /// <summary>CPF vazio vira nulo; preenchido precisa passar nos dígitos verificadores e é
    /// gravado só com dígitos, para "123.456.789-09" e "12345678909" não virarem duplicatas.</summary>
    private static string? NormalizeOptionalCpf(string? cpf)
    {
        if (string.IsNullOrWhiteSpace(cpf))
            return null;
        if (!Cpf.IsValid(cpf))
            throw new StudentException("cpf_invalid", 400);

        return Cpf.Normalize(cpf);
    }

    private static void ValidateFields(string nome, DateTime dataNascimento, string turma, int anoLetivo)
    {
        var now = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(nome))
            throw new StudentException("nome_required", 400);
        if (nome.Length > 200)
            throw new StudentException("nome_too_long", 400);
        if (dataNascimento >= now)
            throw new StudentException("data_nascimento_must_be_past", 400);

        // Faixa de idade: uma data há 1 mês ou há 200 anos é erro de digitação, não um aluno.
        // O piso de 2 anos é o mínimo fora da creche; o teto de 120 é o limite humano.
        if (dataNascimento > now.AddYears(-2))
            throw new StudentException("data_nascimento_too_recent", 400);
        if (dataNascimento < now.AddYears(-120))
            throw new StudentException("data_nascimento_too_old", 400);

        if (string.IsNullOrWhiteSpace(turma))
            throw new StudentException("turma_required", 400);
        if (turma.Length > 50)
            throw new StudentException("turma_too_long", 400);

        // O ano letivo é o corrente ou, no máximo, o seguinte (planejamento). Sem teto, 99999
        // era aceito e contaminava relatórios e a matrícula.
        if (anoLetivo < 2000 || anoLetivo > now.Year + 1)
            throw new StudentException("ano_letivo_invalid", 400);
    }

    /// <summary>
    /// Projeção única do <see cref="StudentDto"/>. Antes existiam três caminhos — a
    /// projeção da listagem, os <c>Include</c> do detalhe e um mapeamento em memória —
    /// e o <c>Update</c> devolvia <c>parents: []</c> porque mapeava uma entidade sem o
    /// vínculo carregado (M1). Agora todo caminho devolve o mesmo shape.
    /// </summary>
    private static readonly Expression<Func<Student, StudentDto>> ToDto = s => new StudentDto(
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
            sp.Parent.Email ?? string.Empty)).ToList());

    private Task<StudentDto?> ProjectAsync(Guid id) =>
        _db.Students.Where(s => s.Id == id).Select(ToDto).FirstOrDefaultAsync();
}

public class StudentException : Exception
{
    public int StatusCode { get; }

    public StudentException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}
