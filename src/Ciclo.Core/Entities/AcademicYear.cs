using Ciclo.Core.Interfaces;

namespace Ciclo.Core.Entities;

/// <summary>
/// Ano letivo do tenant. É o pai das turmas e a autoridade sobre o intervalo de datas
/// em que as avaliações daquele ano existem.
/// </summary>
public class AcademicYear : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public int Year { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<SchoolClass> Classes { get; set; } = [];
}
