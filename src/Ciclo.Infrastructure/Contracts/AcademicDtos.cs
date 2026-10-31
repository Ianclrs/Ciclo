namespace Ciclo.Infrastructure.Contracts;

// Spec 150 — contratos da estrutura acadêmica e do lançamento de notas.
// São apenas transporte: a regra (unicidade, faixa, dependência) vive no AcademicService.

public record AcademicYearDto(Guid Id, int Year, DateOnly StartDate, DateOnly EndDate, bool IsActive);

public record CreateAcademicYearRequest(int Year, DateOnly StartDate, DateOnly EndDate, bool IsActive);

public record UpdateAcademicYearRequest(int Year, DateOnly StartDate, DateOnly EndDate, bool IsActive);

public record SchoolClassDto(Guid Id, string Name, Guid AcademicYearId, int Year);

public record CreateSchoolClassRequest(string Name, Guid AcademicYearId);

public record UpdateSchoolClassRequest(string Name, Guid AcademicYearId);

public record CurriculumComponentDto(Guid Id, string Name, string Code, bool IsActive);

public record CreateCurriculumComponentRequest(string Name, string Code, bool IsActive);

public record UpdateCurriculumComponentRequest(string Name, string Code, bool IsActive);

public record AssessmentDto(
    Guid Id,
    Guid SchoolClassId,
    Guid CurriculumComponentId,
    string Title,
    decimal Weight,
    DateOnly Date,
    bool IsRecovery);

public record CreateAssessmentRequest(
    Guid SchoolClassId,
    Guid CurriculumComponentId,
    string Title,
    decimal Weight,
    DateOnly Date,
    bool IsRecovery);

public record UpdateAssessmentRequest(
    Guid SchoolClassId,
    Guid CurriculumComponentId,
    string Title,
    decimal Weight,
    DateOnly Date,
    bool IsRecovery);

public record GradeRequest(decimal Value);
