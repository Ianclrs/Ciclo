namespace Ciclo.Api.RateLimiting;

/// <summary>
/// Nomes das políticas de rate limiting. São constantes (e não literais espalhados) porque os
/// atributos <c>[EnableRateLimiting]</c> e os testes de contrato precisam do mesmo valor.
///
/// Contrato verificado em <c>ApiRateLimitingTests</c>: todo endpoint <c>[AllowAnonymous]</c>
/// precisa declarar uma política — um endpoint público sem limite é força bruta gratuita.
/// </summary>
internal static class RateLimitPolicies
{
    /// <summary>Login, cadastro, refresh, recuperação de senha e callback do Google.</summary>
    internal const string Auth = "auth";

    /// <summary>Criação de escola (tenant) — operação rara e cara, limite bem mais estrito.</summary>
    internal const string SchoolRegistration = "school-registration";

    /// <summary>
    /// Download por URL assinada (Spec 170). Público por desenho — o token é a autorização — e
    /// com limite mais folgado que o de autenticação: uma turma inteira baixando documentos sai
    /// pelo mesmo IP (NAT da escola).
    /// </summary>
    internal const string Download = "download";
}
