using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// Contrato do colégio do usuário logado (chip da topbar): o nome e o slug vêm do recurso de
/// tenant, não do usuário. Só o endpoint autenticado responde.
/// </summary>
public class TenantSummaryTests
{
    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private static (TenantService Service, AppDbContext Db) Create()
    {
        var tenantContext = new FixedTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"tenant-summary-{Guid.NewGuid()}")
            .Options;
        var db = new AppDbContext(options, tenantContext);
        var service = new TenantService(db, null!, null!, tenantContext, NullLogger<TenantService>.Instance);
        return (service, db);
    }

    [Fact]
    public async Task GetCurrent_ReturnsNameAndSlugOfTheTenant()
    {
        var (service, db) = Create();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Colégio Modelo",
            Slug = "colegio-modelo",
            Country = "BR",
            IsActive = true,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var result = await service.GetCurrentAsync(tenant.Id);

        Assert.Equal(tenant.Id, result.Id);
        Assert.Equal("Colégio Modelo", result.Name);
        Assert.Equal("colegio-modelo", result.Slug);
    }

    [Fact]
    public async Task GetCurrent_WithUnknownTenant_FailsWith404()
    {
        var (service, _) = Create();

        var error = await Assert.ThrowsAsync<AuthException>(() => service.GetCurrentAsync(Guid.NewGuid()));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal("tenant_not_found", error.Message);
    }

    [Fact]
    public void CurrentTenantEndpoint_RequiresAuthentication()
    {
        var endpoint = EndpointInventory.All().Single(e => e.Route == "api/tenants/me");

        Assert.True(endpoint.RequiresAuthentication);
        Assert.False(endpoint.AllowsAnonymous);
    }
}
