using System.Text;
using Ciclo.Api.Middleware;
using Ciclo.Core.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ciclo.Api.Tests;

/// <summary>
/// M3 — o formato de erro tem um dono único. Estes testes travam o que o cliente consome:
/// o <c>code</c> estável e o content type de ProblemDetails.
/// </summary>
public sealed class ProblemResponseTests
{
    [Fact]
    public void FromCode_EmitsProblemDetailsWithStableCode()
    {
        var result = ProblemResponse.FromCode("student_not_found", 404);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objectResult.StatusCode);
        Assert.Contains(ProblemResponse.ContentType, objectResult.ContentTypes);

        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(404, problem.Status);
        Assert.Equal("student_not_found", problem.Extensions[ProblemResponse.CodeExtension]);
        Assert.Equal("student_not_found", problem.Title);
    }

    [Fact]
    public void FromCode_KeepsStatusAndCodeIndependentFromTitleText()
    {
        var problem = (ProblemDetails)((ObjectResult)ProblemResponse.FromCode("forbidden", 403)).Value!;

        Assert.Equal(403, problem.Status);
        Assert.Equal("forbidden", problem.Extensions[ProblemResponse.CodeExtension]);
    }
}
