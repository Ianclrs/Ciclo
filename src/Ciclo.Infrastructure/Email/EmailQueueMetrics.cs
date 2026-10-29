using Ciclo.Core.Interfaces;
using Prometheus;

namespace Ciclo.Infrastructure.Email;

/// <summary>
/// Publica a profundidade e a capacidade da fila de e-mail no registro do Prometheus.
///
/// A leitura acontece no callback de coleta, direto do canal. Um contador incrementado no
/// enfileiramento e decrementado no consumo divergiria sempre que uma operação falhasse no meio;
/// ler o estado no scrape é O(1) e não tem estado próprio.
///
/// É construído uma vez no startup (a API resolve a instância) para registrar o callback; sem
/// isso o gauge nunca apareceria no scrape.
/// </summary>
public sealed class EmailQueueMetrics
{
    private readonly IEmailQueueStatus _status;
    private readonly Gauge _depth;
    private readonly Gauge _capacity;

    public EmailQueueMetrics(IEmailQueueStatus status)
    {
        _status = status;

        _depth = Metrics.CreateGauge(
            "ciclo_email_queue_depth",
            "Mensagens de e-mail aguardando envio.");

        _capacity = Metrics.CreateGauge(
            "ciclo_email_queue_capacity",
            "Capacidade do canal de e-mail.");

        Metrics.DefaultRegistry.AddBeforeCollectCallback(Collect);
    }

    private void Collect()
    {
        _depth.Set(_status.Depth);
        _capacity.Set(_status.Capacity);
    }
}
