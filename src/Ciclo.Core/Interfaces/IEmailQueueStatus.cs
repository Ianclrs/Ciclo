namespace Ciclo.Core.Interfaces;

/// <summary>
/// Estado observável da fila de envio de e-mail.
///
/// A leitura acontece no momento da coleta de métricas, direto do canal — não existe contador
/// paralelo que possa divergir do estado real.
/// </summary>
public interface IEmailQueueStatus
{
    int Depth { get; }
    int Capacity { get; }
}
