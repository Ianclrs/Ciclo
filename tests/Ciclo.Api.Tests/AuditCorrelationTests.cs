using Ciclo.Core.Auditing;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 110 — resolução do identificador de correlação (FR-004, NFR-005).
///
/// O candidato vem do cliente e é não confiável: só entra no registro quando casa com o formato
/// permitido. Nos demais casos o identificador de rastreio do servidor é o fallback — nunca um
/// valor arbitrário do request.
/// </summary>
public sealed class AuditCorrelationTests
{
    [Fact]
    public void Resolve_KeepsAValidCandidate()
    {
        var resolved = AuditCorrelation.Resolve("abc-1234.5:6_7", "trace-ignored");

        Assert.Equal("abc-1234.5:6_7", resolved);
    }

    [Fact]
    public void Resolve_FallsBackToTraceIdentifierWhenInvalid()
    {
        Assert.Equal("trace-fallback", AuditCorrelation.Resolve(null, "trace-fallback"));
        Assert.Equal("trace-fallback", AuditCorrelation.Resolve("   ", "trace-fallback"));
        Assert.Equal("trace-fallback", AuditCorrelation.Resolve("ab", "trace-fallback"));
        Assert.Equal("trace-fallback", AuditCorrelation.Resolve("abc 1234", "trace-fallback"));
        Assert.Equal("trace-fallback", AuditCorrelation.Resolve(new string('a', 65), "trace-fallback"));
    }

    [Fact]
    public void Resolve_TruncatesTheFallback()
    {
        var traceIdentifier = new string('t', 200);

        var resolved = AuditCorrelation.Resolve(null, traceIdentifier);

        Assert.Equal(AuditCorrelation.MaxLength, resolved.Length);
        Assert.Equal(traceIdentifier[..AuditCorrelation.MaxLength], resolved);
    }
}
