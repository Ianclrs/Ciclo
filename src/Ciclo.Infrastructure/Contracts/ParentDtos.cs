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

// Boletim completo do aluno no portal dos pais. GradeDto continua sendo o resumo por componente;
// estes registros expõem o detalhe auditável (ADR-150-5).
public record ReportCardDto(
    Guid AlunoId,
    string? Turma,
    int? AnoLetivo,
    IReadOnlyList<ReportCardEntryDto> Componentes);

public record ReportCardEntryDto(
    string Componente,
    decimal? MediaRegular,
    decimal? NotaRecuperacao,
    decimal? MediaFinal,
    string Situacao,
    IReadOnlyList<ReportCardScoreDto> Avaliacoes);

public record ReportCardScoreDto(
    string Avaliacao,
    decimal Peso,
    decimal? Nota,
    DateOnly Data,
    bool Recuperacao);
