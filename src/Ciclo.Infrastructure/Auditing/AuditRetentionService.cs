using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ciclo.Infrastructure.Auditing;

public sealed partial class AuditRetentionService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditRetentionService> _logger;

    public AuditRetentionService(IServiceScopeFactory scopeFactory, ILogger<AuditRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var purger = scope.ServiceProvider.GetRequiredService<AuditRetentionPurger>();
                var removed = await purger.PurgeAsync(DateTime.UtcNow, stoppingToken);
                LogPurged(_logger, removed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogPurgeFailed(_logger, ex);
            }
        }
    }

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information,
        Message = "Audit retention purge removed {Count} event(s).")]
    private static partial void LogPurged(ILogger logger, int count);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Error,
        Message = "Audit retention purge failed; the next cycle will retry.")]
    private static partial void LogPurgeFailed(ILogger logger, Exception exception);
}
