using Microsoft.Extensions.Logging;

namespace Ciclo.Api.Tests;

/// <summary>
/// Logger que guarda as mensagens já formatadas, para verificar o que é — e o que não é —
/// registrado. Usado nos testes de vazamento de segredo em log (C7).
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    internal List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Messages.Add(formatter(state, exception));
}
