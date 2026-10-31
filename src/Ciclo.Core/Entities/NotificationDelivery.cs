using Ciclo.Core.Interfaces;
using Ciclo.Core.Notifications;

namespace Ciclo.Core.Entities;

/// <summary>
/// Registro de entrega de uma notificação proativa (Spec 140): uma linha por
/// (gatilho, destinatário, canal). Carrega a chave de idempotência e o desfecho.
/// </summary>
public class NotificationDelivery : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid RecipientUserId { get; set; }
    public NotificationTriggerKind Kind { get; set; }
    public NotificationDeliveryChannel Channel { get; set; }
    public NotificationDeliveryState State { get; set; } = NotificationDeliveryState.Pendente;
    public string? Erro { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }

    // Navigation
    public Tenant Tenant { get; set; } = null!;
}

public enum NotificationDeliveryChannel
{
    InApp = 0,
    Email = 1
}

public enum NotificationDeliveryState
{
    Pendente = 0,
    Enviado = 1,
    Falhou = 2,
    Suprimida = 3
}
