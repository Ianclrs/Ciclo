using Ciclo.Core.Interfaces;

namespace Ciclo.Core.Entities;

/// <summary>
/// Avaliação de uma turma em um componente curricular. O <see cref="Weight"/> é o que
/// transforma a média em ponderada e <see cref="IsRecovery"/> separa a recuperação das notas regulares.
/// </summary>
public class Assessment : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SchoolClassId { get; set; }
    public Guid CurriculumComponentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Weight { get; set; } = 1m;
    public DateOnly Date { get; set; }
    public bool IsRecovery { get; set; }

    // Navigation
    public SchoolClass SchoolClass { get; set; } = null!;
    public CurriculumComponent CurriculumComponent { get; set; } = null!;
}
