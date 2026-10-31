using System.Text;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 110 — consulta e exportação da trilha: filtros, ordenação estável, paginação normalizada,
/// isolamento por tenant e limite da exportação.
/// </summary>
public sealed class AuditQueryServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-1111-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-2222-0000-0000-000000000002");

    private static readonly AuditEventFilter All = new(null, null, null, null, null, null);

    /// <summary>
    /// O filtro global de tenant prende a instância do contexto no modelo do EF, e o modelo é
    /// cacheado por TIPO de contexto. Um tipo por tenant é o que torna o isolamento observável no
    /// mesmo processo (mesma armadilha registrada em <c>DocumentComplianceTests</c>).
    /// </summary>
    private sealed class TenantContextA : AppDbContext
    {
        internal TenantContextA(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }
    }

    private sealed class TenantContextB : AppDbContext
    {
        internal TenantContextB(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        private readonly Guid _tenantId;

        internal FixedTenantContext(Guid tenantId) => _tenantId = tenantId;

        public Guid TenantId => _tenantId;

        public bool IsResolved => true;

        public void SetTenant(Guid tenantId) { }
    }

    private static DbContextOptions<AppDbContext> OptionsFor(string databaseName) =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options;

    private static TenantContextA CreateContext() =>
        new(OptionsFor(Guid.NewGuid().ToString()), new FixedTenantContext(TenantA));

    private static AuditEvent NewEvent(
        Guid tenantId, string action, string targetType, DateTime occurredAt, Guid? actor = null, Guid? target = null) =>
        new()
        {
            TenantId = tenantId,
            OccurredAt = occurredAt,
            Action = action,
            TargetType = targetType,
            TargetId = target ?? Guid.NewGuid(),
            ActorUserId = actor
        };

    private static string[] CsvLines(byte[] bytes)
    {
        Assert.Equal((byte)0xEF, bytes[0]);
        Assert.Equal((byte)0xBB, bytes[1]);
        Assert.Equal((byte)0xBF, bytes[2]);

        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public async Task Query_FiltersByActionAndDateRange()
    {
        using var db = CreateContext();
        var now = DateTime.UtcNow;

        var approvedRecent = NewEvent(
            TenantA, AuditActions.EnrollmentApproved, AuditTargetTypes.Enrollment, now.AddDays(-2));
        var approvedOld = NewEvent(
            TenantA, AuditActions.EnrollmentApproved, AuditTargetTypes.Enrollment, now.AddDays(-40));
        var rejectedRecent = NewEvent(
            TenantA, AuditActions.EnrollmentRejected, AuditTargetTypes.Enrollment, now.AddDays(-2));

        db.AuditEvents.AddRange(approvedRecent, approvedOld, rejectedRecent);
        await db.SaveChangesAsync();

        var filter = new AuditEventFilter(
            now.AddDays(-7), now, AuditActions.EnrollmentApproved, null, null, null);

        var result = await new AuditQueryService(db).QueryAsync(filter, 1, 50);

        Assert.Equal(1, result.Total);
        Assert.Equal(approvedRecent.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task Query_OrdersByOccurredAtThenIdDescending()
    {
        using var db = CreateContext();
        var occurredAt = DateTime.UtcNow.AddDays(-1);

        var older = NewEvent(TenantA, AuditActions.SessionRevoked, AuditTargetTypes.Session, occurredAt.AddDays(-1));

        var lowerId = NewEvent(TenantA, AuditActions.SessionRevoked, AuditTargetTypes.Session, occurredAt);
        lowerId.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var higherId = NewEvent(TenantA, AuditActions.SessionRevoked, AuditTargetTypes.Session, occurredAt);
        higherId.Id = Guid.Parse("ffffffff-0000-0000-0000-000000000002");

        db.AuditEvents.AddRange(older, lowerId, higherId);
        await db.SaveChangesAsync();

        var items = (await new AuditQueryService(db).QueryAsync(All, 1, 50)).Items;

        List<Guid> expected = [higherId.Id, lowerId.Id, older.Id];
        Assert.Equal(expected, items.Select(e => e.Id).ToList());
    }

    [Fact]
    public async Task Query_DoesNotReturnEventsFromAnotherTenant()
    {
        var database = Guid.NewGuid().ToString();
        using var dbA = new TenantContextA(OptionsFor(database), new FixedTenantContext(TenantA));
        using var dbB = new TenantContextB(OptionsFor(database), new FixedTenantContext(TenantB));

        dbA.AuditEvents.Add(NewEvent(
            TenantA, AuditActions.EnrollmentApproved, AuditTargetTypes.Enrollment, DateTime.UtcNow.AddHours(-1)));
        await dbA.SaveChangesAsync();

        var viewFromB = await new AuditQueryService(dbB).QueryAsync(All, 1, 50);

        Assert.Equal(0, viewFromB.Total);
        Assert.Empty(viewFromB.Items);
    }

    [Fact]
    public async Task Query_NormalizesPageAndPageSize()
    {
        using var db = CreateContext();
        var now = DateTime.UtcNow;

        db.AuditEvents.AddRange(
            NewEvent(TenantA, AuditActions.DocumentVerified, AuditTargetTypes.Document, now.AddHours(-1)),
            NewEvent(TenantA, AuditActions.DocumentRejected, AuditTargetTypes.Document, now.AddHours(-2)),
            NewEvent(TenantA, AuditActions.EnrollmentCancelled, AuditTargetTypes.Enrollment, now.AddHours(-3)));
        await db.SaveChangesAsync();

        var service = new AuditQueryService(db);

        var floor = await service.QueryAsync(All, 0, 0);
        Assert.Equal(1, floor.Page);
        Assert.Equal(50, floor.PageSize);
        Assert.Equal(3, floor.Items.Count);

        var capped = await service.QueryAsync(All, 1, 500);
        Assert.Equal(200, capped.PageSize);
        Assert.Equal(3, capped.Total);
    }

    [Fact]
    public async Task Query_WhenRangeIsInverted_ThrowsInvalidDateRange()
    {
        using var db = CreateContext();
        var now = DateTime.UtcNow;
        var filter = new AuditEventFilter(now, now.AddDays(-1), null, null, null, null);

        var exception = await Assert.ThrowsAsync<AuditException>(
            () => new AuditQueryService(db).QueryAsync(filter, 1, 50));

        Assert.Equal("invalid_date_range", exception.Message);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task Query_WhenRangeExceeds400Days_ThrowsDateRangeTooWide()
    {
        using var db = CreateContext();
        var now = DateTime.UtcNow;
        var filter = new AuditEventFilter(now.AddDays(-500), now, null, null, null, null);

        var exception = await Assert.ThrowsAsync<AuditException>(
            () => new AuditQueryService(db).QueryAsync(filter, 1, 50));

        Assert.Equal("date_range_too_wide", exception.Message);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task Export_OrdersChronologicallyAscending()
    {
        using var db = CreateContext();
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        db.AuditEvents.AddRange(
            NewEvent(TenantA, AuditActions.DocumentVerified, AuditTargetTypes.Document, baseTime.AddDays(2)),
            NewEvent(TenantA, AuditActions.DocumentVerified, AuditTargetTypes.Document, baseTime),
            NewEvent(TenantA, AuditActions.DocumentVerified, AuditTargetTypes.Document, baseTime.AddDays(1)));
        await db.SaveChangesAsync();

        var filter = new AuditEventFilter(baseTime.AddDays(-1), baseTime.AddDays(3), null, null, null, null);
        var lines = CsvLines(await new AuditQueryService(db).ExportCsvAsync(filter));

        Assert.Equal(4, lines.Length);
        Assert.StartsWith("2026-01-01T00:00:00.000Z", lines[1]);
        Assert.StartsWith("2026-01-02T00:00:00.000Z", lines[2]);
        Assert.StartsWith("2026-01-03T00:00:00.000Z", lines[3]);
    }

    [Fact]
    public async Task Export_WhenAboveTheLimit_ThrowsExportExceedsLimit()
    {
        using var db = CreateContext();
        var occurredAt = DateTime.UtcNow;

        for (var index = 0; index <= AuditQueryService.MaxExportRows; index++)
        {
            db.AuditEvents.Add(NewEvent(
                TenantA,
                AuditActions.EnrollmentApproved,
                AuditTargetTypes.Enrollment,
                occurredAt.AddMinutes(-index)));
        }

        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AuditException>(
            () => new AuditQueryService(db).ExportCsvAsync(All));

        Assert.Equal("export_exceeds_limit", exception.Message);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task Export_StartsWithUtf8Bom()
    {
        using var db = CreateContext();
        db.AuditEvents.Add(NewEvent(
            TenantA, AuditActions.EnrollmentApproved, AuditTargetTypes.Enrollment, DateTime.UtcNow.AddHours(-1)));
        await db.SaveChangesAsync();

        var bytes = await new AuditQueryService(db).ExportCsvAsync(All);

        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.StartsWith("OccurredAt,ActorUserId,ActorRole,Action,TargetType,TargetId,CorrelationId,IpAddress,UserAgent,BeforeJson,AfterJson\r\n", text);
    }
}
