using Microsoft.AspNetCore.Mvc;

namespace Ciclo.Api.Middleware;

/// <summary>
/// Escrita da resposta de erro canônica da API (ProblemDetails / RFC 9457).
///
/// Existe para que o formato tenha um dono único: antes, cada ponto que precisava responder
/// um erro montava o próprio JSON — <c>{ error }</c> no middleware de exceção,
/// <c>{ error, message }</c> no de tenant, e ProblemDetails nos controllers. O frontend lia
/// <c>title</c> justamente no formato dos controllers, ou seja, os outros dois ficavam invisíveis.
/// </summary>
internal static class ProblemResponse
{
    internal const string ContentType = "application/problem+json";

    /// <summary>
    /// Código estável e legível por máquina (ex.: <c>forbidden</c>, <c>tenant_not_resolved</c>),
    /// exposto como membro de extensão do ProblemDetails. O <c>title</c> é para humanos e pode
    /// mudar; o <c>code</c> é contrato.
    /// </summary>
    internal const string CodeExtension = "code";

    internal static Task WriteAsync(HttpContext context, int status, string code, string title)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title
        };
        problem.Extensions[CodeExtension] = code;

        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: ContentType);
    }
}
