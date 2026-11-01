using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ciclo.Api.Controllers;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Auth;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Security;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 200 — configuração e limites por escola. Prova no dono de domínio (<see cref="TenantService"/>)
/// a leitura com herança de nome, a normalização de flags e cores, todas as recusas de validação e a
/// invalidação de cache; e no host real a autenticação, o papel Admin e os códigos de erro HTTP.
/// </summary>
[Collection(ApiTestHost.CollectionName)]
public sealed class TenantConfigurationTests : IDisposable
{
    private const string ConfigurationRoute = "/api/tenants/me/configuration";

    private static readonly UpdateTenantConfigurationRequest EmptyRequest = new(
        null, null, null, null, null, null, null, null, null, null, null);

    private static readonly string[] DocumentsAndParentPortal = ["Documents", "ParentPortal"];
    private static readonly string[] DocumentsOnly = ["Documents"];
    private static readonly string[] NotificationsAndDocuments = ["Notifications", "Documents"];
    private static readonly string[] DocumentsAndNotifications = ["Documents", "Notifications"];
    private static readonly string[] RetiredFlag = ["Retired"];

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private readonly ApiTestHost _host;
    private readonly AppDbContext _db;
    private readonly MemoryCache _cache;
    private readonly TenantService _service;

    public TenantConfigurationTests(ApiTestHost host)
    {
        _host = host;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"tenant-config-{Guid.NewGuid()}")
            .Options;

        _db = new AppDbContext(options, new FixedTenantContext());
        _cache = new MemoryCache(new MemoryCacheOptions());
        _service = new TenantService(
            _db, null!, null!, new FixedTenantContext(), _cache, NullLogger<TenantService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    // ---------------------------------------------------------------- leitura

    [Fact]
    public async Task GetConfiguration_ReturnsEntityDefaults()
    {
        var tenant = AddTenant("Colegio Aurora");

        var dto = await _service.GetConfigurationAsync(tenant.Id);

        Assert.Equal(tenant.Id, dto.TenantId);
        Assert.Equal("Colegio Aurora", dto.DisplayName);
        Assert.Null(dto.LogoUrl);
        Assert.Null(dto.PrimaryColor);
        Assert.Null(dto.SecondaryColor);
        Assert.Equal("UTC", dto.TimeZone);
        Assert.Equal(2025, dto.DefaultSchoolYear);
        Assert.Equal("pt-BR", dto.Locale);
        Assert.Equal(tenant.StorageQuotaBytes, dto.StorageQuotaBytes);
        Assert.Equal(50, dto.UserQuota);
        Assert.Equal("Basic", dto.Plan);
        Assert.Empty(dto.FeatureFlags);
    }

    [Fact]
    public async Task GetConfiguration_PrefersDisplayNameOverName()
    {
        var tenant = AddTenant("Colegio Aurora", t => t.DisplayName = "Aurora Internacional");

        var dto = await _service.GetConfigurationAsync(tenant.Id);

        Assert.Equal("Aurora Internacional", dto.DisplayName);
    }

    [Fact]
    public async Task GetConfiguration_BlankDisplayNameFallsBackToName()
    {
        var tenant = AddTenant("Colegio Aurora", t => t.DisplayName = "   ");

        var dto = await _service.GetConfigurationAsync(tenant.Id);

        Assert.Equal("Colegio Aurora", dto.DisplayName);
    }

    [Fact]
    public async Task GetConfiguration_UnknownTenant_FailsWith404()
    {
        var error = await Assert.ThrowsAsync<AuthException>(() =>
            _service.GetConfigurationAsync(Guid.NewGuid()));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal("tenant_not_found", error.Message);
    }

    [Fact]
    public async Task GetConfiguration_InactiveTenant_FailsWith404()
    {
        var tenant = AddTenant("Colegio Aurora", t => t.IsActive = false);

        var error = await Assert.ThrowsAsync<AuthException>(() =>
            _service.GetConfigurationAsync(tenant.Id));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal("tenant_not_found", error.Message);
    }

    [Fact]
    public async Task GetConfiguration_FeatureFlagsAreOrderedAndDeduplicated()
    {
        var tenant = AddTenant("Colegio Aurora", t =>
            t.FeatureFlags = "ParentPortal,Documents,ParentPortal");

        var dto = await _service.GetConfigurationAsync(tenant.Id);

        Assert.Equal(DocumentsAndParentPortal, dto.FeatureFlags);
    }

    [Fact]
    public async Task GetConfiguration_LegacyUnknownFlag_ReturnsOnlyKnownFlags()
    {
        var tenant = AddTenant("Colegio Aurora", t => t.FeatureFlags = "Documents,Retired");

        var dto = await _service.GetConfigurationAsync(tenant.Id);

        Assert.Equal(DocumentsOnly, dto.FeatureFlags);
    }

    [Fact]
    public async Task GetConfiguration_TwoTenants_EachReceivesItsOwnConfiguration()
    {
        var first = AddTenant("Escola Um", t => t.PrimaryColor = "#112233");
        var second = AddTenant("Escola Dois", t => t.PrimaryColor = "#445566");

        var firstDto = await _service.GetConfigurationAsync(first.Id);
        var secondDto = await _service.GetConfigurationAsync(second.Id);

        Assert.Equal("#112233", firstDto.PrimaryColor);
        Assert.Equal("#445566", secondDto.PrimaryColor);
        Assert.Equal("Escola Um", firstDto.DisplayName);
        Assert.Equal("Escola Dois", secondDto.DisplayName);
    }

    [Fact]
    public async Task GetConfiguration_UsesOneCacheEntryPerTenant()
    {
        var first = AddTenant("Escola Um", t => t.PrimaryColor = "#112233");
        var second = AddTenant("Escola Dois", t => t.PrimaryColor = "#445566");

        await _service.GetConfigurationAsync(first.Id);
        await _service.GetConfigurationAsync(second.Id);

        Assert.True(_cache.TryGetValue("tenant-config:" + first.Id, out TenantConfigurationDto? cachedFirst));
        Assert.True(_cache.TryGetValue("tenant-config:" + second.Id, out TenantConfigurationDto? cachedSecond));
        Assert.Equal("#112233", cachedFirst!.PrimaryColor);
        Assert.Equal("#445566", cachedSecond!.PrimaryColor);
    }

    // ---------------------------------------------------------------- atualização

    [Fact]
    public async Task UpdateConfiguration_NormalizesColorsToUpperCase()
    {
        var tenant = AddTenant("Colegio Aurora");

        var dto = await _service.UpdateConfigurationAsync(
            tenant.Id, EmptyRequest with { PrimaryColor = "#1f6feb", SecondaryColor = "#0b3d91" });

        Assert.Equal("#1F6FEB", dto.PrimaryColor);
        Assert.Equal("#0B3D91", dto.SecondaryColor);

        var stored = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenant.Id);
        Assert.Equal("#1F6FEB", stored.PrimaryColor);
    }

    [Fact]
    public async Task UpdateConfiguration_BlankLogoUrl_ClearsTheLogo()
    {
        var tenant = AddTenant("Colegio Aurora", t => t.LogoUrl = "https://cdn/logo.png");

        var dto = await _service.UpdateConfigurationAsync(
            tenant.Id, EmptyRequest with { LogoUrl = "   " });

        Assert.Null(dto.LogoUrl);
    }

    [Fact]
    public async Task UpdateConfiguration_BlankDisplayName_FallsBackToName()
    {
        var tenant = AddTenant("Colegio Aurora", t => t.DisplayName = "Aurora Internacional");

        var dto = await _service.UpdateConfigurationAsync(
            tenant.Id, EmptyRequest with { DisplayName = "   " });

        Assert.Equal("Colegio Aurora", dto.DisplayName);
    }

    [Fact]
    public async Task UpdateConfiguration_NullFieldsPreserveCurrentValues()
    {
        var tenant = AddTenant("Colegio Aurora", t =>
        {
            t.DisplayName = "Aurora Internacional";
            t.PrimaryColor = "#112233";
            t.TimeZone = "UTC";
            t.Locale = "en-US";
            t.UserQuota = 120;
            t.Plan = TenantPlan.Standard;
        });

        var dto = await _service.UpdateConfigurationAsync(
            tenant.Id, EmptyRequest with { UserQuota = 10 });

        Assert.Equal(10, dto.UserQuota);
        Assert.Equal("Aurora Internacional", dto.DisplayName);
        Assert.Equal("#112233", dto.PrimaryColor);
        Assert.Equal("en-US", dto.Locale);
        Assert.Equal("Standard", dto.Plan);
    }

    [Fact]
    public async Task UpdateConfiguration_EmptyRequest_ReturnsCurrentState()
    {
        var tenant = AddTenant("Colegio Aurora", t =>
        {
            t.DisplayName = "Aurora Internacional";
            t.UserQuota = 120;
        });

        var dto = await _service.UpdateConfigurationAsync(tenant.Id, EmptyRequest);

        Assert.Equal("Aurora Internacional", dto.DisplayName);
        Assert.Equal(120, dto.UserQuota);

        var stored = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenant.Id);
        Assert.Equal("Aurora Internacional", stored.DisplayName);
        Assert.Equal(120, stored.UserQuota);
    }

    [Fact]
    public async Task UpdateConfiguration_PlanChangeWithoutFlags_UsesPlanDefaults()
    {
        var tenant = AddTenant("Colegio Aurora", t => t.Plan = TenantPlan.Basic);

        var dto = await _service.UpdateConfigurationAsync(
            tenant.Id, EmptyRequest with { Plan = "Premium" });

        Assert.Equal("Premium", dto.Plan);
        Assert.Equal(TenantFeature.DefaultFor(TenantPlan.Premium), dto.FeatureFlags);
    }

    [Fact]
    public async Task UpdateConfiguration_PlanChangeWithFlags_KeepsExplicitFlags()
    {
        var tenant = AddTenant("Colegio Aurora", t => t.Plan = TenantPlan.Basic);

        var dto = await _service.UpdateConfigurationAsync(
            tenant.Id,
            EmptyRequest with { Plan = "Premium", FeatureFlags = NotificationsAndDocuments });

        Assert.Equal(DocumentsAndNotifications, dto.FeatureFlags);
    }

    [Fact]
    public async Task UpdateConfiguration_UnknownTenant_FailsWith404()
    {
        var error = await Assert.ThrowsAsync<AuthException>(() =>
            _service.UpdateConfigurationAsync(Guid.NewGuid(), EmptyRequest with { UserQuota = 10 }));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal("tenant_not_found", error.Message);
    }

    [Fact]
    public async Task UpdateConfiguration_InvalidColor_FailsWith400()
    {
        var error = await AssertInvalidAsync(EmptyRequest with { PrimaryColor = "#GGGGGG" });

        Assert.Equal("invalid_color", error.Message);
    }

    [Fact]
    public async Task UpdateConfiguration_UnknownTimeZone_FailsWith400()
    {
        var error = await AssertInvalidAsync(EmptyRequest with { TimeZone = "Mars/Olympus" });

        Assert.Equal("invalid_time_zone", error.Message);
    }

    [Fact]
    public async Task UpdateConfiguration_SchoolYearOutOfRange_FailsWith400()
    {
        var error = await AssertInvalidAsync(EmptyRequest with { DefaultSchoolYear = 1999 });

        Assert.Equal("invalid_school_year", error.Message);
    }

    [Fact]
    public async Task UpdateConfiguration_UnsupportedLocale_FailsWith400()
    {
        var error = await AssertInvalidAsync(EmptyRequest with { Locale = "fr-FR" });

        Assert.Equal("invalid_locale", error.Message);
    }

    [Fact]
    public async Task UpdateConfiguration_UserQuotaOutOfRange_FailsWith400()
    {
        var error = await AssertInvalidAsync(EmptyRequest with { UserQuota = 0 });

        Assert.Equal("invalid_user_quota", error.Message);
    }

    [Fact]
    public async Task UpdateConfiguration_StorageQuotaOutOfRange_FailsWith400()
    {
        var error = await AssertInvalidAsync(EmptyRequest with { StorageQuotaBytes = 0 });

        Assert.Equal("invalid_storage_quota", error.Message);
    }

    [Fact]
    public async Task UpdateConfiguration_UnknownFeatureFlag_FailsWith400()
    {
        var error = await AssertInvalidAsync(
            EmptyRequest with { FeatureFlags = RetiredFlag });

        Assert.Equal("invalid_feature_flag", error.Message);
    }

    [Fact]
    public async Task UpdateConfiguration_UnknownPlan_FailsWith400()
    {
        var error = await AssertInvalidAsync(EmptyRequest with { Plan = "Enterprise" });

        Assert.Equal("invalid_plan", error.Message);
    }

    [Fact]
    public async Task UpdateConfiguration_InvalidRequest_DoesNotChangeDatabaseOrCache()
    {
        var tenant = AddTenant("Colegio Aurora", t => t.DisplayName = "Aurora Internacional");
        await _service.GetConfigurationAsync(tenant.Id);

        await Assert.ThrowsAsync<AuthException>(() =>
            _service.UpdateConfigurationAsync(
                tenant.Id, EmptyRequest with { DisplayName = "Novo", PrimaryColor = "vermelho" }));

        var stored = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenant.Id);
        Assert.Equal("Aurora Internacional", stored.DisplayName);

        var dto = await _service.GetConfigurationAsync(tenant.Id);
        Assert.Equal("Aurora Internacional", dto.DisplayName);
    }

    // ---------------------------------------------------------------- cache

    [Fact]
    public async Task GetConfiguration_ServesTheCachedValueUntilTheConfigurationIsWritten()
    {
        var tenant = AddTenant("Colegio Aurora", t => t.DisplayName = "Aurora Internacional");

        var firstRead = await _service.GetConfigurationAsync(tenant.Id);
        Assert.Equal("Aurora Internacional", firstRead.DisplayName);

        // Alteração direta no banco não aparece na leitura: a entrada está em cache.
        var tracked = await _db.Tenants.FirstAsync(t => t.Id == tenant.Id);
        tracked.DisplayName = "Alterado Sem Serviço";
        await _db.SaveChangesAsync();

        var cachedRead = await _service.GetConfigurationAsync(tenant.Id);
        Assert.Equal("Aurora Internacional", cachedRead.DisplayName);

        // A escrita pelo serviço remove a entrada: a leitura seguinte vê o valor novo.
        var updated = await _service.UpdateConfigurationAsync(
            tenant.Id, EmptyRequest with { DisplayName = "Aurora Nova" });
        Assert.Equal("Aurora Nova", updated.DisplayName);

        var afterWrite = await _service.GetConfigurationAsync(tenant.Id);
        Assert.Equal("Aurora Nova", afterWrite.DisplayName);
    }

    // ---------------------------------------------------------------- borda HTTP

    [Fact]
    public async Task ConfigurationEndpoints_WithoutAToken_Return401()
    {
        using var client = _host.CreateClient();

        using var read = await client.GetAsync(ConfigurationRoute);
        using var write = await client.PutAsJsonAsync(
            ConfigurationRoute, new { displayName = "Sem Token" });

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    [Fact]
    public async Task UpdateConfiguration_AsNonAdmin_Returns403()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), UserRole.Staff);

        using var response = await client.PutAsJsonAsync(
            ConfigurationRoute, new { displayName = "Tentativa do Staff" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public void ConfigurationEndpoints_RequireAuthenticationAndTheWriteIsAdminOnly()
    {
        var endpoints = EndpointInventory.All()
            .Where(e => e.Route == "api/tenants/me/configuration")
            .ToList();

        Assert.Equal(2, endpoints.Count);
        Assert.All(endpoints, endpoint =>
        {
            Assert.True(endpoint.RequiresAuthentication);
            Assert.False(endpoint.AllowsAnonymous);
        });

        var write = typeof(TenantsController)
            .GetMethod(nameof(TenantsController.UpdateConfiguration))!
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(write);
        Assert.Equal(AccessPolicy.Roles.UserManage, write!.Roles);
    }

    // ---------------------------------------------------------------- borda do controller

    [Fact]
    public async Task GetConfiguration_ThroughTheController_ReturnsTheConfiguration()
    {
        var tenant = AddTenant("Colegio Aurora", t => t.DisplayName = "Aurora Internacional");
        var controller = CreateController(tenant.Id);

        var result = await controller.GetConfiguration();

        var dto = Assert.IsType<TenantConfigurationDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(tenant.Id, dto.TenantId);
        Assert.Equal("Aurora Internacional", dto.DisplayName);
        Assert.Equal("Basic", dto.Plan);
    }

    [Fact]
    public async Task GetConfiguration_ThroughTheController_UnknownTenant_Returns404WithCode()
    {
        var controller = CreateController(Guid.NewGuid());

        var result = await controller.GetConfiguration();

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, error.StatusCode);
        Assert.Equal("tenant_not_found", ProblemCode(error));
    }

    [Fact]
    public async Task UpdateConfiguration_ThroughTheController_Returns400WithTheValidationCode()
    {
        var tenant = AddTenant("Colegio Aurora");
        var controller = CreateController(tenant.Id);

        var result = await controller.UpdateConfiguration(
            EmptyRequest with { PrimaryColor = "#GGGGGG" });

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, error.StatusCode);
        Assert.Equal("invalid_color", ProblemCode(error));
    }

    [Fact]
    public async Task UpdateConfiguration_ThroughTheController_IsVisibleOnTheNextRead()
    {
        var tenant = AddTenant("Colegio Aurora");
        var controller = CreateController(tenant.Id);

        // Popula o cache de configuração antes da escrita.
        await controller.GetConfiguration();

        var write = await controller.UpdateConfiguration(EmptyRequest with
        {
            DisplayName = "Escola Renomeada",
            PrimaryColor = "#1f6feb",
            UserQuota = 25,
            Plan = "Standard"
        });
        Assert.IsType<OkObjectResult>(write);

        var read = await controller.GetConfiguration();
        var dto = Assert.IsType<TenantConfigurationDto>(Assert.IsType<OkObjectResult>(read).Value);
        Assert.Equal("Escola Renomeada", dto.DisplayName);
        Assert.Equal("#1F6FEB", dto.PrimaryColor);
        Assert.Equal(25, dto.UserQuota);
        Assert.Equal("Standard", dto.Plan);
        Assert.Equal(TenantFeature.DefaultFor(TenantPlan.Standard), dto.FeatureFlags);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<AuthException> AssertInvalidAsync(UpdateTenantConfigurationRequest request)
    {
        var tenant = AddTenant("Colegio Aurora");

        var error = await Assert.ThrowsAsync<AuthException>(() =>
            _service.UpdateConfigurationAsync(tenant.Id, request));

        Assert.Equal(400, error.StatusCode);
        return error;
    }

    private Tenant AddTenant(string name, Action<Tenant>? configure = null)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = $"escola-{Guid.NewGuid():N}",
            Country = "BR",
            IsActive = true
        };

        configure?.Invoke(tenant);
        _db.Tenants.Add(tenant);
        _db.SaveChanges();
        return tenant;
    }

    private TenantsController CreateController(Guid tenantId)
    {
        var identity = new ClaimsIdentity([new Claim("tenant_id", tenantId.ToString())], "Test");

        return new TenantsController(_service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }

    private static string? ProblemCode(ObjectResult result) =>
        Assert.IsType<ProblemDetails>(result.Value).Extensions["code"] as string;

    private HttpClient AuthenticatedClient(Guid tenantId, UserRole role)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(tenantId, role));
        return client;
    }

    private static string Token(Guid tenantId, UserRole role)
    {
        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c["Jwt:Secret"]).Returns(ApiTestHost.JwtSecret);
        configuration.Setup(c => c["Jwt:Issuer"]).Returns("Ciclo");
        configuration.Setup(c => c["Jwt:Audience"]).Returns("Ciclo");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "config-test@escola.com",
            Name = "Config Test",
            TenantId = tenantId,
            Role = role
        };

        return new JwtTokenGenerator(configuration.Object).GenerateAccessToken(user);
    }
}
