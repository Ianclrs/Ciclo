using Ciclo.Core.Interfaces;

namespace Ciclo.Core.Entities;

/// <summary>
/// Spec 120 — convite nominal de identidade. A única representação persistida do segredo é
/// <see cref="TokenHash"/> (SHA-256 em hexadecimal): o token em claro existe só na resposta ao Admin.
/// A validade é decidida na leitura (<see cref="UsedAt"/> nulo e <see cref="ExpiresAt"/> no futuro),
/// sem job de limpeza — não há host de tarefas agendadas no projeto.
/// </summary>
public class UserInvitation : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    public bool IsUsed => UsedAt != null;
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsPending => !IsUsed && !IsExpired;

    public User User { get; set; } = null!;
}
