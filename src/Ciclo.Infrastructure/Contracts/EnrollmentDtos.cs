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

/// <summary>
/// Prévia da rematrícula em lote para um período (Spec 160 FR-001).
/// </summary>
/// <param name="DataReferencia">
/// Instante usado para decidir se um documento está vencido. Existe para que a leitura da prévia
/// seja auditável: o cliente não precisa recalcular validade para interpretar <c>IsExpired</c>.
/// </param>
public record RenewalPreviewDto(
    Guid PeriodId,
    string PeriodName,
    int AnoLetivo,
    DateTime DataReferencia,
    IReadOnlyList<RenewalPreviewItemDto> Items);

/// <summary>
/// Situação de um aluno candidato à renovação.
/// </summary>
/// <param name="BlockReason">
/// Nulo quando <paramref name="Eligible"/> é verdadeiro; caso contrário, um de
/// <c>student_inactive</c>, <c>student_transferred</c>, <c>enrollment_already_exists</c> ou
/// <c>pending_required_documents</c>, nessa ordem de precedência.
/// </param>
public record RenewalPreviewItemDto(
    Guid StudentId,
    string StudentName,
    bool Eligible,
    string? BlockReason,
    IReadOnlyList<CarriedDocumentDto> CarriedDocuments,
    IReadOnlyList<string> MissingDocumentTypes,
    bool EnrollmentAlreadyExists);

public record RenewalBatchRequest(Guid EnrollmentPeriodId, IReadOnlyList<Guid> StudentIds);

/// <summary>
/// Desfecho por aluno. Serializado como **texto** (`"Renewed"`, `"SkippedAlreadyExists"`, …):
/// a API não configura conversor global de enum, e o contrato do frontend declara a união
/// literal com esses nomes. Sem o atributo, o campo sairia como `0..3` e o rótulo do desfecho
/// na tela ficaria indefinido.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum RenewalOutcome
{
    Renewed = 0,
    SkippedAlreadyExists = 1,
    BlockedPendingDocuments = 2,
    Failed = 3
}

public record RenewalBatchItemResultDto(
    Guid StudentId,
    string StudentName,
    RenewalOutcome Outcome,
    string? Message);

/// <summary>
/// Resultado do lote: um desfecho por aluno e os contadores desses desfechos. Os contadores somam
/// sempre a quantidade de <paramref name="Items"/> — a operação é verificável aluno a aluno, nunca
/// como sucesso ou falha global (Spec 160 FR-003).
/// </summary>
public record RenewalBatchResultDto(
    Guid EnrollmentPeriodId,
    int AnoLetivo,
    IReadOnlyList<RenewalBatchItemResultDto> Items,
    int Renewed,
    int Skipped,
    int Blocked,
    int Failed);

public record InvalidTransitionResponse(string Error, string From, string To, List<string> Allowed);
