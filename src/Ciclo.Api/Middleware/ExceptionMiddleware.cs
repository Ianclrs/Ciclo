using Ciclo.Core.Exceptions;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Api.Middleware;

/// <summary>
/// Tradutor único de exceção para resposta HTTP, no formato canônico da API
/// (ProblemDetails / RFC 9457 — ver management/arch.md).
///
/// Antes, só <see cref="ForbiddenException"/> era tratada: qualquer outra exceção de domínio
/// virava 500 sem corpo, e o cliente não tinha como distinguir "sem permissão" de "erro
/// interno" nem obter um código estável. O mapeamento vive aqui e em nenhum outro lugar.
///
/// Regras:
/// - exceção inesperada NUNCA expõe a mensagem original (pode conter SQL, caminho de disco ou
///   connection string) — ela é registrada no log e o cliente recebe uma mensagem genérica;
/// - a resposta já iniciada não é reescrita (não é possível trocar o status depois do envio);
/// - cancelamento por desconexão do cliente não é erro do servidor.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // O cliente desconectou antes da resposta. Não há para quem responder e não é falha
            // do servidor — tentar escrever aqui geraria uma segunda exceção.
#pragma warning disable CA1848, CA1873
            _logger.LogDebug("Request aborted by the client: {Method} {Path}",
                context.Request.Method, context.Request.Path);
#pragma warning restore CA1848, CA1873
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                // A resposta já começou a ser enviada: reescrever o status é impossível.
                // Registrar e propagar é o único caminho honesto.
#pragma warning disable CA1848, CA1873
                _logger.LogError(ex, "Exception after the response started: {Method} {Path}",
                    context.Request.Method, context.Request.Path);
#pragma warning restore CA1848, CA1873
                throw;
            }

            await WriteProblemAsync(context, ex);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception exception)
    {
        var (status, code, title) = Map(exception);

#pragma warning disable CA1848, CA1873
        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception ({Code}) on {Method} {Path}",
                code, context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request failed ({Code}) on {Method} {Path}",
                code, context.Request.Method, context.Request.Path);
        }
#pragma warning restore CA1848, CA1873

        context.Response.Clear();
        await ProblemResponse.WriteAsync(context, status, code, title);
    }

    /// <summary>
    /// Tradução exceção → (status, código estável, título para o cliente).
    /// O título de um 5xx é sempre genérico; o detalhe fica só no log.
    /// </summary>
    private static (int Status, string Code, string Title) Map(Exception exception) => exception switch
    {
        AuthException auth => (auth.StatusCode, "auth_error", auth.Message),

        ForbiddenException forbidden =>
            (StatusCodes.Status403Forbidden, "forbidden", forbidden.Message),

        TenantNotResolvedException =>
            (StatusCodes.Status401Unauthorized, "tenant_not_resolved",
                "Tenant context could not be resolved from the current request."),

        // LocalFileStorage lança isto em tentativa de path traversal.
        UnauthorizedAccessException =>
            (StatusCodes.Status403Forbidden, "forbidden", "Access denied."),

        FileNotFoundException or KeyNotFoundException =>
            (StatusCodes.Status404NotFound, "not_found", "The requested resource was not found."),

        DbUpdateException =>
            (StatusCodes.Status500InternalServerError, "persistence_error",
                "The request could not be completed."),

        _ => (StatusCodes.Status500InternalServerError, "internal_error",
            "An unexpected error occurred.")
    };
}
