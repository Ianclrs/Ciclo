using Ciclo.Api.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Security;
using Ciclo.Infrastructure.Services;

using Ciclo.Api.Middleware;

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
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>Identificação do colégio do usuário logado (chip da topbar).</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrent()
    {
        try
        {
            return Ok(await _tenantService.GetCurrentAsync(GetTenantId()));
        }
        catch (AuthException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>Configuração efetiva da escola do usuário logado.</summary>
    [HttpGet("me/configuration")]
    [Authorize]
    public async Task<IActionResult> GetConfiguration()
    {
        try
        {
            return Ok(await _tenantService.GetConfigurationAsync(GetTenantId()));
        }
        catch (AuthException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>Atualização da configuração da escola (somente Admin).</summary>
    [HttpPut("me/configuration")]
    [Authorize(Roles = AccessPolicy.Roles.UserManage)]
    public async Task<IActionResult> UpdateConfiguration([FromBody] UpdateTenantConfigurationRequest request)
    {
        try
        {
            return Ok(await _tenantService.UpdateConfigurationAsync(GetTenantId(), request));
        }
        catch (AuthException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    private Guid GetTenantId()
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            throw new AuthException("tenant_not_resolved", 401);

        return tenantId;
    }
}
