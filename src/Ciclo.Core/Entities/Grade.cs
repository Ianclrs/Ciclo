using Ciclo.Core.Interfaces;

namespace Ciclo.Core.Entities;

/// <summary>
/// Nota de um aluno em uma avaliação. O par avaliação/aluno é a chave natural: o índice único
/// é o que torna o lançamento idempotente.
/// </summary>
public class Grade : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AssessmentId { get; set; }
    public Guid StudentId { get; set; }
    public decimal Value { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Assessment Assessment { get; set; } = null!;
    public Student Student { get; set; } = null!;
}
