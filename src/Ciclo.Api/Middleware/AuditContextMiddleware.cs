using System.Security.Claims;
using Ciclo.Core.Auditing;
using Ciclo.Infrastructure.Auditing;

namespace Ciclo.Api.Middleware;

public sealed class AuditContextMiddleware
{
    private readonly RequestDelegate _next;

    public AuditContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AuditActor actor)
    {
        // A correlação já foi resolvida uma única vez na borda (Spec 130, CorrelationMiddleware) e
        // publicada em Items; reutilizá-la evita recompilar o padrão de validação a cada requisição
        // e garante que log, resposta e evento de auditoria carreguem o mesmo valor. O fallback
        // existe para execução fora desse pipeline (testes, chamada direta do middleware).
        var correlationId =
            context.Items[AuditCorrelation.ItemsKey] is string resolved && resolved.Length > 0
                ? resolved
                : AuditCorrelation.Resolve(
                    context.Request.Headers[AuditCorrelation.HeaderName].ToString(),
                    context.TraceIdentifier);

        actor.SetCorrelationId(correlationId);
        actor.Capture(
            ResolveUserId(context.User),
            context.User.FindFirst(ClaimTypes.Role)?.Value ?? context.User.FindFirst("role")?.Value,
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Headers.UserAgent.ToString());

        context.Response.Headers[AuditCorrelation.HeaderName] = correlationId;

        await _next(context);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId) ? userId : null;
}
