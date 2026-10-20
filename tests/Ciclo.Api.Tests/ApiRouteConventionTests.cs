using Microsoft.AspNetCore.Mvc;

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
    /// <summary>
    /// A4: a API de rematrícula era uma segunda superfície sobre a MESMA tabela de matrículas —
    /// mesmos create/list/get/approve/reject — e por isso as duas listagens devolviam as mesmas
    /// linhas. O fluxo único é `/api/enrollments`. Este teste impede a reintrodução.
    /// </summary>
    [Fact]
    public void TheDuplicatedEnrollmentApi_IsNotPublished()
    {
        var duplicated = EndpointInventory.All()
            .Where(endpoint => endpoint.Route.StartsWith("api/reenrollments", StringComparison.OrdinalIgnoreCase))
            .Select(endpoint => $"{endpoint.Controller}.{endpoint.Action} {endpoint.Route}")
            .ToList();

        Assert.True(duplicated.Count == 0,
            "A rematrícula voltou a ter API própria sobre a mesma tabela. Ver A4 em "
            + "plano-correcoes.md. Rotas: " + string.Join(", ", duplicated));
    }

    [Fact]
    public void EveryApplicationEndpoint_IsUnderApiPrefix()
    {
        var offenders = EndpointInventory.All()
            .Where(r => !string.Equals(r.Controller, EndpointInventory.ProbeController, StringComparison.Ordinal))
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
        var controllersOutsideApi = EndpointInventory.All()
            .Where(r => !r.Route.StartsWith("api/", StringComparison.Ordinal))
            .Select(r => r.Controller)
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.Equal([EndpointInventory.ProbeController], controllersOutsideApi);
    }

    /// <summary>
    /// Backdoor de teste não pode ser publicado. Um controller de conveniência para teste
    /// manual (existiu o /api/test-auth) vira endpoint de produção — e um endpoint que concede
    /// acesso por role sem nenhuma regra de negócio é superfície de ataque gratuita.
    /// Contrato de teste vive em tests/Ciclo.Api.Tests, nunca na API.
    /// </summary>
    [Fact]
    public void NoTestOrDebugBackdoor_IsPublished()
    {
        var backdoorControllers = typeof(Program).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .Where(t => t.Name.Contains("Test", StringComparison.OrdinalIgnoreCase)
                     || t.Name.Contains("Debug", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.FullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(backdoorControllers.Count == 0,
            $"Controllers de teste/debug publicados na API: {string.Join("; ", backdoorControllers)}. " +
            "Mova o contrato para tests/Ciclo.Api.Tests.");

        var backdoorRoutes = EndpointInventory.All()
            .Where(r => r.Route.Contains("test", StringComparison.OrdinalIgnoreCase)
                     || r.Route.Contains("debug", StringComparison.OrdinalIgnoreCase))
            .Select(r => $"{r.Controller}.{r.Action} -> '{r.Route}'")
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Assert.True(backdoorRoutes.Count == 0,
            $"Rotas de teste/debug publicadas: {string.Join("; ", backdoorRoutes)}.");
    }

    /// <summary>
    /// O fluxo OAuth do Google foi removido por decisão explícita registrada em
    /// management/cross-session/plano-correcoes.md (item C8). Ele existia quebrado e no pior
    /// lugar possível: ligava a conta Google a um usuário já existente pelo e-mail, sem validar
    /// que o e-mail era verificado e sem filtrar tenant — ou seja, portava tomada de conta.
    /// Também mandava dados para um SPA cuja rota de destino não existe.
    ///
    /// O botão continua no frontend, sem fluxo por trás. Reintroduzir o fluxo é decisão
    /// consciente: exige implementar a validação de e-mail/tenant e remover este contrato.
    /// </summary>
    [Fact]
    public void NoGoogleOAuthEndpoint_IsPublished()
    {
        var googleRoutes = EndpointInventory.All()
            .Where(r => r.Route.Contains("google", StringComparison.OrdinalIgnoreCase))
            .Select(r => $"{r.Controller}.{r.Action} -> '{r.Route}'")
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Assert.True(googleRoutes.Count == 0,
            $"Fluxo OAuth do Google publicado novamente: {string.Join("; ", googleRoutes)}. " +
            "Ele foi removido por portar tomada de conta — ver plano-correcoes.md, item C8.");
    }
}
