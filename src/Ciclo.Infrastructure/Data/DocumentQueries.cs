using System.Linq.Expressions;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;

namespace Ciclo.Infrastructure.Data;

/// <summary>
/// Consultas e projeção de documento em um dono único.
///
/// A projeção estava repetida em três métodos do serviço e com shapes diferentes — a listagem
/// de vencimento omitia a foto do aluno. Com uma projeção só, o shape é o mesmo por construção
/// (mesma lição do M1 para aluno).
///
/// A busca por nome usa `lower(coluna) LIKE` pela mesma razão do M5: `Contains` é sensível a
/// caixa no Npgsql. O suppress dos analisadores fica aqui, no dono da expressão, e é provado
/// por `QueryTranslationTests` contra o provider real.
/// </summary>
public static class DocumentQueries
{
    public static readonly Expression<Func<Document, DocumentDto>> ToDto = document => new DocumentDto(
        document.Id,
        document.StudentId,
        document.Student.Nome,
        document.DocumentTypeId,
        document.DocumentType.Nome,
        document.NomeArquivo,
        document.Status.ToString(),
        document.DataValidade,
        document.MotivoRejeicao,
        document.CreatedAt,
        document.Student.Foto);

    /// <summary>Busca por parte do nome do aluno. <paramref name="lowerTerm"/> já normalizado.</summary>
    public static IQueryable<Document> WhereStudentNameContains(
        this IQueryable<Document> query, string lowerTerm)
    {
#pragma warning disable CA1304, CA1311, CA1862
        return query.Where(document => document.Student.Nome.ToLower().Contains(lowerTerm));
#pragma warning restore CA1304, CA1311, CA1862
    }
}
