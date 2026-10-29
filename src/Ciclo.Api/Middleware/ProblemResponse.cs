using Ciclo.Core.Auditing;
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

    /// <summary>
    /// Spec 130 — identificador do request, para que o relato do usuário leve direto ao log.
    /// </summary>
    internal const string CorrelationExtension = "correlationId";

    /// <summary>
    /// Resposta de erro de domínio a partir do código da exceção (M3).
    ///
    /// Antes, cada controller montava <c>Problem(title: ex.Message, statusCode: ...)</c> — 43
    /// pontos repetindo o mesmo formato e nenhum deles expondo o <c>code</c> de contrato. Agora
    /// existe um lugar: controller que lança exceção de domínio responde por aqui.
    ///
    /// O <c>title</c> repete o código de propósito: o texto para humanos é resolvido no cliente
    /// (`apiErrorMessage`), então a redação pode mudar sem quebrar o contrato, e o contrato não
    /// depende de cada chamador lembrar de preencher dois campos.
    /// </summary>
    internal static IActionResult FromCode(string code, int status)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = code
        };
        problem.Extensions[CodeExtension] = code;

        var result = new ObjectResult(problem) { StatusCode = status };
        result.ContentTypes.Add(ContentType);
        return result;
    }

    internal static Task WriteAsync(HttpContext context, int status, string code, string title)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title
        };
        problem.Extensions[CodeExtension] = code;

        if (context.Items[AuditCorrelation.ItemsKey] is string correlationId
            && !string.IsNullOrWhiteSpace(correlationId))
        {
            problem.Extensions[CorrelationExtension] = correlationId;
        }

        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: ContentType);
    }
}
