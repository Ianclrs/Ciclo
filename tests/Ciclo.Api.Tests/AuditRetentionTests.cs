using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Auditing;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 110 — expurgo de retenção: remove apenas eventos fora do prazo configurado, devolve o total
/// removido e atravessa tenants de forma explícita (a única operação autorizada a fazer isso).
/// </summary>
public sealed class AuditRetentionTests
{
    private const int RetentionDays = 365;

    /// <summary>
    /// Instante fixo: o purgador recebe o agora por parâmetro (NFR-005), então o teste não depende do
    /// relógio da máquina.
    /// </summary>
    private static readonly DateTime UtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Guid SeedTenantId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>
    /// Tenant deliberadamente NÃO resolvido: o filtro global compara com <see cref="Guid.Empty"/>, e
    /// as linhas semeadas têm tenant preenchido. Só <c>IgnoreQueryFilters()</c> as encontra — se o
    /// purgador perder essa chamada, nada é removido e estes testes falham.
    /// </summary>
    private sealed class UnresolvedTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;

        public bool IsResolved => false;

        public void SetTenant(Guid tenantId) =>
            throw new NotSupportedException("O expurgo de retenção não resolve tenant.");
    }

    /// <summary>
    /// Tipo derivado próprio: o filtro global prende a instância do contexto no modelo do EF, e o
    /// modelo é cacheado por TIPO de contexto (mesma armadilha registrada em
    /// <c>DocumentComplianceTests</c>). O isolamento entre testes vem do banco InMemory de nome único.
    /// </summary>
    private sealed class RetentionTestDbContext : AppDbContext
    {
        internal RetentionTestDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }
    }

    private static RetentionTestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RetentionTestDbContext(options, new UnresolvedTenantContext());
    }

    private static AuditEvent NewEvent(DateTime occurredAt) => new()
    {
        TenantId = SeedTenantId,
        OccurredAt = occurredAt,
        Action = AuditActions.EnrollmentApproved,
        TargetType = AuditTargetTypes.Enrollment,
        TargetId = Guid.NewGuid()
    };

    [Fact]
    public async Task Purge_DeletesOnlyEventsOlderThanRetention()
    {
        await using var db = CreateContext();

        // Três eventos fora da retenção de 365 dias e dois dentro dela.
        db.AuditEvents.AddRange(
            NewEvent(UtcNow.AddDays(-400)),
            NewEvent(UtcNow.AddDays(-500)),
            NewEvent(UtcNow.AddDays(-600)),
            NewEvent(UtcNow.AddDays(-10)),
            NewEvent(UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        // Prova de que o filtro global está ativo: sem IgnoreQueryFilters as linhas não aparecem.
        Assert.Equal(0, await db.AuditEvents.CountAsync());
        Assert.Equal(5, await db.AuditEvents.IgnoreQueryFilters().CountAsync());

        var purger = new AuditRetentionPurger(db, new AuditSettings(RetentionDays));

        // O laço de lotes (PurgeBatchSize = 5000) é exercitado com 3 eventos elegíveis; semear 5001
        // linhas InMemory só encareceria a suíte sem cobrir nenhum caminho adicional.
        var removed = await purger.PurgeAsync(UtcNow, CancellationToken.None);

        Assert.Equal(3, removed);

        var remaining = await db.AuditEvents.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, e => e.OccurredAt < UtcNow.AddDays(-RetentionDays));
        Assert.Contains(remaining, e => e.OccurredAt == UtcNow.AddDays(-10));
    }

    [Fact]
    public async Task Purge_ReturnsTheNumberOfRemovedEvents()
    {
        await using var db = CreateContext();

        db.AuditEvents.AddRange(
            NewEvent(UtcNow.AddDays(-366)),
            NewEvent(UtcNow.AddDays(-400)),
            NewEvent(UtcNow.AddDays(-800)),
            NewEvent(UtcNow.AddDays(-1000)),
            NewEvent(UtcNow.AddDays(-5)));
        await db.SaveChangesAsync();

        var purger = new AuditRetentionPurger(db, new AuditSettings(RetentionDays));

        var removed = await purger.PurgeAsync(UtcNow, CancellationToken.None);

        Assert.Equal(4, removed);
        Assert.Equal(1, await db.AuditEvents.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Purge_WhenNothingIsEligible_ReturnsZero()
    {
        await using var db = CreateContext();

        db.AuditEvents.AddRange(
            NewEvent(UtcNow.AddDays(-10)),
            NewEvent(UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        var purger = new AuditRetentionPurger(db, new AuditSettings(RetentionDays));

        var removed = await purger.PurgeAsync(UtcNow, CancellationToken.None);

        Assert.Equal(0, removed);
        Assert.Equal(2, await db.AuditEvents.IgnoreQueryFilters().CountAsync());
    }
}
