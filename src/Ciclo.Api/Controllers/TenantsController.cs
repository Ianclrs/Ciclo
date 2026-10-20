using Ciclo.Api.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Services;

namespace Ciclo.Api.Controllers;

[ApiController]
[Route("api/tenants")]
public class TenantsController : ControllerBase
{
    private readonly ITenantService _tenantService;

    public TenantsController(ITenantService tenantService)
    {
        _tenantService = tenantService;
    }

    /// <summary>Registra uma nova escola (tenant) e o usuário administrador.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.SchoolRegistration)]
    public async Task<IActionResult> Register([FromBody] RegisterSchoolRequest request)
    {
        try
        {
            var result = await _tenantService.RegisterSchoolAsync(request);
            return Created(string.Empty, result);
        }
        catch (AuthException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }
}
