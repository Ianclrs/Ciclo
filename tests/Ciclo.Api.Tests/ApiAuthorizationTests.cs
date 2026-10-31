using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ciclo.Api.Tests;

/// <summary>
/// C3 — deny-by-default.
///
/// O projeto não tinha nenhuma <c>FallbackPolicy</c> e nenhum <c>[AllowAnonymous]</c>: um
/// endpoint sem <c>[Authorize]</c> era indistinguível de um endpoint público por decisão —
/// "público" e "esquecido" davam no mesmo. Com a política de fallback, esquecer o atributo
/// passa a negar; e marcar <c>[AllowAnonymous]</c> torna cada endpoint público uma decisão
/// explícita, verificável por este contrato.
/// </summary>
public class ApiAuthorizationTests
{
    /// <summary>
    /// Endpoints públicos por decisão. Alterar esta lista é intencional e revisável: qualquer
    /// endpoint novo que apareça público sem constar aqui quebra o teste.
    /// </summary>
    private static readonly string[] ExpectedAnonymousRoutes =
    [
        "Health",
        "Health/db",
        "api/auth/accept-invite",
        "api/auth/confirm-email",
        "api/auth/forgot-password",
        "api/auth/login",
        "api/auth/refresh",
        "api/auth/register",
        "api/auth/register/parent",
        "api/auth/resend-confirmation",
        "api/auth/reset-password",
        "api/tenants/register"
    ];

    [Fact]
    public void EveryEndpoint_IsExplicitlyClassified()
    {
        var unclassified = EndpointInventory.All()
            .Where(e => !e.RequiresAuthentication && !e.AllowsAnonymous)
            .Select(e => $"{e.Controller}.{e.Action} -> '{e.Route}'")
            .OrderBy(e => e, StringComparer.Ordinal)
            .ToList();

        Assert.True(unclassified.Count == 0,
            $"Endpoints sem [Authorize] e sem [AllowAnonymous]: {string.Join("; ", unclassified)}. " +
            "Classifique explicitamente — a política de fallback exige autenticação, mas a " +
            "classificação precisa ser uma decisão declarada, não um efeito colateral.");
    }

    [Fact]
    public void AnonymousEndpoints_AreExactlyTheExpectedOnes()
    {
        var anonymous = EndpointInventory.All()
            .Where(e => e.AllowsAnonymous)
            .Select(e => e.Route)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(ExpectedAnonymousRoutes, anonymous);
    }

    [Fact]
    public void NoEndpoint_IsBothAnonymousAndRoleRestricted()
    {
        var conflicting = EndpointInventory.All()
            .Where(e => e.AllowsAnonymous && e.RequiresAuthentication)
            .Select(e => $"{e.Controller}.{e.Action} -> '{e.Route}'")
            .OrderBy(e => e, StringComparer.Ordinal)
            .ToList();

        // [AllowAnonymous] vence [Authorize] em runtime: declarar os dois é contradição que
        // passa despercebida na leitura do controller.
        Assert.True(conflicting.Count == 0,
            $"Endpoints com [AllowAnonymous] e [Authorize] ao mesmo tempo: {string.Join("; ", conflicting)}.");
    }
}

/// <summary>
/// Verificação de C3 no host real: a política de fallback que o <c>Program</c> registra e o
/// efeito dela no pipeline. Usa o host compartilhado da coleção (ver <see cref="ApiTestHost"/>).
/// </summary>
[Collection(ApiTestHost.CollectionName)]
public class ApiAuthorizationPipelineTests
{
    private readonly ApiTestHost _host;

    public ApiAuthorizationPipelineTests(ApiTestHost host)
    {
        _host = host;
    }

    [Fact]
    public void FallbackPolicy_RequiresAuthenticatedUser()
    {
        var options = _host.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        Assert.NotNull(options.FallbackPolicy);
        Assert.Contains(options.FallbackPolicy!.Requirements,
            requirement => requirement is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task EndpointWithoutToken_IsDenied()
    {
        using var client = _host.CreateClient();

        using var response = await client.GetAsync("/api/students");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousEndpointWithoutToken_IsAllowed()
    {
        using var client = _host.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
