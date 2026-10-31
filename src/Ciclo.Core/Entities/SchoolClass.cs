using Ciclo.Core.Interfaces;

namespace Ciclo.Core.Entities;

/// <summary>
/// Turma/série como entidade de primeira classe: o vínculo do aluno passa a ser por chave
/// estrangeira, e não pela string livre de <see cref="Student.Turma"/>.
/// </summary>
public class SchoolClass : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid AcademicYearId { get; set; }

    // Navigation
    public AcademicYear AcademicYear { get; set; } = null!;
    public ICollection<Student> Students { get; set; } = [];
}
