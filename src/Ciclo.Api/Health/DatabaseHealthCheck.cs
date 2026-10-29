using Ciclo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ciclo.Api.Health;

/// <summary>
/// Readiness do banco: abre uma conexão real, com orçamento de 2 segundos.
///
/// Usa <see cref="IServiceScopeFactory"/> porque o health check é resolvido como singleton pelo
/// registro de health checks, enquanto <see cref="AppDbContext"/> é scoped — injetar o contexto
/// diretamente criaria uma dependência de vida inválida (captive dependency).
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseHealthCheck(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // `CanConnectAsync` só faz sentido para provider relacional; o InMemory (usado nos
            // testes) responde sempre `false` por não ter conexão externa para verificar.
            if (!db.Database.IsRelational())
            {
                return HealthCheckResult.Healthy();
            }

            return await db.Database.CanConnectAsync(timeout.Token)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("database_unreachable");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("database_timeout");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("database_error", exception);
        }
    }
}
