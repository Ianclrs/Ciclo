using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ciclo.Infrastructure.Notifications;

/// <summary>
/// Host do ciclo agendado de comunicação proativa (Spec 140).
///
/// Usa o <see cref="BackgroundService"/> do próprio processo, sem agendador externo: é a decisão
/// já registrada em <c>management/arch.md</c> e o mesmo formato do despacho de e-mail existente.
///
/// Cada tick cria o próprio escopo, porque o job roda fora de qualquer requisição HTTP: o
/// <c>AppDbContext</c> e o <c>IEmailSender</c> são scoped e não podem ser capturados pelo construtor
/// do serviço hospedado.
///
/// Uma exceção de um tick é registrada e o laço continua no próximo intervalo: banco indisponível em
/// um ciclo não pode matar o agendador até o fim da vida do processo.
/// </summary>
public sealed class ScheduledNotificationJob : BackgroundService
{
    /// <summary>Intervalo fixo entre ciclos agendados.</summary>
    public static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledNotificationJob> _logger;

    public ScheduledNotificationJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledNotificationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);

        try
        {
            // O primeiro ciclo roda imediatamente; os seguintes, um por intervalo.
            do
            {
                await RunTickAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento normal do host: não há ciclo pendente a reportar.
        }
    }

    private async Task RunTickAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IProactiveNotificationService>();

            var gatilhos = await service.RunScheduledAsync(DateTime.UtcNow, stoppingToken);

            LogCicloConcluido(_logger, gatilhos);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host encerrando: não é falha do ciclo, e o laço termina limpo.
            throw;
        }
        catch (Exception ex)
        {
            // Erro de um tick não derruba o agendador: o próximo intervalo tenta de novo.
            LogCicloFalhou(_logger, ex);
        }
    }

#pragma warning disable CA1848, CA1873
    private static void LogCicloConcluido(ILogger logger, int gatilhos)
        => logger.LogInformation(
            "Proactive notification cycle completed with {Triggers} trigger(s)", gatilhos);

    private static void LogCicloFalhou(ILogger logger, Exception exception)
        => logger.LogError(exception, "Proactive notification cycle failed; next interval will retry");
#pragma warning restore CA1848, CA1873
}
