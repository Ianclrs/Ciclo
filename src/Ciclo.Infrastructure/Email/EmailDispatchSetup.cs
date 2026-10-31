using Ciclo.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Ciclo.Infrastructure.Email;

/// <summary>
/// Registro do disparo de e-mail fora do escopo da requisição.
///
/// É público porque é o ponto de composição do módulo para quem hospeda a API (<c>Program</c>);
/// todo o resto do mecanismo (fila, background service, política de retry) fica interno.
///
/// O dispatcher é <b>singleton</b> de propósito: a fila precisa sobreviver às requisições. Já o
/// <c>IEmailSender</c> continua scoped e é resolvido pelo background service, um escopo por
/// mensagem — é aí que o envio passa a acontecer.
/// </summary>
public static class EmailDispatchSetup
{
    public static IServiceCollection AddCicloEmailDispatch(this IServiceCollection services)
    {
        services.AddSingleton<ChannelEmailDispatcher>();
        services.AddSingleton<IEmailDispatcher>(sp => sp.GetRequiredService<ChannelEmailDispatcher>());

        // Spec 130 — a mesma instância responde pelo estado da fila. A métrica que a expõe
        // (<see cref="EmailQueueMetrics"/>) registra o callback de coleta na construção e lê
        // este estado no scrape; a API resolve a instância no startup para que o callback exista.
        services.AddSingleton<IEmailQueueStatus>(sp => (IEmailQueueStatus)sp.GetRequiredService<IEmailDispatcher>());
        services.AddSingleton<EmailQueueMetrics>();

        services.AddHostedService<EmailDispatchService>();

        return services;
    }
}
