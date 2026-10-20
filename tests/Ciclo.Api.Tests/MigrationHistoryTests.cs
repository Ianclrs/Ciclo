using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Ciclo.Api.Tests;

/// <summary>
/// A1 — contrato do histórico de migrations.
///
/// O defeito do A1 não foi só "EnsureCreated não altera tabela": existiam **três** fontes de schema
/// (modelo EF via EnsureCreated, 8 migrations em C# que nunca eram aplicadas, e `db/migrations.sql`)
/// e nenhuma era autoridade. Pior: não havia `AppDbContextModelSnapshot`, então o EF não reconhecia
/// as migrations como histórico — `migrations add` gerava `CreateTable` de todas as 17 tabelas,
/// como se o modelo estivesse vazio.
///
/// Estes contratos travam a reconciliação: o histórico existe, e **o modelo não pode divergir dele**
/// sem alguém gerar a migration correspondente. É a falha que produziu a divergência silenciosa
/// entre o que o código esperava e o que o banco tinha.
///
/// O provider é obrigatoriamente o mesmo de produção (Npgsql): o snapshot é por provider, então
/// comparar com InMemory daria diferenças falsas. Nenhuma conexão é aberta — comparar o modelo com
/// o snapshot é trabalho em memória.
/// </summary>
public sealed class MigrationHistoryTests
{
    private const string DummyConnection =
        "Host=localhost;Database=__never_used__;Username=__never_used__";

    [Fact]
    public void ThereIsAMigrationHistory()
    {
        using var context = CreateContext();

        var migrations = context.Database.GetMigrations().ToList();

        Assert.True(migrations.Count > 0,
            "Não há migrations no assembly. Sem elas o schema não tem dono: era o estado do A1 "
            + "(EnsureCreated criava o schema em Development e nada o alterava depois).");

        Assert.Contains(migrations, name => name.EndsWith("InitialCreate", StringComparison.Ordinal));
    }

    /// <summary>
    /// O contrato central: mudar o modelo sem gerar migration é a origem da divergência silenciosa.
    /// Aqui isso quebra a suíte, não o ambiente de alguém.
    /// </summary>
    [Fact]
    public void TheModelDoesNotDivergeFromTheMigrationHistory()
    {
        using var context = CreateContext();

        var hasPending = context.Database.HasPendingModelChanges();

        Assert.False(hasPending,
            "O modelo do EF divergiu do histórico de migrations. Alguém alterou o modelo (entidades "
            + "ou OnModelCreating) sem gerar a migration correspondente. Gere a migration: "
            + "`dotnet ef migrations add <Nome> --project src/Ciclo.Infrastructure "
            + "--startup-project src/Ciclo.Api --no-build` (ver §1 do plano-correcoes.md — sempre com "
            + "--no-build e com APPDATA exportado).");
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(DummyConnection)
            .Options;

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.IsResolved).Returns(false);

        return new AppDbContext(options, tenantContext.Object);
    }
}
