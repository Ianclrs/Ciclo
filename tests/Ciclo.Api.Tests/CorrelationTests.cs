using Ciclo.Api.Filters;
using Ciclo.Api.Middleware;
using Ciclo.Core.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 130 — correlação resolvida na borda e propagada ao ProblemDetails.
///
/// O valor tem de ser o mesmo nos três lugares (cabeçalho, Items e corpo de erro). Testar cada
/// um isoladamente não provaria a identidade; por isso o teste usa o mesmo request.
/// </summary>
public sealed class CorrelationTests
{
    private static Task Next(HttpContext _) => Task.CompletedTask;

    [Fact]
    public async Task ValidHeader_IsPreservedInItemsAndResponse()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[AuditCorrelation.HeaderName] = "abc-1234";

        await new CorrelationMiddleware(Next).InvokeAsync(context);

        Assert.Equal("abc-1234", context.Items[AuditCorrelation.ItemsKey]);
        Assert.Equal("abc-1234", context.Response.Headers[AuditCorrelation.HeaderName].ToString());
    }

    [Fact]
    public async Task MissingHeader_UsesTraceIdentifier()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-xyz" };

        await new CorrelationMiddleware(Next).InvokeAsync(context);

        Assert.Equal("trace-xyz", context.Items[AuditCorrelation.ItemsKey]);
        Assert.Equal("trace-xyz", context.Response.Headers[AuditCorrelation.HeaderName].ToString());
    }

    [Fact]
    public async Task OversizedHeader_IsDiscardedAndReplacedByTraceIdentifier()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-fallback" };
        context.Request.Headers[AuditCorrelation.HeaderName] = new string('a', 200);

        await new CorrelationMiddleware(Next).InvokeAsync(context);

        Assert.Equal("trace-fallback", context.Items[AuditCorrelation.ItemsKey]);
    }

    [Fact]
    public void Filter_AddsCorrelationIdToProblemDetails()
    {
        var context = new DefaultHttpContext();
        context.Items[AuditCorrelation.ItemsKey] = "corr-1";
        var problem = new ProblemDetails { Status = 400 };

        new ProblemDetailsCorrelationFilter().OnActionExecuted(Executed(context, problem));

        Assert.Equal("corr-1", problem.Extensions[ProblemResponse.CorrelationExtension]);
    }

    [Fact]
    public void Filter_WithoutCorrelation_DoesNotAddTheExtension()
    {
        var problem = new ProblemDetails { Status = 400 };

        new ProblemDetailsCorrelationFilter()
            .OnActionExecuted(Executed(new DefaultHttpContext(), problem));

        Assert.False(problem.Extensions.ContainsKey(ProblemResponse.CorrelationExtension));
    }

    [Fact]
    public async Task WriteAsync_IncludesCorrelationIdInTheBody()
    {
        var context = new DefaultHttpContext();
        context.Items[AuditCorrelation.ItemsKey] = "corr-write";
        context.Response.Body = new MemoryStream();

        await ProblemResponse.WriteAsync(context, 401, "unauthorized", "unauthorized");

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.Equal(401, context.Response.StatusCode);
        Assert.Contains(ProblemResponse.CorrelationExtension, body);
        Assert.Contains("corr-write", body);
    }

    private static ActionExecutedContext Executed(HttpContext http, ProblemDetails problem)
    {
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());

        return new ActionExecutedContext(
            actionContext, Array.Empty<IFilterMetadata>(), controller: new object())
        {
            Result = new ObjectResult(problem)
        };
    }
}
