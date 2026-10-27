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
        var correlationId = AuditCorrelation.Resolve(
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
