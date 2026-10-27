namespace Ciclo.Api.Tests;

/// <summary>
/// M3 — guarda estrutural do formato de erro.
///
/// O formato canônico tem um dono (`ProblemResponse`). Este teste existe porque a violação é
/// invisível em teste de comportamento: um controller que volte a montar `Problem(title: ...)`
/// ou `new { error = ... }` continua respondendo 400/404 — só perde o `code` de contrato, e o
/// cliente passa a cair na mensagem genérica sem que nada falhe.
/// </summary>
public sealed class ErrorFormatConventionTests
{
    [Fact]
    public void Controllers_DoNotBuildErrorBodiesByHand()
    {
        var controllers = Path.Combine(RepositoryRoot.Find(), "src", "Ciclo.Api", "Controllers");

        var offenders = Directory
            .EnumerateFiles(controllers, "*.cs")
            .Where(file =>
            {
                var source = File.ReadAllText(file);
                return source.Contains("Problem(title:", StringComparison.Ordinal)
                    || source.Contains("new { error", StringComparison.Ordinal);
            })
            .Select(Path.GetFileName)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Use ProblemResponse.FromCode(...) em vez de montar o corpo de erro: {string.Join(", ", offenders)}");
    }
}
