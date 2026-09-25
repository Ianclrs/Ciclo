using Microsoft.AspNetCore.Mvc;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Services;

namespace Ciclo.Api.Controllers;

[ApiController]
[Route("tenants")]
public class TenantsController : ControllerBase
{
    private readonly ITenantService _tenantService;

    public TenantsController(ITenantService tenantService)
    {
        _tenantService = tenantService;
    }

    /// <summary>Registra uma nova escola (tenant) e o usuário administrador.</summary>
    [HttpPost("register")]
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
