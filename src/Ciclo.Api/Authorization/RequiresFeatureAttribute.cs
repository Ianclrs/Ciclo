using Ciclo.Api.Middleware;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ciclo.Api.Authorization;

/// <summary>
/// Recusa a requisição quando a funcionalidade não está ligada para a escola do usuário.
///
/// O papel continua sendo o primeiro portão (<c>[Authorize(Roles = ...)]</c>): este atributo
/// responde a uma pergunta diferente — "esta escola contratou/habilitou a funcionalidade?".
/// Sem ele, as flags eram só dado de tela: desmarcar "Documentos" não bloqueava nada.
///
/// A checagem roda como filtro de autorização, depois de <c>TenantMiddleware</c>, então o tenant
/// já está resolvido. A resposta usa o mesmo formato canônico dos demais erros: 403 com
/// <c>code</c> = <see cref="FeatureDisabledCode"/>, que o frontend traduz.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresFeatureAttribute : Attribute, IAsyncAuthorizationFilter
{
    /// <summary>Código de contrato devolvido quando a funcionalidade está desligada.</summary>
    public const string FeatureDisabledCode = "feature_disabled";

    private readonly string _feature;

    public RequiresFeatureAttribute(string feature)
    {
        _feature = feature;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Sem sessão quem responde é o [Authorize]; sem tenant resolvido, o próprio controller
        // devolve `tenant_not_resolved`. Aqui só se decide o que a flag decide.
        var tenantContext = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
        if (!tenantContext.IsResolved)
            return;

        var gate = context.HttpContext.RequestServices.GetRequiredService<ITenantFeatureGate>();
        if (await gate.IsEnabledAsync(tenantContext.TenantId, _feature))
            return;

        context.Result = ProblemResponse.FromCode(
            FeatureDisabledCode,
            StatusCodes.Status403Forbidden);
    }
}
