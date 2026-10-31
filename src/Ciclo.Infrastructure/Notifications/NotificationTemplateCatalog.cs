using Ciclo.Core.Notifications;

namespace Ciclo.Infrastructure.Notifications;

/// <summary>
/// Assunto e corpo de uma notificação proativa. O mesmo template alimenta o título/mensagem
/// in-app e o assunto/corpo do e-mail, para que os dois canais não divirjam (Spec 140, FR-003).
/// </summary>
public sealed record NotificationTemplate(string Assunto, string Corpo);

/// <summary>
/// Catálogo estático de templates por tipo de gatilho e locale (Spec 140, FR-003).
/// Os corpos podem carregar os placeholders <c>{aluno}</c> e <c>{referencia}</c>: o catálogo
/// os devolve intactos e o chamador substitui antes de persistir.
/// </summary>
public static class NotificationTemplateCatalog
{
    /// <summary>Locale padrão e único escrito nesta spec; é também o fallback de qualquer outro valor.</summary>
    public const string DefaultLocale = "pt-BR";

    private static readonly Dictionary<NotificationTriggerKind, NotificationTemplate> PtBr = new()
    {
        [NotificationTriggerKind.MatriculaSubmetida] = new(
            "Matrícula recebida",
            "A matrícula do aluno {aluno} foi recebida e está em análise. Referência: {referencia}."),
        [NotificationTriggerKind.MatriculaAprovada] = new(
            "Matrícula aprovada",
            "A matrícula do aluno {aluno} foi aprovada. Referência: {referencia}."),
        [NotificationTriggerKind.MatriculaRejeitada] = new(
            "Matrícula rejeitada",
            "A matrícula do aluno {aluno} foi rejeitada. Referência: {referencia}."),
        [NotificationTriggerKind.DocumentoVerificado] = new(
            "Documento verificado",
            "O documento {referencia} do aluno {aluno} foi verificado e aprovado."),
        [NotificationTriggerKind.DocumentoRejeitado] = new(
            "Documento rejeitado",
            "O documento {referencia} do aluno {aluno} foi rejeitado."),
        [NotificationTriggerKind.DocumentoAVencer] = new(
            "Documento a vencer",
            "O documento {referencia} do aluno {aluno} está próximo do vencimento."),
        [NotificationTriggerKind.RematriculaAberta] = new(
            "Rematrícula aberta",
            "O período de rematrícula {referencia} está aberto."),
        [NotificationTriggerKind.RematriculaPrazoFinal] = new(
            "Rematrícula no prazo final",
            "O período de rematrícula {referencia} está na última semana.")
    };

    private static readonly Dictionary<string, Dictionary<NotificationTriggerKind, NotificationTemplate>> Catalogs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultLocale] = PtBr
        };

    /// <summary>
    /// Resolve o template do <paramref name="kind"/> para o <paramref name="locale"/> pedido.
    /// O locale é comparado sem diferenciar maiúsculas de minúsculas e sem espaços nas pontas;
    /// valores nulos, vazios ou desconhecidos caem para <see cref="DefaultLocale"/>.
    /// Lança <see cref="InvalidOperationException"/> quando o tipo não tem entrada no catálogo,
    /// para que um template ausente falhe no primeiro despacho em vez de enviar corpo vazio.
    /// </summary>
    public static NotificationTemplate Resolve(NotificationTriggerKind kind, string? locale = null)
    {
        var normalizedLocale = string.IsNullOrWhiteSpace(locale) ? DefaultLocale : locale.Trim();
        var catalog = Catalogs.TryGetValue(normalizedLocale, out var byKind) ? byKind : Catalogs[DefaultLocale];

        return catalog.TryGetValue(kind, out var template)
            ? template
            : throw new InvalidOperationException($"notification_template_missing:{kind}");
    }
}
