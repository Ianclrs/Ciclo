using Ciclo.Core.Entities;

namespace Ciclo.Infrastructure.Data;

/// <summary>
/// Consultas de aluno cuja correção depende da TRADUÇÃO do provider (M5).
///
/// `Contains` vira `LIKE` no Npgsql, que é sensível a caixa: buscar "ana" não encontrava
/// "Ana Souza". `ToLower()` nos dois lados resolve, mas os analisadores CA1304/CA1311/CA1862
/// supõem execução local e não sabem que, dentro de um `IQueryable`, a chamada é traduzida para
/// `lower(coluna) LIKE ...` — nada roda em .NET. O suppress fica aqui, no dono da expressão,
/// e é provado por `QueryTranslationTests` (SQL gerado pelo provider Npgsql, sem conexão).
///
/// Ficam em um dono único porque a prova de tradução precisa exercitar a MESMA expressão que o
/// serviço usa: se o teste replicasse a expressão, uma mudança no serviço deixaria de ser coberta.
/// </summary>
public static class StudentQueries
{
    /// <summary>Busca por parte do nome, sem diferenciar caixa. <paramref name="lowerTerm"/> já normalizado.</summary>
    public static IQueryable<Student> WhereNameContains(this IQueryable<Student> query, string lowerTerm)
    {
#pragma warning disable CA1304, CA1311, CA1862
        return query.Where(s => s.Nome.ToLower().Contains(lowerTerm));
#pragma warning restore CA1304, CA1311, CA1862
    }

    /// <summary>Turma exata, sem diferenciar caixa. <paramref name="lowerTurma"/> já normalizada.</summary>
    public static IQueryable<Student> WhereTurma(this IQueryable<Student> query, string lowerTurma)
    {
#pragma warning disable CA1304, CA1311, CA1862
        return query.Where(s => s.Turma.ToLower() == lowerTurma);
#pragma warning restore CA1304, CA1311, CA1862
    }
}
