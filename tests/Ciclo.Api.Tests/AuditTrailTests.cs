using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Exceptions;
using Ciclo.Infrastructure.Auditing;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Api.Tests;

/// <summary>
/// Testes da trilha de auditoria (Spec 110): escrita transacional, imutabilidade em duas camadas
/// na aplicação, correlação, CSV e normalização da consulta.
/// </summary>
public sealed class AuditTrailTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// Contexto derivado com tipo próprio: o EF cacheia o modelo por tipo, e o filtro global fica
    /// preso à instância que construiu o modelo (mesmo motivo documentado em DocumentComplianceTests).
    /// </summary>
    private sealed class AuditTestDbContext : AppDbContext
    {
        internal AuditTestDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        private readonly Guid? _tenantId;

        internal FixedTenantContext(Guid? tenantId) => _tenantId = tenantId;

        public Guid TenantId => _tenantId
            ?? throw new TenantNotResolvedException("tenant_not_resolved");

        public bool IsResolved => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => throw new NotSupportedException("tenant is fixed in tests");
    }

    private static AuditTestDbContext CreateContext(Guid? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AuditTestDbContext(options, new FixedTenantContext(tenantId ?? TestTenantId));
    }

    private static AuditActor CreateActor()
    {
        var actor = new AuditActor();
        actor.SetCorrelationId("corr-12345678");
        actor.Capture(ActorId, "Admin", "10.0.0.1", "test-agent");
        return actor;
    }

    [Fact]
    public async Task Record_AddsEventWithTenantActorAndCorrelation()
    {
        using var db = CreateContext();
        var trail = new AuditTrail(db, new FixedTenantContext(TestTenantId), CreateActor());

        trail.Record(new AuditEntry(
            AuditActions.EnrollmentApproved,
            AuditTargetTypes.Enrollment,
            Guid.NewGuid(),
            new { Status = "Pendente" },
            new { Status = "Aprovado" }));

        await db.SaveChangesAsync();

        var saved = await db.AuditEvents.SingleAsync();
        Assert.Equal(TestTenantId, saved.TenantId);
        Assert.Equal(ActorId, saved.ActorUserId);
        Assert.Equal("Admin", saved.ActorRole);
        Assert.Equal("corr-12345678", saved.CorrelationId);
        Assert.Equal("10.0.0.1", saved.IpAddress);
        Assert.Equal("test-agent", saved.UserAgent);
        Assert.Contains("Pendente", saved.BeforeJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_ThrowsWhenTenantIsNotResolved()
    {
        using var db = CreateContext();
        var trail = new AuditTrail(db, new FixedTenantContext(null), CreateActor());

        var exception = Assert.Throws<TenantNotResolvedException>(() => trail.Record(
            new AuditEntry(AuditActions.SessionRevoked, AuditTargetTypes.Session, Guid.NewGuid())));

        Assert.Equal("audit_event_requires_resolved_tenant", exception.Message);
        Assert.Empty(db.AuditEvents);
    }

    [Fact]
    public void Record_ThrowsOnEmptyAction()
    {
        using var db = CreateContext();
        var trail = new AuditTrail(db, new FixedTenantContext(TestTenantId), CreateActor());

        Assert.Throws<ArgumentException>(() => trail.Record(
            new AuditEntry(" ", AuditTargetTypes.Enrollment, Guid.NewGuid())));
    }

    [Fact]
    public void Record_ThrowsWhenPayloadExceedsTheLimit()
    {
        using var db = CreateContext();
        var trail = new AuditTrail(db, new FixedTenantContext(TestTenantId), CreateActor());

        var oversized = new string('x', AuditTrailTests.PayloadLimitForTest + 1);

        var exception = Assert.Throws<InvalidOperationException>(() => trail.Record(
            new AuditEntry(AuditActions.DocumentVerified, AuditTargetTypes.Document, Guid.NewGuid(),
                null, new { Text = oversized })));

        Assert.Contains("audit payload exceeds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guard_BlocksModificationOfAnAuditEvent()
    {
        using var db = CreateContext();
        var entity = new AuditEvent
        {
            TenantId = TestTenantId,
            Action = AuditActions.DocumentVerified,
            TargetType = AuditTargetTypes.Document,
            TargetId = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow.AddDays(-100)
        };

        db.AuditEvents.Add(entity);
        await db.SaveChangesAsync();

        entity.Action = "tampered";

        var exception = await Assert.ThrowsAsync<AuditImmutabilityException>(
            () => db.SaveChangesAsync());

        Assert.Equal("audit_events_are_append_only", exception.Message);
    }

    [Fact]
    public async Task Guard_BlocksDeleteWithinTheRetentionFloorAndAllowsOlderEvents()
    {
        using var db = CreateContext();
        var recent = new AuditEvent
        {
            TenantId = TestTenantId,
            Action = AuditActions.EnrollmentCancelled,
            TargetType = AuditTargetTypes.Enrollment,
            TargetId = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow.AddDays(-10)
        };
        var old = new AuditEvent
        {
            TenantId = TestTenantId,
            Action = AuditActions.EnrollmentCancelled,
            TargetType = AuditTargetTypes.Enrollment,
            TargetId = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow.AddDays(-100)
        };

        db.AuditEvents.AddRange(recent, old);
        await db.SaveChangesAsync();

        db.AuditEvents.Remove(recent);
        var exception = await Assert.ThrowsAsync<AuditImmutabilityException>(() => db.SaveChangesAsync());
        Assert.Equal("audit_events_within_the_retention_floor_cannot_be_deleted", exception.Message);

        db.ChangeTracker.Clear();
        var oldEvent = await db.AuditEvents.SingleAsync(e => e.Id == old.Id);
        db.AuditEvents.Remove(oldEvent);
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public void Csv_WritesTheFixedHeaderAndEscapesReservedCharacters()
    {
        var dto = new AuditEventDto(
            Guid.NewGuid(),
            new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc),
            ActorId,
            "Admin",
            AuditActions.EnrollmentRejected,
            AuditTargetTypes.Enrollment,
            Guid.NewGuid(),
            null,
            "{\"motivo\":\"linha, com vírgula e \\\"aspas\\\"\"}",
            "10.0.0.1",
            "agent",
            "corr-1");

        var csv = AuditCsvWriter.Write(new List<AuditEventDto> { dto });
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            "OccurredAt,ActorUserId,ActorRole,Action,TargetType,TargetId,CorrelationId,IpAddress,UserAgent,BeforeJson,AfterJson",
            lines[0]);
        Assert.StartsWith("2026-10-27T12:00:00.000Z", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"{\"\"motivo\"\"", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_RejectsAnInvertedRange()
    {
        using var db = CreateContext();
        var service = new AuditQueryService(db);
        var filter = new AuditEventFilter(
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(-1),
            null, null, null, null);

        var exception = await Assert.ThrowsAsync<AuditException>(
            () => service.QueryAsync(filter, 1, 50));

        Assert.Equal("invalid_date_range", exception.Message);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task Query_RejectsARangeWiderThanTheLimit()
    {
        using var db = CreateContext();
        var service = new AuditQueryService(db);
        var filter = new AuditEventFilter(
            DateTime.UtcNow.AddDays(-401),
            DateTime.UtcNow,
            null, null, null, null);

        var exception = await Assert.ThrowsAsync<AuditException>(
            () => service.QueryAsync(filter, 1, 50));

        Assert.Equal("date_range_too_wide", exception.Message);
    }

    [Fact]
    public async Task Query_NormalizesPageAndPageSizeAndOrdersDescending()
    {
        using var db = CreateContext();
        for (var index = 0; index < 3; index++)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                TenantId = TestTenantId,
                Action = AuditActions.EnrollmentApproved,
                TargetType = AuditTargetTypes.Enrollment,
                TargetId = Guid.NewGuid(),
                OccurredAt = DateTime.UtcNow.AddMinutes(-index)
            });
        }

        await db.SaveChangesAsync();

        var service = new AuditQueryService(db);
        var result = await service.QueryAsync(new AuditEventFilter(null, null, null, null, null, null), 0, 0);

        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(3, result.Total);
        Assert.True(result.Items[0].OccurredAt >= result.Items[1].OccurredAt);
    }

    [Fact]
    public async Task Export_StartsWithTheUtf8BomAndTheFixedHeader()
    {
        using var db = CreateContext();
        db.AuditEvents.Add(new AuditEvent
        {
            TenantId = TestTenantId,
            Action = AuditActions.DocumentVerified,
            TargetType = AuditTargetTypes.Document,
            TargetId = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new AuditQueryService(db);
        var bytes = await service.ExportCsvAsync(new AuditEventFilter(null, null, null, null, null, null));

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("OccurredAt,ActorUserId", text, StringComparison.Ordinal);
    }

    private const int PayloadLimitForTest = 8192;
}
