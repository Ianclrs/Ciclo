using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 130 — a profundidade da fila é lida do canal, não de um contador paralelo.
///
/// O dispatcher é construído sem o worker: as mensagens enfileiradas ficam paradas e a leitura
/// é determinística (nada de espera ou concorrência).
/// </summary>
public sealed class EmailQueueStatusTests
{
    [Fact]
    public void Depth_TracksEnqueuedMessages()
    {
        var dispatcher = new ChannelEmailDispatcher(
            NullLogger<ChannelEmailDispatcher>.Instance);

        for (var i = 0; i < 3; i++)
        {
            dispatcher.Enqueue(new EmailMessage($"pai-{i}@test.com", "Pai", "Assunto", "Corpo"));
        }

        Assert.Equal(3, ((IEmailQueueStatus)dispatcher).Depth);
        Assert.Equal(ChannelEmailDispatcher.QueueCapacity, ((IEmailQueueStatus)dispatcher).Capacity);
    }

    [Fact]
    public void EmptyQueue_HasZeroDepth()
    {
        var dispatcher = new ChannelEmailDispatcher(
            NullLogger<ChannelEmailDispatcher>.Instance);

        Assert.Equal(0, ((IEmailQueueStatus)dispatcher).Depth);
    }
}
