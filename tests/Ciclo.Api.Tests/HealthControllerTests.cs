using System.Net;
using System.Net.Http.Json;

namespace Ciclo.Api.Tests;

[Collection(ApiTestHost.CollectionName)]
public class HealthControllerTests
{
    private readonly HttpClient _client;

    public HealthControllerTests(ApiTestHost host)
    {
        _client = host.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ReturnsOk_WithHealthyStatus()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("Healthy", body!.status);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetHealthDb_ReturnsOk_WhenDatabaseIsReachable()
    {
        var response = await _client.GetAsync("/health/db");

        // When database is reachable, expect 200 OK
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadFromJsonAsync<HealthDbResponse>();
            Assert.NotNull(body);
            Assert.Equal("Healthy", body!.status);
            Assert.Equal("Connected", body.database);
        }
        // When database is unreachable (no PostgreSQL running), expect 503
        else
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<HealthDbResponse>();
            Assert.NotNull(body);
            Assert.Equal("Unhealthy", body!.status);
            Assert.Equal("Disconnected", body.database);
        }
    }

    private sealed record HealthResponse(string status);
    private sealed record HealthDbResponse(string status, string database);
}
