using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// M5 — prova de tradução no provider real.
///
/// A busca usa `ToLower().Contains(...)` dentro de um `IQueryable`, com os analisadores
/// CA1304/CA1311/CA1862 suprimidos no ponto (eles supõem execução local). O provider InMemory
/// executa a expressão em .NET e portanto NÃO prova que o Npgsql consegue traduzi-la — um
/// `ToLower` não traduzível compilaria, passaria nos testes e falharia em produção.
///
/// Aqui o SQL é gerado por `ToQueryString()` com o provider Npgsql: nenhuma conexão é aberta,
/// e uma tradução impossível lança em vez de passar despercebida.
///
/// Contexto derivado de propósito: o EF cacheia o modelo por TIPO de contexto, e misturar dois
/// providers no mesmo tipo faria um modelo vazar para o outro (mesmo motivo documentado em
/// <c>DocumentTestData</c>).
/// </summary>
public sealed class QueryTranslationTests
{
    private sealed class NpgsqlProbeDbContext : AppDbContext
    {
        internal NpgsqlProbeDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }
    }

    private sealed class ProbeTenantContext : ITenantContext
    {
        public Guid TenantId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

        public bool IsResolved => true;

        public void SetTenant(Guid tenantId) => throw new NotSupportedException();
    }

    private static NpgsqlProbeDbContext CreateProbe()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            // Connection string não é usada por ToQueryString; nenhuma conexão é aberta.
            .UseNpgsql("Host=localhost;Database=probe;Username=probe;Password=probe")
            .Options;

        return new NpgsqlProbeDbContext(options, new ProbeTenantContext());
    }

    [Fact]
    public void StudentNameSearch_TranslatesToLowerAndLike()
    {
        using var db = CreateProbe();
        var term = "ana";

        var sql = db.Students
            .WhereNameContains(term)
            .ToQueryString();

        Assert.Contains("lower(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StudentTurmaFilter_TranslatesToLowerAndEquality()
    {
        using var db = CreateProbe();
        var turma = "5a";

        var sql = db.Students
            .WhereTurma(turma)
            .ToQueryString();

        Assert.Contains("lower(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
    }
}
