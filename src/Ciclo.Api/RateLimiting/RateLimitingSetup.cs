using System.Threading.RateLimiting;
using Ciclo.Api.Middleware;
using Microsoft.AspNetCore.RateLimiting;

namespace Ciclo.Api.RateLimiting;

/// <summary>
/// Rate limiting HTTP dos endpoints anônimos.
///
/// Por que existe: a única defesa contra tentativa e erro de senha era o lockout por conta do
/// Identity (5 tentativas / 30 min). Isso protege uma conta, mas não limita a origem — qualquer
/// pessoa pode martelar o login, varrer e-mails em massa no "esqueci a senha" ou bloquear
/// contas conhecidas de propósito. O limite por origem fecha essa porta; o lockout do Identity
/// continua valendo e não foi enfraquecido.
///
/// A partição é por endereço de origem (IP). Atenção ao valor escolhido: em redes escolares
/// muitos usuários legítimos saem pelo mesmo IP (NAT), então o limite precisa ser folgado o
/// bastante para não barrar uso legítimo e ainda assim tornar a varredura inviável.
/// </summary>
internal static class RateLimitingSetup
{
    /// <summary>Suficiente para uso humano (inclusive por trás de NAT) e hostil para varredura.</summary>
    private const int AuthRequestsPerMinute = 30;

    /// <summary>Criar escola é raro e caro; praticamente não há uso legítimo repetido.</summary>
    private const int SchoolRegistrationsPerHour = 5;

    internal static IServiceCollection AddCicloRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicies.Auth, context =>
                FixedWindowPerOrigin(context, TimeSpan.FromMinutes(1), AuthRequestsPerMinute));

            options.AddPolicy(RateLimitPolicies.SchoolRegistration, context =>
                FixedWindowPerOrigin(context, TimeSpan.FromHours(1), SchoolRegistrationsPerHour));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                // Mesmo formato canônico de erro do resto da API (ver ProblemResponse).
                await ProblemResponse.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    "too_many_requests",
                    "Too many requests from this origin. Try again later.");
            };
        });

        return services;
    }

    private static RateLimitPartition<string> FixedWindowPerOrigin(
        HttpContext context, TimeSpan window, int permitLimit)
        => RateLimitPartition.GetFixedWindowLimiter(
            // Sem IP resolvido (improvável, mas possível) os pedidos caem numa partição comum:
            // fail-closed, nunca "sem limite".
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown-origin",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = window,
                PermitLimit = permitLimit,
                QueueLimit = 0
            });
}
