using Ciclo.Api.Health;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Storage;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 130 — verificações de readiness.
///
/// O caso que importa é o do armazenamento: um diretório pode existir e ainda assim recusar
/// escrita (volume somente-leitura, disco cheio). O teste de falha aponta a raiz para um ARQUIVO,
/// o que faz a criação do diretório falhar de forma determinística.
/// </summary>
public sealed class HealthCheckTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(), "ciclo-health", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Storage_WithWritableDirectory_IsHealthy()
    {
        var check = new StorageHealthCheck(Options.Create(new FileStorageOptions
        {
            RootPath = _rootPath
        }));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.False(File.Exists(Path.Combine(_rootPath, "health-probe.tmp")));
    }

    [Fact]
    public async Task Storage_WithUnwritableRoot_IsUnhealthy()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_rootPath)!);
        await File.WriteAllTextAsync(_rootPath, "não é um diretório");

        var check = new StorageHealthCheck(Options.Create(new FileStorageOptions
        {
            // Um diretório "dentro" de um arquivo: a criação falha de forma determinística.
            RootPath = Path.Combine(_rootPath, "sub")
        }));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Database_WithInMemoryProvider_IsHealthy()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(new FixedTenantContext());
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase($"health-{Guid.NewGuid():N}"));

        await using var provider = services.BuildServiceProvider();
        var check = new DatabaseHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.True(
            result.Status == HealthStatus.Healthy,
            $"esperado Healthy, obtido {result.Status}: {result.Description} {result.Exception}");
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; } = Guid.NewGuid();

        public bool IsResolved => true;

        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public void Dispose()
    {
        if (File.Exists(_rootPath))
        {
            File.Delete(_rootPath);
        }

        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
