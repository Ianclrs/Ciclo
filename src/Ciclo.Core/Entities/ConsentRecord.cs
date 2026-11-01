using Ciclo.Core.Interfaces;

namespace Ciclo.Core.Entities;

/// <summary>
/// Spec 210 — consentimento registrado por titular (aluno ou responsável) e por finalidade.
///
/// Uma linha por (TenantId, SubjectType, SubjectId, Purpose): conceder novamente atualiza a
/// linha existente em vez de inserir outra, e o índice único declara essa regra no banco.
/// </summary>
public class ConsentRecord : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public ConsentSubjectType SubjectType { get; set; }
    // Aponta para Students.Id quando SubjectType é Student e para Users.Id quando é Guardian,
    // por isso não há chave estrangeira: o alvo depende do tipo.
    public Guid SubjectId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string LegalBasis { get; set; } = string.Empty;
    public ConsentStatus Status { get; set; } = ConsentStatus.Granted;
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public Guid? GrantedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Tenant Tenant { get; set; } = null!;
}

public enum ConsentSubjectType
{
    Student = 0,
    Guardian = 1
}

public enum ConsentStatus
{
    Granted = 0,
    Revoked = 1
}
