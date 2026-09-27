using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Ciclo.Api.Tests;

/// <summary>
/// Contrato de rotas (ver management/arch.md): todo endpoint de aplicação vive sob /api.
/// O SPA chama a API na própria origem — proxy do Vite em desenvolvimento, rewrite da Vercel
/// em produção. Uma rota nova fora de /api quebraria o proxy em produção e reintroduziria a
/// colisão com as rotas do React Router (/parent/*, /auth/google/callback).
///
/// Verifica os atributos de rota por reflexão, sem subir o host: determinístico e sem I/O.
/// </summary>
public class ApiRouteConventionTests
{
    /// <summary>Única exceção intencional: /Health é probe de infraestrutura do host da API.</summary>
    private const string ProbeController = "Health";

    [Fact]
    public void EveryApplicationEndpoint_IsUnderApiPrefix()
    {
        var offenders = EnumerateRoutes()
            .Where(r => !string.Equals(r.Controller, ProbeController, StringComparison.Ordinal))
            .Where(r => !r.Route.StartsWith("api/", StringComparison.Ordinal))
            .Select(r => $"{r.Controller}.{r.Action} -> '{r.Route}'")
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Endpoints fora de /api: {string.Join("; ", offenders)}. " +
            "Declare [Route(\"api/<resource>\")] no controller.");
    }

    [Fact]
    public void OnlyTheHealthProbe_LivesOutsideApiPrefix()
    {
        var controllersOutsideApi = EnumerateRoutes()
            .Where(r => !r.Route.StartsWith("api/", StringComparison.Ordinal))
            .Select(r => r.Controller)
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.Equal([ProbeController], controllersOutsideApi);
    }

    private static IEnumerable<(string Controller, string Action, string Route)> EnumerateRoutes()
    {
        var controllers = typeof(Program).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var controller in controllers)
        {
            var name = controller.Name.EndsWith("Controller", StringComparison.Ordinal)
                ? controller.Name[..^"Controller".Length]
                : controller.Name;

            var controllerRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template;

            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

            foreach (var action in actions)
            {
                foreach (var http in action.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var parts = new[] { controllerRoute, http.Template }
                        .Where(p => !string.IsNullOrEmpty(p))
                        .Select(p => p!.Replace("[controller]", name, StringComparison.OrdinalIgnoreCase));

                    yield return (name, action.Name, string.Join('/', parts));
                }
            }
        }
    }
}
