namespace Ciclo.Infrastructure.Contracts;

public record CreateEnrollmentPeriodRequest(string Nome, DateTime DataInicio, DateTime DataFim, int AnoLetivo);

public record EnrollmentPeriodDto(Guid Id, string Nome, DateTime DataInicio, DateTime DataFim, int AnoLetivo, bool IsActive);

public record CreateEnrollmentRequest(Guid StudentId, Guid EnrollmentPeriodId);

/// <summary>
/// Documento do aluno considerado no detalhe da matrícula (Spec 60 FR-004).
/// </summary>
/// <param name="IsExpired">
/// Documento aprovado mas vencido: não conta como entregue e precisa ser reenviado. Distingue
/// "reaproveitado" de "venceu" na tela — é o que a V6 chama de notificação de documento vencido.
/// </param>
public record CarriedDocumentDto(
    Guid DocumentId,
    string DocumentType,
    DateTime? DataValidade,
    bool IsExpired);

public record EnrollmentDto(
    Guid Id,
    Guid StudentId,
    string StudentName,
    Guid PeriodId,
    string PeriodName,
    string Status,
    string? MotivoRejeicao,
    DateTime CreatedAt,
    DateTime? ApprovedAt,
    // Foto do aluno (data URL). Preenchido apenas nos endpoints que exibem o avatar.
    string? StudentFoto = null,
    // Situação documental do aluno. Preenchidos apenas no DETALHE (GetEnrollmentByIdAsync): as
    // listagens projetam direto em SQL e não exibem documento por aluno — carregar isso em cada
    // linha de uma página seria custo sem uso. Ficam no FIM de propósito: são opcionais e no meio
    // deslocariam a foto, que sites de projeção passam posicionalmente (árvore de expressão não
    // aceita argumento nomeado fora de posição — CS9307). Ver A4 em plano-correcoes.md.
    IReadOnlyList<CarriedDocumentDto>? CarriedDocuments = null,
    IReadOnlyList<string>? MissingDocumentTypes = null);

public record RejectEnrollmentRequest(string Motivo);

public record InvalidTransitionResponse(string Error, string From, string To, List<string> Allowed);
