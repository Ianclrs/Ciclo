using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ciclo.Core.Entities;
using Ciclo.Core.Notifications;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Email;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Infrastructure.Notifications;

/// <summary>
/// Converte um gatilho de domínio em notificações proativas (Spec 140).
/// O caminho de evento é chamado pelos serviços de domínio depois do status já persistido;
/// o caminho agendado é chamado por <see cref="ScheduledNotificationJob"/> a cada intervalo.
/// </summary>
public interface IProactiveNotificationService
{
    /// <summary>Despacha um gatilho de evento para todos os responsáveis do aluno.</summary>
    Task HandleAsync(NotificationTriggerEvent trigger, CancellationToken ct);

    /// <summary>
    /// Avalia o corpus de todos os tenants no instante <paramref name="nowUtc"/> e emite os
    /// gatilhos agendados. Devolve quantos gatilhos foram emitidos.
    /// </summary>
    Task<int> RunScheduledAsync(DateTime nowUtc, CancellationToken ct);
}

/// <summary>
/// Dispatcher único da comunicação proativa: destinatários, template, entrega in-app, e-mail e
/// registro de entrega idempotente.
///
/// A idempotência é insert-first: a linha <see cref="NotificationDelivery"/> em estado
/// <see cref="NotificationDeliveryState.Pendente"/> é gravada ANTES de o canal ser chamado. Se a
/// chave já existir, nada é enviado — o índice único é o árbitro, inclusive entre duas instâncias.
///
/// Falha de um canal (ou de um destinatário) grava <see cref="NotificationDeliveryState.Falhou"/> e
/// não interrompe o lote: um e-mail recusado não pode levar junto a notificação in-app dos demais.
/// </summary>
public sealed class ProactiveNotificationService : IProactiveNotificationService
{
    /// <summary>Janela, em dias, a partir de hoje para avisar que um documento vai vencer.</summary>
    public const int JanelaVencimentoDias = 30;

    /// <summary>Janela, em dias, para avisar que o período de rematrícula está acabando.</summary>
    public const int JanelaPrazoRematriculaDias = 7;

    /// <summary>Teto de candidatos processados por ciclo agendado e por consulta.</summary>
    public const int LimitePorCiclo = 500;

    private const int TamanhoMaximoErro = 500;

    private readonly AppDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ProactiveNotificationService> _logger;

    public ProactiveNotificationService(
        AppDbContext db,
        IEmailSender emailSender,
        ITenantContext tenantContext,
        ILogger<ProactiveNotificationService> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    // ── FR-001: gatilho por evento ───────────────────────────────────

    public async Task HandleAsync(NotificationTriggerEvent trigger, CancellationToken ct)
    {
        // O job roda fora de qualquer requisição: o tenant tem de ser fixado aqui, senão o filtro
        // global e as linhas gravadas ficariam por conta de um valor ambiente qualquer (NFR-001).
        if (trigger.TenantId != Guid.Empty)
            _tenantContext.SetTenant(trigger.TenantId);

        var aluno = string.Empty;

        // Vencimento e rematrícula trazem o aluno no gatilho; só os dois tipos de período não têm
        // aluno único, porque o destinatário é todo responsável com matrícula no período.
        if (!IsPeriodKind(trigger.Kind))
        {
            // O filtro global de tenant já restringe a consulta: aluno de outro tenant não é
            // encontrado, e a ausência é registrada como aviso em vez de virar exceção.
            var student = await _db.Students
                .FirstOrDefaultAsync(s => s.Id == trigger.StudentId, ct);

            if (student is null)
            {
#pragma warning disable CA1848, CA1873
                _logger.LogWarning(
                    "notification_trigger_student_not_found: kind={Kind} studentId={StudentId}",
                    trigger.Kind, trigger.StudentId);
#pragma warning restore CA1848, CA1873
                return;
            }

            aluno = student.Nome;
        }

        var referencia = await ResolveReferenceNameAsync(trigger, ct);
        var recipients = await ResolveRecipientsAsync(trigger, ct);

        if (recipients.Count == 0)
        {
#pragma warning disable CA1848, CA1873
            _logger.LogWarning(
                "notification_trigger_without_recipients: kind={Kind} referenceId={ReferenceId}",
                trigger.Kind, trigger.ReferenceId);
#pragma warning restore CA1848, CA1873
            return;
        }

        var template = NotificationTemplateCatalog.Resolve(
            trigger.Kind, NotificationTemplateCatalog.DefaultLocale);

        var subject = template.Assunto;
        var body = template.Corpo
            .Replace("{aluno}", aluno, StringComparison.Ordinal)
            .Replace("{referencia}", referencia, StringComparison.Ordinal);

        foreach (var recipient in recipients)
        {
            await DispatchAsync(
                trigger, recipient, NotificationDeliveryChannel.InApp, subject, body, ct);

            // Responsável sem e-mail recebe só o in-app: a ausência do endereço não é uma falha
            // de entrega e por isso não gera linha de e-mail (Edge Cases do Spec 140).
            if (!string.IsNullOrWhiteSpace(recipient.Email))
            {
                await DispatchAsync(
                    trigger, recipient, NotificationDeliveryChannel.Email, subject, body, ct);
            }
        }
    }

    // ── FR-002: gatilho por agendamento ──────────────────────────────

    public async Task<int> RunScheduledAsync(DateTime nowUtc, CancellationToken ct)
    {
        // A lista de tenants não é filtrada por tenant: é o ponto de partida do laço que fixa o
        // escopo antes de cada consulta.
        var tenantIds = await _db.Tenants.Select(t => t.Id).ToListAsync(ct);
        var emitidos = 0;

        foreach (var tenantId in tenantIds)
        {
            _tenantContext.SetTenant(tenantId);

            emitidos += await EmitDocumentosAVencerAsync(tenantId, nowUtc, ct);
            emitidos += await EmitRematriculasAsync(tenantId, nowUtc, ct);
        }

        return emitidos;
    }

    private async Task<int> EmitDocumentosAVencerAsync(Guid tenantId, DateTime nowUtc, CancellationToken ct)
    {
        var limite = nowUtc.AddDays(JanelaVencimentoDias);

        // Só documento aprovado e com validade conta: pendente/rejeitado não vence, e validade nula
        // é pulada pela própria comparação.
        var candidatos = await _db.Documents
            .Where(d => d.Status == DocumentStatus.Aprovado
                && d.DataValidade != null
                && d.DataValidade >= nowUtc
                && d.DataValidade <= limite)
            .OrderBy(d => d.DataValidade)
            .Take(LimitePorCiclo)
            .Select(d => new { d.Id, d.StudentId })
            .ToListAsync(ct);

        foreach (var document in candidatos)
        {
            await HandleAsync(
                new NotificationTriggerEvent(
                    NotificationTriggerKind.DocumentoAVencer, tenantId, document.StudentId, document.Id, nowUtc),
                ct);
        }

        return candidatos.Count;
    }

    private async Task<int> EmitRematriculasAsync(Guid tenantId, DateTime nowUtc, CancellationToken ct)
    {
        var candidatos = await _db.EnrollmentPeriods
            .Where(p => p.IsActive)
            .OrderBy(p => p.DataInicio)
            .Take(LimitePorCiclo)
            .Select(p => new { p.Id, p.DataInicio, p.DataFim })
            .ToListAsync(ct);

        var limitePrazo = nowUtc.AddDays(JanelaPrazoRematriculaDias);
        var emitidos = 0;

        foreach (var period in candidatos)
        {
            if (period.DataInicio.Date == nowUtc.Date)
            {
                await HandleAsync(
                    new NotificationTriggerEvent(
                        NotificationTriggerKind.RematriculaAberta, tenantId, Guid.Empty, period.Id, nowUtc),
                    ct);
                emitidos++;
            }

            if (period.DataFim >= nowUtc && period.DataFim <= limitePrazo)
            {
                await HandleAsync(
                    new NotificationTriggerEvent(
                        NotificationTriggerKind.RematriculaPrazoFinal, tenantId, Guid.Empty, period.Id, nowUtc),
                    ct);
                emitidos++;
            }
        }

        return emitidos;
    }

    // ── Destinatários e texto ────────────────────────────────────────

    private async Task<List<Recipient>> ResolveRecipientsAsync(
        NotificationTriggerEvent trigger, CancellationToken ct)
    {
        List<Guid> studentIds;

        if (IsPeriodKind(trigger.Kind))
        {
            // Período não tem aluno no gatilho: os destinatários são os responsáveis de todos os
            // alunos matriculados nele.
            studentIds = await _db.Enrollments
                .Where(e => e.EnrollmentPeriodId == trigger.ReferenceId)
                .Select(e => e.StudentId)
                .Distinct()
                .ToListAsync(ct);
        }
        else
        {
            studentIds = [trigger.StudentId];
        }

        if (studentIds.Count == 0)
            return [];

        var rows = await _db.StudentParents
            .Where(sp => studentIds.Contains(sp.StudentId))
            .Join(
                _db.Users,
                sp => sp.ParentId,
                u => u.Id,
                (sp, u) => new { u.Id, u.Name, u.Email, u.TenantId })
            .Where(r => r.TenantId == trigger.TenantId)
            .ToListAsync(ct);

        // Um responsável com dois alunos no mesmo período entra uma vez só.
        return rows
            .GroupBy(r => r.Id)
            .Select(g => new Recipient(g.Key, g.First().Name, g.First().Email))
            .ToList();
    }

    private async Task<string> ResolveReferenceNameAsync(
        NotificationTriggerEvent trigger, CancellationToken ct)
    {
        switch (trigger.Kind)
        {
            case NotificationTriggerKind.RematriculaAberta:
            case NotificationTriggerKind.RematriculaPrazoFinal:
                return await _db.EnrollmentPeriods
                    .Where(p => p.Id == trigger.ReferenceId)
                    .Select(p => p.Nome)
                    .FirstOrDefaultAsync(ct) ?? string.Empty;

            case NotificationTriggerKind.DocumentoVerificado:
            case NotificationTriggerKind.DocumentoRejeitado:
            case NotificationTriggerKind.DocumentoAVencer:
                return await _db.Documents
                    .Where(d => d.Id == trigger.ReferenceId)
                    .Select(d => d.DocumentType.Nome)
                    .FirstOrDefaultAsync(ct) ?? string.Empty;

            default:
                // Matrícula: a referência legível é o período em que ela foi feita.
                return await _db.Enrollments
                    .Where(e => e.Id == trigger.ReferenceId)
                    .Select(e => e.Period.Nome)
                    .FirstOrDefaultAsync(ct) ?? string.Empty;
        }
    }

    // ── Entrega ──────────────────────────────────────────────────────

    private async Task DispatchAsync(
        NotificationTriggerEvent trigger,
        Recipient recipient,
        NotificationDeliveryChannel channel,
        string subject,
        string body,
        CancellationToken ct)
    {
        var key = BuildIdempotencyKey(trigger);

        // Além do índice único, uma consulta explícita barra o caso comum (o mesmo gatilho chegando
        // duas vezes em sequência): quem já tem linha não é enviado de novo.
        var jaEntregue = await _db.NotificationDeliveries.AnyAsync(d =>
            d.TenantId == trigger.TenantId
            && d.IdempotencyKey == key
            && d.RecipientUserId == recipient.UserId
            && d.Channel == channel, ct);

        if (jaEntregue)
        {
            LogSuppressed(trigger, recipient, channel);
            return;
        }

        var delivery = new NotificationDelivery
        {
            Id = Guid.NewGuid(),
            TenantId = trigger.TenantId,
            IdempotencyKey = key,
            RecipientUserId = recipient.UserId,
            Kind = trigger.Kind,
            Channel = channel,
            State = NotificationDeliveryState.Pendente,
            CreatedAt = DateTime.UtcNow
        };

        _db.NotificationDeliveries.Add(delivery);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // O índice único é o árbitro da corrida entre duas instâncias: quem perde não envia.
            // A entidade sai do rastreador para não ser reinserida no próximo SaveChanges.
            _db.Entry(delivery).State = EntityState.Detached;
            LogSuppressed(trigger, recipient, channel);
            return;
        }

        Notification? notification = null;
        UserNotification? userNotification = null;

        try
        {
            if (channel == NotificationDeliveryChannel.InApp)
            {
                notification = new Notification
                {
                    Id = Guid.NewGuid(),
                    TenantId = trigger.TenantId,
                    Titulo = subject,
                    Mensagem = body,
                    Tipo = MapNotificationType(trigger.Kind),
                    ReferenceId = trigger.ReferenceId,
                    CreatedAt = DateTime.UtcNow
                };

                userNotification = new UserNotification
                {
                    Id = Guid.NewGuid(),
                    NotificationId = notification.Id,
                    UserId = recipient.UserId
                };

                _db.Notifications.Add(notification);
                _db.UserNotifications.Add(userNotification);
            }
            else
            {
                // O e-mail só é despachado para responsável com endereço; sem endereço nenhuma
                // linha de e-mail existe, então aqui o endereço é sempre não vazio.
                await _emailSender.SendAsync(recipient.Email!, recipient.Nome, subject, body);
            }

            delivery.State = NotificationDeliveryState.Enviado;
            delivery.SentAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
#pragma warning disable CA1031 // a falha de um canal é registrada na linha da entrega e o lote segue
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Uma falha de canal não interrompe o lote: fica registrada na linha da entrega e o
            // próximo destinatário segue.
            if (notification is not null)
                _db.Entry(notification).State = EntityState.Detached;

            if (userNotification is not null)
                _db.Entry(userNotification).State = EntityState.Detached;

            delivery.State = NotificationDeliveryState.Falhou;
            delivery.Erro = Truncate(ex.Message);

            // Sem o token: registrar o desfecho não pode ser pulado justamente quando a operação
            // foi cancelada, senão a linha ficaria em Pendente para sempre.
            await _db.SaveChangesAsync(CancellationToken.None);

#pragma warning disable CA1848, CA1873
            _logger.LogError(
                ex,
                "notification_delivery_failed: kind={Kind} channel={Channel} recipientUserId={RecipientUserId}",
                trigger.Kind, channel, recipient.UserId);
#pragma warning restore CA1848, CA1873
        }
    }

    private void LogSuppressed(
        NotificationTriggerEvent trigger, Recipient recipient, NotificationDeliveryChannel channel)
    {
#pragma warning disable CA1848, CA1873
        _logger.LogInformation(
            "notification_delivery_suppressed: kind={Kind} channel={Channel} recipientUserId={RecipientUserId}",
            trigger.Kind, channel, recipient.UserId);
#pragma warning restore CA1848, CA1873
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Abertura e prazo de rematrícula não têm aluno no gatilho: os destinatários vêm do período.
    /// </summary>
    private static bool IsPeriodKind(NotificationTriggerKind kind) =>
        kind is NotificationTriggerKind.RematriculaAberta or NotificationTriggerKind.RematriculaPrazoFinal;

    /// <summary>
    /// Os três tipos agendados ganham o dia na chave: o mesmo candidato é emitido a cada ciclo do
    /// job, e o sufixo de data o transforma em uma entrega por dia.
    /// </summary>
    private static bool IsScheduledKind(NotificationTriggerKind kind) =>
        kind is NotificationTriggerKind.DocumentoAVencer
            or NotificationTriggerKind.RematriculaAberta
            or NotificationTriggerKind.RematriculaPrazoFinal;

    private static string BuildIdempotencyKey(NotificationTriggerEvent trigger) =>
        IsScheduledKind(trigger.Kind)
            ? FormattableString.Invariant(
                $"{trigger.Kind}:{trigger.ReferenceId}:{trigger.OccurredAt:yyyy-MM-dd}")
            : FormattableString.Invariant($"{trigger.Kind}:{trigger.ReferenceId}");

    private static NotificationType MapNotificationType(NotificationTriggerKind kind) => kind switch
    {
        NotificationTriggerKind.MatriculaSubmetida
            or NotificationTriggerKind.MatriculaAprovada
            or NotificationTriggerKind.MatriculaRejeitada
            or NotificationTriggerKind.RematriculaAberta
            or NotificationTriggerKind.RematriculaPrazoFinal => NotificationType.Matricula,
        NotificationTriggerKind.DocumentoVerificado
            or NotificationTriggerKind.DocumentoRejeitado
            or NotificationTriggerKind.DocumentoAVencer => NotificationType.DocumentoPendente,
        _ => NotificationType.Geral
    };

    private static string Truncate(string message) =>
        message.Length <= TamanhoMaximoErro ? message : message[..TamanhoMaximoErro];

    private sealed record Recipient(Guid UserId, string Nome, string? Email);
}
