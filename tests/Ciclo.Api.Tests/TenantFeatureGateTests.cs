using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 200 — o portão das funcionalidades (ADR-200-2). Antes, `FeatureFlags` era só dado de
/// tela: desmarcar "Documentos" não bloqueava nada. Estes testes fixam o comportamento que o
/// atributo <c>[RequiresFeature]</c> consulta e que a escrita de configuração precisa invalidar.
/// </summary>
public sealed class TenantFeatureGateTests : IDisposable
{
    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private readonly AppDbContext _db;
    private readonly MemoryCache _cache;
    private readonly TenantService _tenantService;
    private readonly TenantFeatureGate _gate;

    public TenantFeatureGateTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"feature-gate-{Guid.NewGuid()}")
            .Options;

        _db = new AppDbContext(options, new FixedTenantContext());
        _cache = new MemoryCache(new MemoryCacheOptions());
        _tenantService = new TenantService(
            _db, null!, null!, new FixedTenantContext(), _cache, NullLogger<TenantService>.Instance);
        _gate = new TenantFeatureGate(_tenantService);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    private Tenant AddTenant(string featureFlags)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Colegio Aurora",
            Slug = $"escola-{Guid.NewGuid():N}",
            Country = "BR",
            IsActive = true,
            Plan = TenantPlan.Basic,
            FeatureFlags = featureFlags,
        };

        _db.Tenants.Add(tenant);
        _db.SaveChanges();
        return tenant;
    }

    [Fact]
    public async Task IsEnabledAsync_FlagPresent_Allows()
    {
        var tenant = AddTenant("Documents,ParentPortal");

        Assert.True(await _gate.IsEnabledAsync(tenant.Id, TenantFeature.Documents));
        Assert.True(await _gate.IsEnabledAsync(tenant.Id, TenantFeature.ParentPortal));
    }

    [Fact]
    public async Task IsEnabledAsync_FlagAbsent_Denies()
    {
        var tenant = AddTenant("Documents,ParentPortal");

        Assert.False(await _gate.IsEnabledAsync(tenant.Id, TenantFeature.AuditExport));
    }

    /// <summary>
    /// Lista vazia é o estado de escola que nunca salvou a configuração — a tela recusa salvar um
    /// conjunto vazio. Esse estado preserva o comportamento anterior ao portão: nada é bloqueado
    /// por ausência de configuração, porque bloquear aqui derrubaria funcionalidade em uso.
    /// </summary>
    [Fact]
    public async Task IsEnabledAsync_EmptyFlagList_TreatsAsNotConfigured()
    {
        var tenant = AddTenant(string.Empty);

        Assert.True(await _gate.IsEnabledAsync(tenant.Id, TenantFeature.Documents));
        Assert.True(await _gate.IsEnabledAsync(tenant.Id, TenantFeature.AuditExport));
    }

    [Fact]
    public async Task IsEnabledAsync_UnknownFeature_Denies()
    {
        var tenant = AddTenant("Documents,ParentPortal");

        Assert.False(await _gate.IsEnabledAsync(tenant.Id, "RelatoriosAvancados"));
    }

    [Fact]
    public async Task IsEnabledAsync_BlankFeature_Denies()
    {
        var tenant = AddTenant("Documents,ParentPortal");

        Assert.False(await _gate.IsEnabledAsync(tenant.Id, ""));
        Assert.False(await _gate.IsEnabledAsync(tenant.Id, "   "));
    }

    /// <summary>
    /// A leitura do portão atravessa o cache da configuração: desligar uma funcionalidade na tela
    /// precisa valer já na requisição seguinte, não só depois dos 5 minutos de expiração.
    /// </summary>
    [Fact]
    public async Task IsEnabledAsync_AfterConfigurationUpdate_ReflectsNewFlags()
    {
        var tenant = AddTenant("Documents,ParentPortal");

        Assert.True(await _gate.IsEnabledAsync(tenant.Id, TenantFeature.Documents));

        await _tenantService.UpdateConfigurationAsync(
            tenant.Id,
            new Ciclo.Infrastructure.Contracts.UpdateTenantConfigurationRequest(
                null, null, null, null, null, null, null, null, null,
                Array.Empty<string>(), null));

        Assert.False(await _gate.IsEnabledAsync(tenant.Id, TenantFeature.Documents));
    }
}
