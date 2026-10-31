using System.Net;
using Ciclo.Core.Auditing;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 110 — a correlação resolvida na borda chega ao cabeçalho de resposta do host real. Um
/// candidato válido é preservado; um inválido é descartado em favor do identificador de trace.
/// </summary>
[Collection(ApiTestHost.CollectionName)]
public sealed class AuditPipelineTests
{
    private readonly HttpClient _client;

    public AuditPipelineTests(ApiTestHost host) => _client = host.CreateClient();

    [Fact]
    public async Task Health_EchoesTheCorrelationIdHeader()
    {
        const string correlationId = "pipeline-correlation-001";

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(AuditCorrelation.HeaderName, correlationId);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(AuditCorrelation.HeaderName, out var values));
        Assert.Equal(correlationId, Assert.Single(values));
    }

    [Fact]
    public async Task Health_WhenTheCorrelationIdIsInvalid_ReplacesItWithTheTraceIdentifier()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(AuditCorrelation.HeaderName, "ab");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(AuditCorrelation.HeaderName, out var values));

        var resolved = Assert.Single(values);
        Assert.NotEqual("ab", resolved);
        Assert.False(string.IsNullOrWhiteSpace(resolved));
        Assert.True(resolved.Length <= AuditCorrelation.MaxLength);
    }
}
