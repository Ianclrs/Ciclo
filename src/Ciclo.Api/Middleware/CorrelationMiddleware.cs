using Ciclo.Core.Auditing;
using Serilog.Context;

namespace Ciclo.Api.Middleware;

/// <summary>
/// Resolve a correlação uma única vez por request e a disponibiliza para todo o pipeline:
/// HttpContext.Items (auditoria e ProblemDetails), cabeçalho de resposta e LogContext.
/// </summary>
public sealed class CorrelationMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = AuditCorrelation.Resolve(
            context.Request.Headers[AuditCorrelation.HeaderName].ToString(),
            context.TraceIdentifier);

        context.Items[AuditCorrelation.ItemsKey] = correlationId;
        context.Response.Headers[AuditCorrelation.HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
