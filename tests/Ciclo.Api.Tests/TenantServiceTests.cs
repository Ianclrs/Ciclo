using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Api.Tests;

/// <summary>
/// Testa o cadastro de escola (TenantService.RegisterSchoolAsync): validação de
/// país, geração de slug, unicidade do email do admin e persistência do tenant.
/// </summary>
public sealed class TenantServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AppDbContext _dbContext;
    private readonly ITenantService _tenantService;

    public TenantServiceTests()
    {
        var services = new ServiceCollection();

        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddDataProtection();

        services.AddIdentityCore<User>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequireUppercase = true;
            options.Password.RequireDigit = true;
            options.Password.RequireNonAlphanumeric = true;
            options.User.RequireUniqueEmail = false;
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        // Tenant context mock: no-op SetTenant (TenantService seta o TenantId do
        // admin explicitamente, então o AutoSetTenantId não precisa agir).
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(tc => tc.IsResolved).Returns(false);
        services.AddScoped<ITenantContext>(_ => tenantContextMock.Object);

        services.AddScoped<ITenantService, TenantService>();
        services.AddSingleton(new Mock<ILogger<TenantService>>().Object);

        _serviceProvider = services.BuildServiceProvider();

        _dbContext = _serviceProvider.GetRequiredService<AppDbContext>();
        _tenantService = _serviceProvider.GetRequiredService<ITenantService>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    private static RegisterSchoolRequest ValidRequest(
        string schoolName = "Colegio Aurora",
        string country = "BR",
        string adminEmail = "admin@aurora.com") =>
        new(schoolName, country, "12.345.678/0001-90", "+55 11 91234-5678",
            "Rua A, 100", "Sao Paulo", "SP", "01310-100", "Admin Aurora", adminEmail, "Senha@123");

    [Fact]
    public async Task RegisterSchoolAsync_ValidRequest_CreatesTenantAndAdmin()
    {
        var result = await _tenantService.RegisterSchoolAsync(ValidRequest());

        Assert.NotEqual(Guid.Empty, result.TenantId);
        Assert.Equal("BR", result.Country);
        Assert.Equal("colegio-aurora", result.Slug);

        var tenant = await _dbContext.Tenants.FirstAsync(t => t.Id == result.TenantId);
        Assert.Equal("Colegio Aurora", tenant.Name);
        Assert.Equal("BR", tenant.Country);
        Assert.Equal("12.345.678/0001-90", tenant.DocumentoFiscal);
        Assert.Equal("Sao Paulo", tenant.Cidade);
        Assert.Equal("SP", tenant.Estado);
        Assert.Equal("01310-100", tenant.Cep);
        Assert.True(tenant.IsActive);

        var admin = await _dbContext.Users.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "admin@aurora.com");
        Assert.Equal(result.TenantId, admin.TenantId);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.True(admin.IsActive);
    }

    [Fact]
    public async Task RegisterSchoolAsync_AccentedName_GeneratesAsciiSlug()
    {
        var result = await _tenantService.RegisterSchoolAsync(
            ValidRequest(schoolName: "Colégio Êxito Águia", adminEmail: "admin@exito.com"));

        Assert.Equal("colegio-exito-aguia", result.Slug);
    }

    [Fact]
    public async Task RegisterSchoolAsync_CountryIsNormalizedToUpperCase()
    {
        var result = await _tenantService.RegisterSchoolAsync(
            ValidRequest(country: "us", adminEmail: "admin@us.com"));

        Assert.Equal("US", result.Country);
    }

    [Fact]
    public async Task RegisterSchoolAsync_UnsupportedCountry_ThrowsAuthException()
    {
        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            _tenantService.RegisterSchoolAsync(ValidRequest(country: "XX")));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("unsupported_country", ex.Message);
    }

    [Fact]
    public async Task RegisterSchoolAsync_EmptySchoolName_ThrowsAuthException()
    {
        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            _tenantService.RegisterSchoolAsync(ValidRequest(schoolName: "   ")));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("school_name_required", ex.Message);
    }

    [Fact]
    public async Task RegisterSchoolAsync_DuplicateAdminEmail_ThrowsAuthException()
    {
        await _tenantService.RegisterSchoolAsync(
            ValidRequest(schoolName: "Primeira", adminEmail: "dup@test.com"));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            _tenantService.RegisterSchoolAsync(
                ValidRequest(schoolName: "Segunda", adminEmail: "dup@test.com")));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("email_already_registered", ex.Message);
    }

    [Fact]
    public async Task RegisterSchoolAsync_DuplicateName_GeneratesUniqueSlug()
    {
        var first = await _tenantService.RegisterSchoolAsync(
            ValidRequest(schoolName: "Colegio Aurora", adminEmail: "a1@test.com"));
        var second = await _tenantService.RegisterSchoolAsync(
            ValidRequest(schoolName: "Colegio Aurora", adminEmail: "a2@test.com"));

        Assert.Equal("colegio-aurora", first.Slug);
        Assert.NotEqual(first.Slug, second.Slug);
        Assert.StartsWith("colegio-aurora-", second.Slug);
        Assert.True(second.Slug.Length <= 100);
    }
}
