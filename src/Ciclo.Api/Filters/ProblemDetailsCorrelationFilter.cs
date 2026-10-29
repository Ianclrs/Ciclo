using Ciclo.Api.Middleware;
using Ciclo.Core.Auditing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ciclo.Api.Filters;

/// <summary>
/// Acrescenta <c>correlationId</c> a toda resposta ProblemDetails produzida por controller.
///
/// Existe porque <c>ProblemResponse.FromCode</c> é chamado em dezenas de pontos e não recebe
/// <see cref="HttpContext"/>: passar o contexto por parâmetro obrigaria a alterar todos eles e
/// reintroduziria a duplicação que o M3 eliminou. O filtro roda antes da serialização, no ponto
/// único onde o <c>ObjectResult</c> já existe.
/// </summary>
public sealed class ProblemDetailsCorrelationFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Result is not ObjectResult { Value: ProblemDetails problem })
        {
            return;
        }

        if (context.HttpContext.Items[AuditCorrelation.ItemsKey] is string correlationId
            && !string.IsNullOrWhiteSpace(correlationId))
        {
            problem.Extensions[ProblemResponse.CorrelationExtension] = correlationId;
        }
    }
}
