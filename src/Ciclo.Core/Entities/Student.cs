using Ciclo.Core.Interfaces;

namespace Ciclo.Core.Entities;

public class Student : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public DateTime DataNascimento { get; set; }
    public string? Cpf { get; set; }
    public string Turma { get; set; } = string.Empty;
    public int AnoLetivo { get; set; }
    public StudentStatus Status { get; set; } = StudentStatus.Ativo;
    public string? Observacoes { get; set; }
    // Foto do aluno como data URL (Base64), já redimensionada pelo navegador antes do envio.
    public string? Foto { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Tenant Tenant { get; set; } = null!;
    public ICollection<StudentParent> StudentParents { get; set; } = [];
}

public enum StudentStatus
{
    Ativo = 0,
    Inativo = 1,
    Transferido = 2
}
