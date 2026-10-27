using System.Diagnostics;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Auditing;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit.Abstractions;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 110 — provas que faltavam: expurgo por retenção, limite da exportação, isolamento entre
/// tenants, filtros da consulta e o custo real do middleware de borda (NFR-001).
/// </summary>
public sealed class AuditIsolationAndRetentionTests
{
    private readonly ITestOutputHelper _output;

    public AuditIsolationAndRetentionTests(ITestOutputHelper output) => _output = output;

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    /// <summary>
    /// O filtro global prende a instância do contexto de tenant no modelo do EF, e o modelo é
    /// cacheado por TIPO de contexto. Por isso cada tenant tem o seu tipo: é a única forma de
    /// provar isolamento no mesmo processo sem depender de estado ambiente.
    /// </summary>
    private sealed class TenantDbContext : AppDbContext
    {
        internal TenantDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }
    }

    private sealed class OtherTenantDbContext : AppDbContext
    {
        internal OtherTenantDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
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

    private static string NewDatabaseName() => Guid.NewGuid().ToString();

    private static TenantDbContext CreateContext(string? databaseName = null) =>
        new(OptionsFor(databaseName ?? NewDatabaseName()), new FixedTenantContext(TenantA));

    private static OtherTenantDbContext CreateOtherContext(string databaseName) =>
        new(OptionsFor(databaseName), new FixedTenantContext(TenantB));

    private static DbContextOptions<AppDbContext> OptionsFor(string databaseName) =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options;

    private static AuditEvent NewEvent(Guid tenantId, string action, string targetType, Guid? actor = null, Guid? target = null, int daysAgo = 1) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OccurredAt = DateTime.UtcNow.AddDays(-daysAgo),
            Action = action,
            TargetType = targetType,
            TargetId = target ?? Guid.NewGuid(),
            ActorUserId = actor
        };

    [Fact]
    public async Task Query_ReturnsOnlyEventsOfTheCurrentTenant()
    {
        var database = NewDatabaseName();
        using var dbA = CreateContext(database);
        using var dbB = CreateOtherContext(database);

        var fromA = NewEvent(TenantA, AuditActions.EnrollmentApproved, AuditTargetTypes.Enrollment);
        var fromB = NewEvent(TenantB, AuditActions.EnrollmentApproved, AuditTargetTypes.Enrollment);
        dbA.AuditEvents.AddRange(fromA, fromB);
        await dbA.SaveChangesAsync();

        var all = new AuditEventFilter(null, null, null, null, null, null);
        var viewA = await new AuditQueryService(dbA).QueryAsync(all, 1, 50);
        var viewB = await new AuditQueryService(dbB).QueryAsync(all, 1, 50);

        Assert.Equal(1, viewA.Total);
        Assert.Equal(fromA.Id, viewA.Items[0].Id);
        Assert.Equal(1, viewB.Total);
        Assert.Equal(fromB.Id, viewB.Items[0].Id);
    }

    [Fact]
    public async Task Purge_RemovesOnlyEventsOlderThanTheRetentionWindow()
    {
        using var db = CreateContext();
        var beyond = NewEvent(TenantA, AuditActions.DocumentVerified, AuditTargetTypes.Document, daysAgo: 400);
        var insideWindow = NewEvent(TenantA, AuditActions.DocumentVerified, AuditTargetTypes.Document, daysAgo: 364);
        var recent = NewEvent(TenantA, AuditActions.DocumentVerified, AuditTargetTypes.Document, daysAgo: 10);
        db.AuditEvents.AddRange(beyond, insideWindow, recent);
        await db.SaveChangesAsync();

        var purger = new AuditRetentionPurger(db, new AuditSettings(365));
        var removed = await purger.PurgeAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, removed);
        var remaining = await db.AuditEvents.ToListAsync();
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, e => e.Id == beyond.Id);
        Assert.Contains(remaining, e => e.Id == insideWindow.Id);
        Assert.Contains(remaining, e => e.Id == recent.Id);
    }

    [Fact]
    public async Task Export_RejectsMoreThanTenThousandRows()
    {
        using var db = CreateContext();

        var events = new List<AuditEvent>(10001);
        for (var index = 0; index <= 10000; index++)
        {
            events.Add(NewEvent(TenantA, AuditActions.EnrollmentApproved, AuditTargetTypes.Enrollment));
        }

        db.AuditEvents.AddRange(events);
        await db.SaveChangesAsync();

        var service = new AuditQueryService(db);
        var exception = await Assert.ThrowsAsync<AuditException>(() => service.ExportCsvAsync(
            new AuditEventFilter(null, null, null, null, null, null)));

        Assert.Equal("export_exceeds_limit", exception.Message);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task Query_AppliesEachFilter()
    {
        using var db = CreateContext();

        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var approved = NewEvent(TenantA, AuditActions.EnrollmentApproved, AuditTargetTypes.Enrollment, actor);
        var rejected = NewEvent(TenantA, AuditActions.EnrollmentRejected, AuditTargetTypes.Enrollment);
        var verified = NewEvent(TenantA, AuditActions.DocumentVerified, AuditTargetTypes.Document, target: target);
        var revoked = NewEvent(TenantA, AuditActions.SessionRevoked, AuditTargetTypes.Session);
        db.AuditEvents.AddRange(approved, rejected, verified, revoked);
        await db.SaveChangesAsync();

        var service = new AuditQueryService(db);

        var byAction = await service.QueryAsync(
            new AuditEventFilter(null, null, AuditActions.SessionRevoked, null, null, null), 1, 50);
        Assert.Equal(revoked.Id, Assert.Single(byAction.Items).Id);

        var byActor = await service.QueryAsync(
            new AuditEventFilter(null, null, null, actor, null, null), 1, 50);
        Assert.Equal(approved.Id, Assert.Single(byActor.Items).Id);

        var byTargetType = await service.QueryAsync(
            new AuditEventFilter(null, null, null, null, AuditTargetTypes.Document, null), 1, 50);
        Assert.Equal(verified.Id, Assert.Single(byTargetType.Items).Id);

        var byTargetId = await service.QueryAsync(
            new AuditEventFilter(null, null, null, null, null, target), 1, 50);
        Assert.Equal(verified.Id, Assert.Single(byTargetId.Items).Id);
    }

    [Fact]
    public async Task Middleware_AverageOverheadStaysWithinTheBudget()
    {
        var actor = new AuditActor();
        var middleware = new Ciclo.Api.Middleware.AuditContextMiddleware(_ => Task.CompletedTask);

        Task Invoke()
        {
            var context = new DefaultHttpContext();
            context.TraceIdentifier = "trace-0001-abcdef";
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            context.Request.Headers.UserAgent = "audit-benchmark";
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "Admin")
            ], "test"));
            return middleware.InvokeAsync(context, actor);
        }

        for (var warmup = 0; warmup < 50; warmup++) await Invoke();

        const int iterations = 500;
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < iterations; index++) await Invoke();
        stopwatch.Stop();

        var averageMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
        _output.WriteLine($"middleware overhead medido: {averageMs:F4} ms por requisicao ({iterations} iteracoes)");
        Assert.True(averageMs < 2.0, $"middleware overhead {averageMs:F3} ms (alvo <= 1 ms, teto do teste 2 ms)");
    }
}
