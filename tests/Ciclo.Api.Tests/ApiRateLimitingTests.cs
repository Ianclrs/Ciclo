using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ciclo.Api.Tests;

/// <summary>
/// C6 — todo endpoint anônimo de aplicação precisa de rate limiting.
///
/// A única defesa contra tentativa e erro de senha era o lockout por conta do Identity. Ele
/// protege a conta, mas não limita a origem: dá para varrer e-mails no "esqueci a senha" ou
/// bloquear de propósito uma conta conhecida. Sem limite por origem, um endpoint público é
/// força bruta gratuita — e um endpoint novo pode nascer sem limite por esquecimento, que é
/// exatamente o que este contrato impede.
///
/// A exceção é o probe <c>/Health</c>: fica fora de <c>/api</c> (é infraestrutura, não
/// superfície de aplicação — ver management/arch.md) e limitá-lo atrapalharia o health check
/// do host.
/// </summary>
public class ApiRateLimitingTests
{
    /// <summary>Política esperada por endpoint anônimo — mudar isto é decisão consciente.</summary>
    private static readonly Dictionary<string, string> ExpectedPolicyByRoute = new(StringComparer.Ordinal)
    {
        ["api/auth/confirm-email"] = "auth",
        ["api/auth/forgot-password"] = "auth",
        ["api/auth/login"] = "auth",
        ["api/auth/refresh"] = "auth",
        ["api/auth/register"] = "auth",
        ["api/auth/register/parent"] = "auth",
        ["api/auth/resend-confirmation"] = "auth",
        ["api/auth/reset-password"] = "auth",
        ["api/tenants/register"] = "school-registration"
    };

    [Fact]
    public void EveryAnonymousApplicationEndpoint_HasARateLimitPolicy()
    {
        var unprotected = EndpointInventory.All()
            .Where(e => e.AllowsAnonymous && IsApplicationEndpoint(e))
            .Where(e => string.IsNullOrEmpty(e.RateLimitPolicy))
            .Select(e => $"{e.Controller}.{e.Action} -> '{e.Route}'")
            .OrderBy(e => e, StringComparer.Ordinal)
            .ToList();

        Assert.True(unprotected.Count == 0,
            $"Endpoints públicos sem [EnableRateLimiting]: {string.Join("; ", unprotected)}. " +
            "Declare uma política de rate limiting — endpoint anônimo sem limite é alvo livre.");
    }

    [Fact]
    public void AnonymousApplicationEndpoints_UseTheExpectedPolicy()
    {
        var actual = EndpointInventory.All()
            .Where(e => e.AllowsAnonymous && IsApplicationEndpoint(e))
            .ToDictionary(e => e.Route, e => e.RateLimitPolicy ?? "(nenhuma)", StringComparer.Ordinal);

        Assert.Equal(
            ExpectedPolicyByRoute.OrderBy(p => p.Key, StringComparer.Ordinal),
            actual.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    /// <summary>
    /// O probe de saúde é público por decisão e fica sem limite por decisão. Se um dia ele
    /// entrar em <c>/api</c>, este teste avisa — o contrato acima passaria a exigi-lo.
    /// </summary>
    [Fact]
    public void HealthProbe_IsPublicAndIntentionallyNotRateLimited()
    {
        var probe = EndpointInventory.All()
            .Where(e => string.Equals(e.Controller, EndpointInventory.ProbeController, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(probe);
        Assert.All(probe, e =>
        {
            Assert.True(e.AllowsAnonymous);
            Assert.Null(e.RateLimitPolicy);
            Assert.False(IsApplicationEndpoint(e));
        });
    }

    private static bool IsApplicationEndpoint(EndpointInventory.Endpoint endpoint)
        => endpoint.Route.StartsWith("api/", StringComparison.Ordinal);
}

/// <summary>
/// Verificação de C6 no host real: o limite existe, responde 429 com <c>Retry-After</c> e no
/// formato canônico de erro. Usa o host compartilhado (ver <see cref="ApiTestHost"/>).
/// </summary>
[Collection(ApiTestHost.CollectionName)]
public class ApiRateLimitingPipelineTests
{
    private readonly ApiTestHost _host;

    public ApiRateLimitingPipelineTests(ApiTestHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task AnonymousAuthEndpoint_ReturnsTooManyRequests_WhenHammered()
    {
        using var client = _host.CreateClient();

        using var rejected = await HammerUntilRejectedAsync(client, "/api/auth/refresh");

        Assert.NotNull(rejected);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected!.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);

        var body = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("too_many_requests", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("title", out _), "a resposta precisa estar em ProblemDetails");
    }

    /// <summary>
    /// Usa <c>/api/auth/refresh</c> de propósito: sem cookie ele responde 401 na hora, sem tocar
    /// o banco — dá para estourar o limite sem pagar I/O a cada tentativa.
    /// </summary>
    private static async Task<HttpResponseMessage?> HammerUntilRejectedAsync(HttpClient client, string route)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var response = await client.PostAsync(route, content: null);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return response;
            }

            response.Dispose();
        }

        return null;
    }
}
