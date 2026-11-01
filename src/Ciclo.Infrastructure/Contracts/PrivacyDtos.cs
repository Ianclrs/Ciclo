namespace Ciclo.Infrastructure.Contracts;

// Spec 210 — contratos de privacidade e LGPD em um único lugar. Os enums viajam como o nome
// do membro, então acrescentar um valor numérico novo não quebra o contrato da API.

public sealed record ConsentDto(
    Guid Id,
    string SubjectType,
    Guid SubjectId,
    string Purpose,
    string LegalBasis,
    string Status,
    DateTime GrantedAt,
    DateTime? RevokedAt);

public sealed record GrantConsentRequest(
    Guid SubjectId,
    string SubjectType,
    string Purpose,
    string LegalBasis);

public sealed record StudentExportDto(
    Guid Id,
    string Nome,
    DateTime DataNascimento,
    string? Cpf,
    string Turma,
    int AnoLetivo,
    string Status,
    string? Observacoes,
    string? Foto,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? AnonymizedAt);

public sealed record GuardianExportDto(
    Guid Id,
    string Name,
    string Email,
    string Role,
    bool IsActive,
    DateTime CreatedAt);

public sealed record EnrollmentExportDto(
    Guid Id,
    string Status,
    DateTime CreatedAt,
    DateTime? ApprovedAt);

public sealed record GradeExportDto(
    Guid Id,
    Guid AssessmentId,
    string AssessmentTitle,
    decimal Value,
    DateTime RecordedAt);

public sealed record DocumentExportDto(
    Guid Id,
    string NomeArquivo,
    string ContentType,
    long TamanhoBytes,
    string Status,
    DateTime CreatedAt);

public sealed record PrivacyExportDto(
    DateTime GeneratedAt,
    StudentExportDto Student,
    IReadOnlyList<GuardianExportDto> Guardians,
    IReadOnlyList<EnrollmentExportDto> Enrollments,
    IReadOnlyList<GradeExportDto> Grades,
    IReadOnlyList<DocumentExportDto> Documents,
    IReadOnlyList<ConsentDto> Consents,
    IReadOnlyList<PrivacyRequestDto> Requests);

public sealed record PrivacyRequestDto(
    Guid Id,
    string Type,
    string Status,
    Guid SubjectStudentId,
    Guid RequestedByUserId,
    string? Reason,
    string? Resolution,
    bool LegalHold,
    DateTime? RetainUntil,
    DateTime SubmittedAt,
    DateTime? DecidedAt,
    DateTime? CompletedAt);

public sealed record CreatePrivacyRequestRequest(
    Guid StudentId,
    string Type,
    string? Reason);

public sealed record DecidePrivacyRequestRequest(
    string Decision,
    string? Resolution,
    bool LegalHold,
    DateTime? RetainUntil);

public sealed record PrivacyRequestCompletionDto(
    PrivacyRequestDto Request,
    PrivacyExportDto? Export);
