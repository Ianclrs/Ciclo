using Ciclo.Core.Interfaces;

namespace Ciclo.Core.Entities;

/// <summary>
/// Spec 210 — solicitação do titular (ou do responsável) e seu ciclo de vida:
/// Submitted → Approved | Rejected → Completed.
/// </summary>
public class PrivacyRequest : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public PrivacyRequestType Type { get; set; }
    public PrivacyRequestStatus Status { get; set; } = PrivacyRequestStatus.Submitted;
    public Guid SubjectStudentId { get; set; }
    // Sem FK para User de propósito: a solicitação sobrevive ao desligamento de quem a abriu,
    // mesmo critério já adotado pela spec 110 para o histórico de operações.
    public Guid RequestedByUserId { get; set; }
    public string? Reason { get; set; }
    public string? Resolution { get; set; }
    // Bloqueio legal: enquanto houver solicitação aberta com esta marca, a anonimização do
    // aluno é recusada.
    public bool LegalHold { get; set; }
    // Prazo de retenção registrado na decisão. Nenhum job o executa (anti-escopo da spec 210).
    public DateTime? RetainUntil { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation
    public Tenant Tenant { get; set; } = null!;
}

public enum PrivacyRequestType
{
    Export = 0,
    Anonymization = 1,
    ConsentRevocation = 2
}

public enum PrivacyRequestStatus
{
    Submitted = 0,
    Approved = 1,
    Rejected = 2,
    Completed = 3
}
