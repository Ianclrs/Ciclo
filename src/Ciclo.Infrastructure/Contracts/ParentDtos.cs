namespace Ciclo.Infrastructure.Contracts;

public record ParentDashboardDto(
    int TotalChildren,
    int UnreadNotifications,
    int PendingDocuments,
    int ActiveEnrollments,
    List<ChildSummaryDto> Children);

public record ChildSummaryDto(
    Guid StudentId,
    string Nome,
    string Turma,
    int AnoLetivo,
    string? EnrollmentStatus,
    int PendingDocuments,
    // Foto do aluno (data URL). Preenchido apenas nos endpoints que exibem o avatar.
    string? StudentFoto = null);

public record ChildDetailDto(
    StudentDto Student,
    List<DocumentDto> Documents,
    EnrollmentDto? CurrentEnrollment,
    List<GradeDto> Grades);

public record GradeDto(string Disciplina, decimal? Nota, string? Observacoes);
