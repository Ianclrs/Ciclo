using System.Net;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 130 — endpoints de observabilidade no host real.
///
/// Cobre os três endpoints novos e a compatibilidade com os dois antigos (`/Health` e
/// `/Health/db`), que continuam sendo usados pelo host atual.
/// </summary>
[Collection(ApiTestHost.CollectionName)]
public sealed class ObservabilityEndpointTests
{
    private readonly ApiTestHost _host;

    public ObservabilityEndpointTests(ApiTestHost host) => _host = host;

    [Fact]
    public async Task Liveness_RespondsHealthy()
    {
        using var client = _host.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_ReportsDatabaseAndStorage()
    {
        using var client = _host.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        Assert.Contains("db", body);
        Assert.Contains("storage", body);
    }

    [Fact]
    public async Task Metrics_ExposeHttpAndQueueSignals()
    {
        using var client = _host.CreateClient();

        // Uma requisição antes do scrape para que a métrica HTTP tenha uma amostra.
        await client.GetAsync("/health/live");

        var body = await client.GetStringAsync("/metrics");

        Assert.Contains("http_requests_received_total", body);
        Assert.Contains("ciclo_email_queue_depth", body);
        Assert.Contains("ciclo_email_queue_capacity", body);
    }

    [Fact]
    public async Task LegacyHealthEndpoints_KeepWorking()
    {
        using var client = _host.CreateClient();

        var health = await client.GetAsync("/Health");
        var database = await client.GetAsync("/Health/db");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.True(
            database.StatusCode == HttpStatusCode.OK,
            await database.Content.ReadAsStringAsync());
    }
}
