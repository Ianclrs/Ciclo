using Microsoft.AspNetCore.Authorization;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Api.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        var endpoint = context.GetEndpoint();
        var requireAuth = endpoint?.Metadata
            .GetMetadata<AuthorizeAttribute>() != null;

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var tenantIdClaim = context.User.FindFirst("tenant_id");
            if (tenantIdClaim == null || !Guid.TryParse(tenantIdClaim.Value, out var tenantId))
            {
                if (requireAuth)
                {
                    // Mesmo formato canônico da API — ver ProblemResponse.
                    await ProblemResponse.WriteAsync(
                        context,
                        StatusCodes.Status401Unauthorized,
                        "tenant_not_resolved",
                        "Tenant context could not be resolved from the current request.");
                    return;
                }
            }
            else
            {
                tenantContext.SetTenant(tenantId);
            }
        }

        await _next(context);
    }
}
