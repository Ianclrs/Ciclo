using Ciclo.Infrastructure.Auditing;
using Ciclo.Infrastructure.Contracts;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 110 — o CSV exportado é evidência entregue a auditoria externa: cabeçalho fixo, escape
/// RFC 4180 e timestamp ISO 8601 UTC determinístico.
/// </summary>
public sealed class AuditCsvWriterTests
{
    private const string Header =
        "OccurredAt,ActorUserId,ActorRole,Action,TargetType,TargetId,CorrelationId,IpAddress,UserAgent,BeforeJson,AfterJson";

    [Fact]
    public void Write_EmitsTheFixedHeader()
    {
        var csv = AuditCsvWriter.Write([]);

        Assert.Equal(Header + "\r\n", csv);
    }

    [Fact]
    public void Write_EscapesCommaQuoteAndNewline()
    {
        var dto = NewDto() with
        {
            ActorRole = "Admin, \"Chefe\"",
            UserAgent = "line1\nline2"
        };

        var line = AuditCsvWriter.Write([dto]).Split("\r\n")[1];

        Assert.Contains("\"Admin, \"\"Chefe\"\"\"", line);
        Assert.Contains("\"line1\nline2\"", line);
    }

    [Fact]
    public void Write_FormatsOccurredAtAsIsoUtc()
    {
        var dto = NewDto() with
        {
            OccurredAt = new DateTime(2026, 10, 22, 13, 5, 7, 123, DateTimeKind.Utc)
        };

        var line = AuditCsvWriter.Write([dto]).Split("\r\n")[1];

        Assert.StartsWith("2026-10-22T13:05:07.123Z,", line);
    }

    private static AuditEventDto NewDto() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        null,
        "Admin",
        "enrollment.approved",
        "Enrollment",
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        BeforeJson: null,
        AfterJson: null,
        IpAddress: "127.0.0.1",
        UserAgent: "test-agent",
        CorrelationId: null);
}
