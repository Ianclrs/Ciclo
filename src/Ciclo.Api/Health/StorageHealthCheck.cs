using Ciclo.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ciclo.Api.Health;

/// <summary>
/// Readiness do armazenamento: prova que o diretório raiz aceita escrita.
///
/// Não basta verificar que o diretório existe: disco cheio ou volume somente-leitura mantêm o
/// diretório e derrubam upload e download. A sonda escreve 1 byte e o remove em seguida.
/// </summary>
public sealed class StorageHealthCheck : IHealthCheck
{
    private const string ProbeFileName = "health-probe.tmp";

    private readonly string _rootPath;

    public StorageHealthCheck(IOptions<FileStorageOptions> options)
        => _rootPath = Path.GetFullPath(options.Value.RootPath);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var probePath = Path.Combine(_rootPath, ProbeFileName);

        try
        {
            Directory.CreateDirectory(_rootPath);
            await File.WriteAllBytesAsync(probePath, [1], cancellationToken);
            File.Delete(probePath);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("storage_unavailable", exception);
        }
    }
}
