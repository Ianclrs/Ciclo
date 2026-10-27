namespace Ciclo.Infrastructure.Contracts;

/// <summary>
/// Filtros da listagem global de documentos (A6b). Todos opcionais; ausente = sem restrição.
/// </summary>
public sealed record DocumentFilter(
    string? Status = null,
    Guid? StudentId = null,
    Guid? DocumentTypeId = null,
    string? Search = null);
