using Ciclo.Core.Interfaces;

namespace Ciclo.Core.Entities;

/// <summary>
/// Componente curricular (disciplina) do tenant. As avaliações e o boletim são por componente.
/// </summary>
public class CurriculumComponent : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
