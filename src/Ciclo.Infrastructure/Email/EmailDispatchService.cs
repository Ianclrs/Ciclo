using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ciclo.Infrastructure.Email;

/// <summary>
/// Consome a fila de e-mails em background, **com escopo próprio**.
///
/// É esta classe que elimina o defeito do item C9: antes, o envio era agendado com
/// <c>_ = SendEmailsAsync(...)</c> a partir da requisição, e continuava usando o
/// <c>AppDbContext</c> e o <c>IEmailSender</c> depois que a requisição terminava — escopo já
/// descartado. A falha caía num <c>catch</c> que logava warning, então o e-mail simplesmente não
/// saía e ninguém percebia. Agora o trabalho roda fora do escopo da requisição, e o escopo é criado
/// aqui, um por mensagem.
/// </summary>
internal sealed class EmailDispatchService : BackgroundService
{
    private readonly ChannelEmailDispatcher _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailDispatchService> _logger;

    public EmailDispatchService(
        ChannelEmailDispatcher queue,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailDispatchService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in _queue.ReadAllAsync(stoppingToken))
            {
                await EmailDelivery.TryDeliverAsync(
                    _scopeFactory, message, _logger, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento normal do host: nada pendente a reportar.
        }
    }
}

/// <summary>
/// Política de entrega de uma mensagem: escopo próprio por tentativa e retry **limitado**.
///
/// Retry limitado, e não infinito, porque o canal é best-effort: a notificação in-app é a fonte
/// durável. Retentar para sempre um destinatário inválido só consumiria a fila.
///
/// É lógica pura (recebe o escopo, não o cria), para poder ser verificada sem host nem espera real.
/// </summary>
internal static class EmailDelivery
{
    internal const int MaxAttempts = 3;

    internal static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);

    internal static async Task<bool> TryDeliverAsync(
        IServiceScopeFactory scopeFactory,
        EmailMessage message,
        ILogger logger,
        CancellationToken cancellationToken,
        TimeSpan? retryDelay = null)
    {
        var delay = retryDelay ?? DefaultRetryDelay;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                // Escopo próprio por tentativa: o IEmailSender é scoped, e reusar o escopo de uma
                // tentativa que falhou significaria reusar um provedor possivelmente já descartado.
                await using var scope = scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

                await sender.SendAsync(message.ToEmail, message.ToName, message.Subject, message.Body);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Host encerrando: não é falha do envio e não deve virar retry.
                throw;
            }
            catch (Exception ex)
            {
                LogAttempt(logger, ex, message, attempt);

                if (attempt < MaxAttempts)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        return false;
    }

    private static void LogAttempt(ILogger logger, Exception exception, EmailMessage message, int attempt)
    {
#pragma warning disable CA1848, CA1873
        if (attempt < MaxAttempts)
        {
            logger.LogWarning(
                exception,
                "Email delivery attempt {Attempt}/{Max} failed for {Email}; retrying",
                attempt, MaxAttempts, message.ToEmail);
        }
        else
        {
            // Erro, e não warning: foi a última tentativa, então este e-mail NÃO será entregue.
            logger.LogError(
                exception,
                "Email delivery failed permanently for {Email} after {Max} attempts; giving up",
                message.ToEmail, MaxAttempts);
        }
#pragma warning restore CA1848, CA1873
    }
}
