using System.Threading.Channels;
using Ciclo.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Ciclo.Infrastructure.Email;

/// <summary>
/// Porta de entrega de e-mail: quem cria a notificação entrega a mensagem aqui e segue.
///
/// Existe para que o envio não aconteça no escopo da requisição. O consumidor é
/// <see cref="EmailDispatchService"/>, que roda em background com escopo próprio.
/// </summary>
public interface IEmailDispatcher
{
    /// <summary>
    /// Enfileira a mensagem. Não bloqueia e não lança por fila cheia (ver
    /// <see cref="ChannelEmailDispatcher"/>): quem chama já persistiu a notificação in-app e uma
    /// exceção aqui faria a requisição falhar depois do commit.
    /// </summary>
    void Enqueue(EmailMessage message);
}

/// <summary>
/// Fila em memória com capacidade limitada.
///
/// Capacidade limitada é proposital: sem limite, uma sequência de transmissões grandes cresceria a
/// memória sem teto. Quando enche, a mensagem é **descartada com log de erro** (e não silenciada,
/// e não bloqueando a requisição) — a notificação in-app continua existindo; o que se perde é o
/// e-mail daquele destinatário.
///
/// Limite conhecido e aceito: a fila vive em memória, então um reinício do processo perde o que
/// ainda não foi enviado. O canal de e-mail hoje é inerte (<see cref="ConsoleEmailSender"/>), e
/// tornar a entrega durável (coluna de pendência + varredura) é decisão de produto, não deste item.
/// </summary>
internal sealed class ChannelEmailDispatcher : IEmailDispatcher, IEmailQueueStatus
{
    internal const int QueueCapacity = 1000;

    private readonly Channel<EmailMessage> _channel;
    private readonly ILogger<ChannelEmailDispatcher> _logger;

    public ChannelEmailDispatcher(ILogger<ChannelEmailDispatcher> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(QueueCapacity)
        {
            // Wait + somente TryWrite: é esta combinação que faz a fila cheia devolver `false`
            // imediatamente. Com DropWrite, TryWrite devolveria `true` mesmo descartando a
            // mensagem — a perda viraria silenciosa, sem log. E como nunca usamos WriteAsync,
            // "Wait" aqui não espera: apenas faz o TryWrite recusar.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });
    }

    public void Enqueue(EmailMessage message)
    {
        if (_channel.Writer.TryWrite(message))
        {
            return;
        }

#pragma warning disable CA1848, CA1873
        _logger.LogError(
            "Email dispatch queue is full ({Capacity} pending); the email to {Email} was dropped. "
            + "The in-app notification remains.",
            QueueCapacity, message.ToEmail);
#pragma warning restore CA1848, CA1873
    }

    /// <summary>Mensagens aguardando envio, lidas do canal no momento da consulta.</summary>
    public int Depth => _channel.Reader.Count;

    /// <summary>Capacidade configurada do canal.</summary>
    public int Capacity => QueueCapacity;

    internal IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
