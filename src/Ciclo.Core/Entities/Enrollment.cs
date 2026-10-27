using Ciclo.Core.Interfaces;

namespace Ciclo.Core.Entities;

public class Enrollment : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid StudentId { get; set; }
    public Guid EnrollmentPeriodId { get; set; }
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Rascunho;
    public string? MotivoRejeicao { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }

    // M12 - auditoria da transicao: quem mudou o status e quando. Sem FK para User de
    // proposito: o registro sobrevive ao desligamento do funcionario (mesmo criterio
    // adotado pela spec 110 para o historico de operacoes).
    public DateTime? StatusChangedAt { get; set; }
    public Guid? StatusChangedBy { get; set; }

    // Navigation
    public Student Student { get; set; } = null!;
    public EnrollmentPeriod Period { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
}

public enum EnrollmentStatus
{
    Rascunho = 0,
    Pendente = 1,
    DocumentacaoPendente = 2,
    Aprovado = 3,
    Rejeitado = 4,
    Cancelado = 5
}
