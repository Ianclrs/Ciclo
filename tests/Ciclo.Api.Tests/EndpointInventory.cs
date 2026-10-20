using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace Ciclo.Api.Tests;

/// <summary>
/// Inventário dos endpoints publicados pela API, obtido por reflexão sobre os atributos.
/// Não sobe host nem toca banco: determinístico e sem I/O.
///
/// É a base dos contratos verificados em <see cref="ApiRouteConventionTests"/> (prefixo /api,
/// ausência de backdoor) e em <see cref="ApiAuthorizationTests"/> (classificação explícita de
/// autorização).
/// </summary>
internal static class EndpointInventory
{
    /// <summary>Única exceção intencional: /Health é probe de infraestrutura do host da API.</summary>
    internal const string ProbeController = "Health";

    internal sealed record Endpoint(
        string Controller,
        string Action,
        string Route,
        bool RequiresAuthentication,
        bool AllowsAnonymous,
        string? RateLimitPolicy);

    internal static IReadOnlyList<Endpoint> All()
    {
        var controllers = typeof(Program).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

        var endpoints = new List<Endpoint>();

        foreach (var controller in controllers)
        {
            var name = controller.Name.EndsWith("Controller", StringComparison.Ordinal)
                ? controller.Name[..^"Controller".Length]
                : controller.Name;

            var controllerRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template;
            var controllerRequiresAuth = controller.GetCustomAttribute<AuthorizeAttribute>() is not null;
            var controllerAllowsAnonymous = controller.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
            var controllerRateLimit = controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;

            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

            foreach (var action in actions)
            {
                var requiresAuth = controllerRequiresAuth
                    || action.GetCustomAttribute<AuthorizeAttribute>() is not null;
                var allowsAnonymous = controllerAllowsAnonymous
                    || action.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
                var rateLimitPolicy = controllerRateLimit
                    ?? action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;

                foreach (var http in action.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var parts = new[] { controllerRoute, http.Template }
                        .Where(p => !string.IsNullOrEmpty(p))
                        .Select(p => p!.Replace("[controller]", name, StringComparison.OrdinalIgnoreCase));

                    endpoints.Add(new Endpoint(
                        name, action.Name, string.Join('/', parts), requiresAuth, allowsAnonymous, rateLimitPolicy));
                }
            }
        }

        return endpoints;
    }
}
