using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;

namespace Ciclo.Infrastructure.Services;

/// <summary>
/// Spec 150 — dono único da leitura administrativa e da escrita da estrutura acadêmica
/// (ano letivo, turma, componente curricular, avaliação) e do lançamento de notas.
///
/// Toda consulta passa pelo filtro global de tenant do <see cref="AppDbContext"/>: um
/// identificador de outro tenant não é encontrado e a operação responde <c>not_found</c>.
/// Não existe <c>IgnoreQueryFilters</c> aqui — a escrita administrativa nunca fura o isolamento.
///
/// Cada operação grava uma única entidade e chama <c>SaveChangesAsync</c> uma vez, de modo que
/// uma falha de validação não deixa linha parcial.
/// </summary>
public interface IAcademicService
{
    Task<List<AcademicYearDto>> ListYearsAsync(CancellationToken cancellationToken = default);
    Task<AcademicYearDto> CreateYearAsync(CreateAcademicYearRequest request, CancellationToken cancellationToken = default);
    Task<AcademicYearDto> UpdateYearAsync(Guid id, UpdateAcademicYearRequest request, CancellationToken cancellationToken = default);
    Task DeleteYearAsync(Guid id, CancellationToken cancellationToken = default);

    Task<List<SchoolClassDto>> ListClassesAsync(Guid? academicYearId, CancellationToken cancellationToken = default);
    Task<SchoolClassDto> CreateClassAsync(CreateSchoolClassRequest request, CancellationToken cancellationToken = default);
    Task<SchoolClassDto> UpdateClassAsync(Guid id, UpdateSchoolClassRequest request, CancellationToken cancellationToken = default);
    Task DeleteClassAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<StudentDto>> ListClassStudentsAsync(Guid classId, CancellationToken cancellationToken = default);

    Task<List<CurriculumComponentDto>> ListComponentsAsync(CancellationToken cancellationToken = default);
    Task<CurriculumComponentDto> CreateComponentAsync(CreateCurriculumComponentRequest request, CancellationToken cancellationToken = default);
    Task<CurriculumComponentDto> UpdateComponentAsync(Guid id, UpdateCurriculumComponentRequest request, CancellationToken cancellationToken = default);
    Task DeleteComponentAsync(Guid id, CancellationToken cancellationToken = default);

    Task<List<AssessmentDto>> ListAssessmentsAsync(Guid? schoolClassId, Guid? curriculumComponentId, CancellationToken cancellationToken = default);
    Task<AssessmentDto> CreateAssessmentAsync(CreateAssessmentRequest request, CancellationToken cancellationToken = default);
    Task<AssessmentDto> UpdateAssessmentAsync(Guid id, UpdateAssessmentRequest request, CancellationToken cancellationToken = default);
    Task DeleteAssessmentAsync(Guid id, CancellationToken cancellationToken = default);

    Task<GradeDto> UpsertGradeAsync(Guid assessmentId, Guid studentId, decimal value, CancellationToken cancellationToken = default);
}

public class AcademicService : IAcademicService
{
    private readonly AppDbContext _db;

    public AcademicService(AppDbContext db)
    {
        _db = db;
    }

    // ── Anos letivos ────────────────────────────────────────────────

    public async Task<List<AcademicYearDto>> ListYearsAsync(CancellationToken cancellationToken = default) =>
        await _db.AcademicYears
            .OrderByDescending(y => y.Year)
            .Select(ToYearDto)
            .ToListAsync(cancellationToken);

    public async Task<AcademicYearDto> CreateYearAsync(
        CreateAcademicYearRequest request, CancellationToken cancellationToken = default)
    {
        ValidateYear(request.Year, request.StartDate, request.EndDate);

        if (await _db.AcademicYears.AnyAsync(y => y.Year == request.Year, cancellationToken))
            throw new AcademicException("academic_year_already_exists", 409);

        var year = new AcademicYear
        {
            Id = Guid.NewGuid(),
            Year = request.Year,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsActive = request.IsActive
        };

        _db.AcademicYears.Add(year);
        await SaveAsync("academic_year_already_exists", cancellationToken);

        return await ProjectYearAsync(year.Id, cancellationToken);
    }

    public async Task<AcademicYearDto> UpdateYearAsync(
        Guid id, UpdateAcademicYearRequest request, CancellationToken cancellationToken = default)
    {
        ValidateYear(request.Year, request.StartDate, request.EndDate);

        var year = await _db.AcademicYears.FirstOrDefaultAsync(y => y.Id == id, cancellationToken)
            ?? throw NotFound();

        if (await _db.AcademicYears.AnyAsync(y => y.Year == request.Year && y.Id != id, cancellationToken))
            throw new AcademicException("academic_year_already_exists", 409);

        year.Year = request.Year;
        year.StartDate = request.StartDate;
        year.EndDate = request.EndDate;
        year.IsActive = request.IsActive;

        await SaveAsync("academic_year_already_exists", cancellationToken);

        return await ProjectYearAsync(year.Id, cancellationToken);
    }

    public async Task DeleteYearAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var year = await _db.AcademicYears.FirstOrDefaultAsync(y => y.Id == id, cancellationToken)
            ?? throw NotFound();

        // Turmas e alunos dependem do ano: excluir deixaria os dois pendurados em um identificador
        // que não existe mais. A verificação é explícita porque o provedor em memória dos testes
        // não aplica a chave estrangeira Restrict.
        if (await _db.SchoolClasses.AnyAsync(c => c.AcademicYearId == id, cancellationToken)
            || await _db.Students.AnyAsync(s => s.AcademicYearId == id, cancellationToken))
            throw new AcademicException("academic_year_in_use", 409);

        _db.AcademicYears.Remove(year);
        await SaveAsync("academic_year_in_use", cancellationToken);
    }

    // ── Turmas ──────────────────────────────────────────────────────

    public async Task<List<SchoolClassDto>> ListClassesAsync(
        Guid? academicYearId, CancellationToken cancellationToken = default)
    {
        IQueryable<SchoolClass> query = _db.SchoolClasses;

        if (academicYearId is not null)
            query = query.Where(c => c.AcademicYearId == academicYearId.Value);

        return await query
            .OrderBy(c => c.Name)
            .Select(ToClassDto)
            .ToListAsync(cancellationToken);
    }

    public async Task<SchoolClassDto> CreateClassAsync(
        CreateSchoolClassRequest request, CancellationToken cancellationToken = default)
    {
        var name = RequiredTrimmed(request.Name, "class_name_required", 50, "class_name_too_long");

        var academicYear = await _db.AcademicYears
            .FirstOrDefaultAsync(y => y.Id == request.AcademicYearId, cancellationToken)
            ?? throw new AcademicException("academic_year_not_found", 404);

        if (await _db.SchoolClasses
                .AnyAsync(c => c.AcademicYearId == academicYear.Id && c.Name == name, cancellationToken))
            throw new AcademicException("class_name_already_exists", 409);

        var schoolClass = new SchoolClass
        {
            Id = Guid.NewGuid(),
            Name = name,
            AcademicYearId = academicYear.Id,
            AcademicYear = academicYear
        };

        _db.SchoolClasses.Add(schoolClass);
        await SaveAsync("class_name_already_exists", cancellationToken);

        return await ProjectClassAsync(schoolClass.Id, cancellationToken);
    }

    public async Task<SchoolClassDto> UpdateClassAsync(
        Guid id, UpdateSchoolClassRequest request, CancellationToken cancellationToken = default)
    {
        var name = RequiredTrimmed(request.Name, "class_name_required", 50, "class_name_too_long");

        var schoolClass = await _db.SchoolClasses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw NotFound();

        var academicYear = await _db.AcademicYears
            .FirstOrDefaultAsync(y => y.Id == request.AcademicYearId, cancellationToken)
            ?? throw new AcademicException("academic_year_not_found", 404);

        if (await _db.SchoolClasses.AnyAsync(
                c => c.AcademicYearId == academicYear.Id && c.Name == name && c.Id != id, cancellationToken))
            throw new AcademicException("class_name_already_exists", 409);

        schoolClass.Name = name;
        schoolClass.AcademicYearId = academicYear.Id;
        schoolClass.AcademicYear = academicYear;

        await SaveAsync("class_name_already_exists", cancellationToken);

        return await ProjectClassAsync(schoolClass.Id, cancellationToken);
    }

    public async Task DeleteClassAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var schoolClass = await _db.SchoolClasses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw NotFound();

        // Alunos e avaliações da turma bloqueiam a exclusão: mover os alunos é decisão do
        // administrador, não efeito colateral de um DELETE.
        if (await _db.Students.AnyAsync(s => s.SchoolClassId == id, cancellationToken)
            || await _db.Assessments.AnyAsync(a => a.SchoolClassId == id, cancellationToken))
            throw new AcademicException("school_class_in_use", 409);

        _db.SchoolClasses.Remove(schoolClass);
        await SaveAsync("school_class_in_use", cancellationToken);
    }

    public async Task<List<StudentDto>> ListClassStudentsAsync(
        Guid classId, CancellationToken cancellationToken = default)
    {
        var exists = await _db.SchoolClasses.AnyAsync(c => c.Id == classId, cancellationToken);
        if (!exists)
            throw NotFound();

        return await _db.Students
            .Where(s => s.SchoolClassId == classId)
            .OrderBy(s => s.Nome)
            .Select(s => new StudentDto(
                s.Id, s.Nome, s.DataNascimento, s.Cpf, s.Turma, s.AnoLetivo,
                s.Status.ToString(), s.Observacoes, s.Foto, s.CreatedAt, new List<ParentLinkDto>()))
            .ToListAsync(cancellationToken);
    }

    // ── Componentes curriculares ────────────────────────────────────

    public async Task<List<CurriculumComponentDto>> ListComponentsAsync(
        CancellationToken cancellationToken = default) =>
        await _db.CurriculumComponents
            .OrderBy(c => c.Name)
            .Select(ToComponentDto)
            .ToListAsync(cancellationToken);

    public async Task<CurriculumComponentDto> CreateComponentAsync(
        CreateCurriculumComponentRequest request, CancellationToken cancellationToken = default)
    {
        var name = RequiredTrimmed(request.Name, "component_name_required", 100, "component_name_too_long");
        var code = RequiredTrimmed(request.Code, "component_code_required", 20, "component_code_too_long");

        if (await _db.CurriculumComponents.AnyAsync(c => c.Code == code, cancellationToken))
            throw new AcademicException("component_code_already_exists", 409);

        var component = new CurriculumComponent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Code = code,
            IsActive = request.IsActive
        };

        _db.CurriculumComponents.Add(component);
        await SaveAsync("component_code_already_exists", cancellationToken);

        return await ProjectComponentAsync(component.Id, cancellationToken);
    }

    public async Task<CurriculumComponentDto> UpdateComponentAsync(
        Guid id, UpdateCurriculumComponentRequest request, CancellationToken cancellationToken = default)
    {
        var name = RequiredTrimmed(request.Name, "component_name_required", 100, "component_name_too_long");
        var code = RequiredTrimmed(request.Code, "component_code_required", 20, "component_code_too_long");

        var component = await _db.CurriculumComponents.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw NotFound();

        if (await _db.CurriculumComponents.AnyAsync(c => c.Code == code && c.Id != id, cancellationToken))
            throw new AcademicException("component_code_already_exists", 409);

        component.Name = name;
        component.Code = code;
        component.IsActive = request.IsActive;

        await SaveAsync("component_code_already_exists", cancellationToken);

        return await ProjectComponentAsync(component.Id, cancellationToken);
    }

    public async Task DeleteComponentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var component = await _db.CurriculumComponents.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw NotFound();

        if (await _db.Assessments.AnyAsync(a => a.CurriculumComponentId == id, cancellationToken))
            throw new AcademicException("component_in_use", 409);

        _db.CurriculumComponents.Remove(component);
        await SaveAsync("component_in_use", cancellationToken);
    }

    // ── Avaliações ──────────────────────────────────────────────────

    public async Task<List<AssessmentDto>> ListAssessmentsAsync(
        Guid? schoolClassId, Guid? curriculumComponentId, CancellationToken cancellationToken = default)
    {
        IQueryable<Assessment> query = _db.Assessments;

        if (schoolClassId is not null)
            query = query.Where(a => a.SchoolClassId == schoolClassId.Value);

        if (curriculumComponentId is not null)
            query = query.Where(a => a.CurriculumComponentId == curriculumComponentId.Value);

        return await query
            .OrderBy(a => a.Date)
            .ThenBy(a => a.Title)
            .Select(ToAssessmentDto)
            .ToListAsync(cancellationToken);
    }

    public async Task<AssessmentDto> CreateAssessmentAsync(
        CreateAssessmentRequest request, CancellationToken cancellationToken = default)
    {
        var title = RequiredTrimmed(request.Title, "assessment_title_required", 100, "assessment_title_too_long");
        ValidateWeight(request.Weight);

        var schoolClass = await _db.SchoolClasses
            .FirstOrDefaultAsync(c => c.Id == request.SchoolClassId, cancellationToken)
            ?? throw new AcademicException("school_class_not_found", 404);

        var component = await _db.CurriculumComponents
            .FirstOrDefaultAsync(c => c.Id == request.CurriculumComponentId, cancellationToken)
            ?? throw new AcademicException("component_not_found", 404);

        if (await _db.Assessments.AnyAsync(
                a => a.SchoolClassId == schoolClass.Id
                     && a.CurriculumComponentId == component.Id
                     && a.Title == title, cancellationToken))
            throw new AcademicException("assessment_title_already_exists", 409);

        var assessment = new Assessment
        {
            Id = Guid.NewGuid(),
            SchoolClassId = schoolClass.Id,
            CurriculumComponentId = component.Id,
            Title = title,
            Weight = request.Weight,
            Date = request.Date,
            IsRecovery = request.IsRecovery
        };

        _db.Assessments.Add(assessment);
        await SaveAsync("assessment_title_already_exists", cancellationToken);

        return await ProjectAssessmentAsync(assessment.Id, cancellationToken);
    }

    public async Task<AssessmentDto> UpdateAssessmentAsync(
        Guid id, UpdateAssessmentRequest request, CancellationToken cancellationToken = default)
    {
        var title = RequiredTrimmed(request.Title, "assessment_title_required", 100, "assessment_title_too_long");
        ValidateWeight(request.Weight);

        var assessment = await _db.Assessments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw NotFound();

        var schoolClass = await _db.SchoolClasses
            .FirstOrDefaultAsync(c => c.Id == request.SchoolClassId, cancellationToken)
            ?? throw new AcademicException("school_class_not_found", 404);

        var component = await _db.CurriculumComponents
            .FirstOrDefaultAsync(c => c.Id == request.CurriculumComponentId, cancellationToken)
            ?? throw new AcademicException("component_not_found", 404);

        if (await _db.Assessments.AnyAsync(
                a => a.SchoolClassId == schoolClass.Id
                     && a.CurriculumComponentId == component.Id
                     && a.Title == title
                     && a.Id != id, cancellationToken))
            throw new AcademicException("assessment_title_already_exists", 409);

        assessment.SchoolClassId = schoolClass.Id;
        assessment.CurriculumComponentId = component.Id;
        assessment.Title = title;
        assessment.Weight = request.Weight;
        assessment.Date = request.Date;
        assessment.IsRecovery = request.IsRecovery;

        await SaveAsync("assessment_title_already_exists", cancellationToken);

        return await ProjectAssessmentAsync(assessment.Id, cancellationToken);
    }

    public async Task DeleteAssessmentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var assessment = await _db.Assessments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw NotFound();

        if (await _db.Grades.AnyAsync(g => g.AssessmentId == id, cancellationToken))
            throw new AcademicException("assessment_has_grades", 409);

        _db.Assessments.Remove(assessment);
        await SaveAsync("assessment_has_grades", cancellationToken);
    }

    // ── Lançamento de nota ──────────────────────────────────────────

    /// <summary>
    /// Lançamento idempotente: o par avaliação/aluno é a chave natural, então a segunda chamada
    /// atualiza a linha existente em vez de criar outra. O índice único
    /// <c>(AssessmentId, StudentId)</c> é o árbitro de uma corrida entre duas instâncias: a
    /// perdedora relê a linha vencedora e a atualiza.
    /// </summary>
    public async Task<GradeDto> UpsertGradeAsync(
        Guid assessmentId, Guid studentId, decimal value, CancellationToken cancellationToken = default)
    {
        if (value < 0m || value > 10m)
            throw new AcademicException("grade_out_of_range", 400);

        var assessment = await _db.Assessments
            .Include(a => a.CurriculumComponent)
            .FirstOrDefaultAsync(a => a.Id == assessmentId, cancellationToken)
            ?? throw NotFound();

        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId, cancellationToken)
            ?? throw NotFound();

        // Nota de aluno de outra turma corromperia a média do componente: é conflito, não 404,
        // porque os dois recursos existem no tenant.
        if (student.SchoolClassId != assessment.SchoolClassId)
            throw new AcademicException("student_not_in_class", 409);

        var recordedAt = DateTime.UtcNow;
        var grade = await _db.Grades
            .FirstOrDefaultAsync(g => g.AssessmentId == assessmentId && g.StudentId == studentId, cancellationToken);

        if (grade is null)
        {
            grade = new Grade
            {
                Id = Guid.NewGuid(),
                AssessmentId = assessmentId,
                StudentId = studentId,
                Value = value,
                RecordedAt = recordedAt
            };

            _db.Grades.Add(grade);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Corrida perdida: descarta a linha recusada pelo índice único e atualiza a
                // vencedora, preservando o contrato idempotente do lançamento.
                _db.Entry(grade).State = EntityState.Detached;

                var winner = await _db.Grades.FirstOrDefaultAsync(
                    g => g.AssessmentId == assessmentId && g.StudentId == studentId, cancellationToken);

                // Sem linha vencedora, a violação não foi a corrida esperada: a exceção original sobe
                // sem tradução, como manda o contrato do serviço.
                if (winner is null)
                    throw;

                winner.Value = value;
                winner.RecordedAt = recordedAt;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            grade.Value = value;
            grade.RecordedAt = recordedAt;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new GradeDto(assessment.CurriculumComponent.Name, value, null);
    }

    // ── Infraestrutura ──────────────────────────────────────────────

    private static readonly Expression<Func<AcademicYear, AcademicYearDto>> ToYearDto =
        y => new AcademicYearDto(y.Id, y.Year, y.StartDate, y.EndDate, y.IsActive);

    private static readonly Expression<Func<SchoolClass, SchoolClassDto>> ToClassDto =
        c => new SchoolClassDto(c.Id, c.Name, c.AcademicYearId, c.AcademicYear.Year);

    private static readonly Expression<Func<CurriculumComponent, CurriculumComponentDto>> ToComponentDto =
        c => new CurriculumComponentDto(c.Id, c.Name, c.Code, c.IsActive);

    private static readonly Expression<Func<Assessment, AssessmentDto>> ToAssessmentDto =
        a => new AssessmentDto(a.Id, a.SchoolClassId, a.CurriculumComponentId, a.Title, a.Weight, a.Date, a.IsRecovery);

    private Task<AcademicYearDto> ProjectYearAsync(Guid id, CancellationToken cancellationToken) =>
        _db.AcademicYears.Where(y => y.Id == id).Select(ToYearDto).FirstAsync(cancellationToken);

    private Task<SchoolClassDto> ProjectClassAsync(Guid id, CancellationToken cancellationToken) =>
        _db.SchoolClasses.Where(c => c.Id == id).Select(ToClassDto).FirstAsync(cancellationToken);

    private Task<CurriculumComponentDto> ProjectComponentAsync(Guid id, CancellationToken cancellationToken) =>
        _db.CurriculumComponents.Where(c => c.Id == id).Select(ToComponentDto).FirstAsync(cancellationToken);

    private Task<AssessmentDto> ProjectAssessmentAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Assessments.Where(a => a.Id == id).Select(ToAssessmentDto).FirstAsync(cancellationToken);

    /// <summary>
    /// Gravação única da operação. Uma violação de índice único que escape das verificações
    /// prévias é uma corrida entre duas instâncias: o código de conflito da operação é a
    /// tradução correta. Qualquer outra exceção sobe sem tradução.
    /// </summary>
    private async Task SaveAsync(string conflictCode, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new AcademicException(conflictCode, 409, ex);
        }
    }

    private static AcademicException NotFound() => new("not_found", 404);

    private static void ValidateYear(int year, DateOnly startDate, DateOnly endDate)
    {
        if (year <= 0)
            throw new AcademicException("academic_year_invalid", 400);

        if (endDate < startDate)
            throw new AcademicException("academic_year_range_invalid", 400);
    }

    private static void ValidateWeight(decimal weight)
    {
        if (weight <= 0m)
            throw new AcademicException("assessment_weight_invalid", 400);
    }

    private static string RequiredTrimmed(string? value, string requiredCode, int maxLength, string tooLongCode)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
            throw new AcademicException(requiredCode, 400);

        if (trimmed.Length > maxLength)
            throw new AcademicException(tooLongCode, 400);

        return trimmed;
    }
}

/// <summary>
/// Falha de domínio da estrutura acadêmica: o código é parte do contrato da API e o
/// <c>ExceptionMiddleware</c> o devolve no corpo no formato canônico.
/// </summary>
public class AcademicException : Exception
{
    public int StatusCode { get; }

    public AcademicException(string message, int statusCode, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
