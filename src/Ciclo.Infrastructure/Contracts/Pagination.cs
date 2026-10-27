namespace Ciclo.Infrastructure.Contracts;

/// <summary>
/// Normalização de paginação em um só lugar (M6).
///
/// Antes cada serviço repetia o mesmo trio de `if` — e dois deles esqueciam o piso de
/// `pageSize`, então `pageSize=0` devolvia zero itens em vez do padrão. A regra é a mesma para
/// toda a API: página mínima 1, tamanho entre 1 e 100.
/// </summary>
public static class Pagination
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (page < 1 ? 1 : page,
         pageSize < 1 ? DefaultPageSize : pageSize > MaxPageSize ? MaxPageSize : pageSize);
}
