using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 200 / FR-004 — a cota de usuários é regra do dono (<see cref="TenantService"/>): conta apenas
/// os usuários do tenant pedido, lê o valor corrente do banco (sem cache) e recusa com 409
/// <c>user_quota_exceeded</c> quando a cota já está atingida.
/// </summary>
public sealed class TenantQuotaTests : IDisposable
{
    // O filtro global por tenant e o cache de planos do EF congelam o valor do tenant na primeira
    // execução de uma mesma forma de consulta no processo. Todos os testes usam o mesmo
    // identificador de escola para que esse valor continue correto entre eles; cada teste abre o
    // próprio banco em memória, então não há compartilhamento de dados.
    private static readonly Guid TargetTenantId = new("20000000-0000-0000-0000-000000000200");
    private static readonly Guid OtherTenantId = new("20000000-0000-0000-0000-000000000201");

    // O TenantContext real usa AsyncLocal (igual ao host): o filtro global do EF captura a
    // instância quando o modelo é construído e lê o valor corrente dela em cada consulta.
    private readonly TenantContext _tenantContext;
    private readonly AppDbContext _db;
    private readonly TenantService _service;

    public TenantQuotaTests()
    {
        _tenantContext = new TenantContext();

        // Opções próprias garantem um modelo do EF construído a partir deste TenantContext
        // (AsyncLocal, como no host), em vez do modelo capturado por um fake de outro teste.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"tenant-quota-{Guid.NewGuid()}")
            .EnableSensitiveDataLogging()
            .Options;

        var cache = new MemoryCache(new MemoryCacheOptions());
        _db = new AppDbContext(options, _tenantContext);
        _service = new TenantService(
            _db, null!, null!, _tenantContext, cache, NullLogger<TenantService>.Instance);
    }

    public void Dispose()
    {
        _tenantContext.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task EnsureUserQuotaAsync_BelowTheQuota_DoesNotThrow()
    {
        var tenant = AddTenant(TargetTenantId, userQuota: 3);
        AddUsers(tenant.Id, 2);
        _tenantContext.SetTenant(tenant.Id);

        await _service.EnsureUserQuotaAsync(tenant.Id);
    }

    [Fact]
    public async Task EnsureUserQuotaAsync_AllowsExactlyTheQuotaAndRefusesTheNextUser()
    {
        // Quota 2: a primeira e a segunda conta são aceitas; a terceira é recusada.
        var tenant = AddTenant(TargetTenantId, userQuota: 2);
        _tenantContext.SetTenant(tenant.Id);

        await _service.EnsureUserQuotaAsync(tenant.Id);
        AddUsers(tenant.Id, 1);
        await _service.EnsureUserQuotaAsync(tenant.Id);

        AddUsers(tenant.Id, 1);
        var error = await Assert.ThrowsAsync<AuthException>(() =>
            _service.EnsureUserQuotaAsync(tenant.Id));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal("user_quota_exceeded", error.Message);
    }

    [Fact]
    public async Task EnsureUserQuotaAsync_DoesNotCountUsersOfAnotherTenant()
    {
        var target = AddTenant(TargetTenantId, userQuota: 1);
        var other = AddTenant(OtherTenantId, userQuota: 10);
        AddUsers(other.Id, 5);

        _tenantContext.SetTenant(target.Id);

        // Cinco usuários em outro tenant não consomem a cota do tenant pedido.
        await _service.EnsureUserQuotaAsync(target.Id);

        AddUsers(target.Id, 1);
        var error = await Assert.ThrowsAsync<AuthException>(() =>
            _service.EnsureUserQuotaAsync(target.Id));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal("user_quota_exceeded", error.Message);
    }

    [Fact]
    public async Task EnsureUserQuotaAsync_ReadsTheCurrentQuotaFromTheDatabase()
    {
        var tenant = AddTenant(TargetTenantId, userQuota: 5);
        AddUsers(tenant.Id, 2);
        _tenantContext.SetTenant(tenant.Id);

        // A entrada do cache de configuração não pode mascarar uma redução de cota.
        await _service.GetConfigurationAsync(tenant.Id);
        await _service.EnsureUserQuotaAsync(tenant.Id);

        var tracked = await _db.Tenants.FirstAsync(t => t.Id == tenant.Id);
        tracked.UserQuota = 2;
        await _db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<AuthException>(() =>
            _service.EnsureUserQuotaAsync(tenant.Id));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal("user_quota_exceeded", error.Message);
    }

    [Fact]
    public async Task EnsureUserQuotaAsync_ZeroQuota_RefusesEveryCreation()
    {
        // Dado legado com quota zero: nenhuma criação é permitida.
        var tenant = AddTenant(TargetTenantId, userQuota: 0);
        _tenantContext.SetTenant(tenant.Id);

        var error = await Assert.ThrowsAsync<AuthException>(() =>
            _service.EnsureUserQuotaAsync(tenant.Id));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal("user_quota_exceeded", error.Message);
    }

    [Fact]
    public async Task EnsureUserQuotaAsync_UnknownTenant_FailsWith404()
    {
        var error = await Assert.ThrowsAsync<AuthException>(() =>
            _service.EnsureUserQuotaAsync(Guid.NewGuid()));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal("tenant_not_found", error.Message);
    }

    [Fact]
    public async Task EnsureUserQuotaAsync_InactiveTenant_FailsWith404()
    {
        var tenant = AddTenant(TargetTenantId, userQuota: 10, isActive: false);

        var error = await Assert.ThrowsAsync<AuthException>(() =>
            _service.EnsureUserQuotaAsync(tenant.Id));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal("tenant_not_found", error.Message);
    }

    private Tenant AddTenant(Guid id, int userQuota, bool isActive = true)
    {
        var tenant = new Tenant
        {
            Id = id,
            Name = "Colegio Aurora",
            Slug = $"escola-{Guid.NewGuid():N}",
            Country = "BR",
            IsActive = isActive,
            UserQuota = userQuota
        };

        _db.Tenants.Add(tenant);
        _db.SaveChanges();
        return tenant;
    }

    private void AddUsers(Guid tenantId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var email = $"{Guid.NewGuid():N}@escola.com";
            _db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = email,
                UserName = email,
                Name = "Usuario de Teste",
                Role = UserRole.Parent,
                IsActive = true,
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        _db.SaveChanges();
    }
}
