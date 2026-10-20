using System.Text.Json;
using Ciclo.Api.Middleware;
using Ciclo.Core.Exceptions;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ciclo.Api.Tests;

/// <summary>
/// C5 — uma exceção de domínio precisa virar uma resposta HTTP com significado.
///
/// Antes, só <see cref="ForbiddenException"/> era traduzida: todo o resto chegava ao cliente
/// como 500 sem corpo, e em Development a página de exceção do host podia expor o stack trace
/// e o SQL. Cada caso abaixo é uma classe de falha distinta.
///
/// O middleware é exercitado direto, sem host: determinístico, sem banco e sem I/O.
/// </summary>
public sealed class ExceptionMiddlewareTests
{
    [Fact]
    public async Task ForbiddenException_BecomesForbiddenProblem()
    {
        var result = await InvokeAsync(new ForbiddenException("Acesso negado ao aluno."));

        Assert.Equal(StatusCodes.Status403Forbidden, result.Status);
        Assert.Equal("forbidden", result.Code);
        Assert.Equal("Acesso negado ao aluno.", result.Title);
    }

    [Fact]
    public async Task TenantNotResolvedException_BecomesUnauthorizedProblem()
    {
        var result = await InvokeAsync(new TenantNotResolvedException());

        Assert.Equal(StatusCodes.Status401Unauthorized, result.Status);
        Assert.Equal("tenant_not_resolved", result.Code);
    }

    /// <summary>LocalFileStorage lança isto em tentativa de path traversal.</summary>
    [Fact]
    public async Task UnauthorizedAccessException_BecomesForbiddenProblem()
    {
        var result = await InvokeAsync(new UnauthorizedAccessException("File path is outside storage root."));

        Assert.Equal(StatusCodes.Status403Forbidden, result.Status);
        Assert.Equal("forbidden", result.Code);
    }

    [Fact]
    public async Task FileNotFoundException_BecomesNotFoundProblem()
    {
        var result = await InvokeAsync(new FileNotFoundException("Documento não encontrado."));

        Assert.Equal(StatusCodes.Status404NotFound, result.Status);
        Assert.Equal("not_found", result.Code);
    }

    [Fact]
    public async Task DbUpdateException_BecomesGenericPersistenceProblem()
    {
        var result = await InvokeAsync(new DbUpdateException("duplicate key value violates unique constraint \"IX_Users_Email\""));

        Assert.Equal(StatusCodes.Status500InternalServerError, result.Status);
        Assert.Equal("persistence_error", result.Code);
    }

    /// <summary>
    /// O ponto central: uma exceção inesperada não pode devolver a mensagem original ao
    /// cliente — ela carrega SQL, caminho de disco ou connection string.
    /// </summary>
    [Fact]
    public async Task UnexpectedException_DoesNotLeakInternalMessage()
    {
        const string internalDetail = "Npgsql: Host=prod-db;Password=hunter2; SELECT * FROM Users";

        var result = await InvokeAsync(new InvalidOperationException(internalDetail));

        Assert.Equal(StatusCodes.Status500InternalServerError, result.Status);
        Assert.Equal("internal_error", result.Code);
        Assert.DoesNotContain("hunter2", result.RawBody, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", result.RawBody, StringComparison.Ordinal);
        Assert.DoesNotContain(internalDetail, result.RawBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Problem_IsWrittenWithTheCanonicalContentType()
    {
        var result = await InvokeAsync(new ForbiddenException("Acesso negado."));

        Assert.Equal("application/problem+json", result.ContentType);
    }

    /// <summary>
    /// Resposta já iniciada não pode ser reescrita: trocar o status depois do envio é
    /// impossível. O middleware deve registrar e propagar, não mascarar.
    /// </summary>
    [Fact]
    public async Task ResponseAlreadyStarted_PropagatesTheException()
    {
        var context = CreateContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        var middleware = CreateMiddleware(new InvalidOperationException("falha no meio do streaming"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
    }

    /// <summary>Cliente desconectou: não há para quem responder e não é erro do servidor.</summary>
    [Fact]
    public async Task ClientDisconnected_DoesNotThrowAndWritesNoBody()
    {
        var context = CreateContext();
        context.RequestAborted = new CancellationToken(canceled: true);
        var middleware = CreateMiddleware(new OperationCanceledException());

        await middleware.InvokeAsync(context);

        Assert.Equal(0, context.Response.Body.Length);
    }

    private static ExceptionMiddleware CreateMiddleware(Exception thrown)
        => new(_ => throw thrown, NullLogger<ExceptionMiddleware>.Instance);

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = "GET";
        context.Request.Path = "/api/test";
        return context;
    }

    private static async Task<ProblemResult> InvokeAsync(Exception thrown)
    {
        var context = CreateContext();

        await CreateMiddleware(thrown).InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;

        return new ProblemResult(
            context.Response.StatusCode,
            context.Response.ContentType,
            root.TryGetProperty("title", out var title) ? title.GetString() : null,
            root.TryGetProperty("code", out var code) ? code.GetString() : null,
            root.GetRawText());
    }

    private sealed record ProblemResult(
        int Status, string? ContentType, string? Title, string? Code, string RawBody);

    /// <summary>Feature de resposta que se declara já iniciada (HasStarted = true).</summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }
}
