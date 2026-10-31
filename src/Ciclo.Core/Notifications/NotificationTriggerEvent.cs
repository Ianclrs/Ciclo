using Ciclo.Core.Interfaces;

namespace Ciclo.Core.Notifications;

/// <summary>
/// Nomeia um evento de domínio para o dispatcher de notificações proativas (Spec 140).
/// O serviço de domínio informa o que aconteceu; o texto da notificação vem do catálogo.
/// Não é entidade mapeada no EF: carrega <see cref="TenantId"/> apenas como dado do evento,
/// e implementa <see cref="ITenantScoped"/> para manter o contrato de tenant explícito.
/// </summary>
public sealed record NotificationTriggerEvent(
    NotificationTriggerKind Kind,
    Guid TenantId,
    Guid StudentId,
    Guid ReferenceId,
    DateTime OccurredAt) : ITenantScoped;

public enum NotificationTriggerKind
{
    MatriculaSubmetida = 0,
    MatriculaAprovada = 1,
    MatriculaRejeitada = 2,
    DocumentoVerificado = 3,
    DocumentoRejeitado = 4,
    DocumentoAVencer = 5,
    RematriculaAberta = 6,
    RematriculaPrazoFinal = 7
}
