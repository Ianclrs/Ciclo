using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ciclo.Api.Middleware;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Security;
using Ciclo.Infrastructure.Services;

namespace Ciclo.Api.Controllers;

/// <summary>
/// M11 — gestão de usuários do colégio. Admin-only (os atributos leem a mesma constante que o
/// serviço usa em <see cref="AccessPolicy"/>).
///
/// O convite cria a conta SEM senha e devolve um token de definição de senha. Em Development o
/// token volta na resposta (mesmo padrão do forgot-password, que viabiliza o fluxo sem servidor
/// de e-mail); em produção ele deve ser entregue por e-mail.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = AccessPolicy.Roles.UserManage)]
public class UsersController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IWebHostEnvironment _environment;

    public UsersController(IAuthService authService, IWebHostEnvironment environment)
    {
        _authService = authService;
        _environment = environment;
    }

    /// <summary>Lista os usuários do colégio.</summary>
    [HttpGet]
    [Authorize(Roles = AccessPolicy.Roles.UserRead)]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var (_, role) = GetActor();
            var tenantId = GetTenantId();
            return Ok(await _authService.ListUsersAsync(tenantId, role));
        }
        catch (AuthException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>Convida um Admin ou Staff. A conta só passa a autenticar após aceitar o convite.</summary>
    [HttpPost("invite")]
    [Authorize(Roles = AccessPolicy.Roles.UserManage)]
    public async Task<IActionResult> Invite([FromBody] InviteUserRequest request)
    {
        try
        {
            var (_, role) = GetActor();
            var tenantId = GetTenantId();
            var (user, inviteToken) = await _authService.InviteUserAsync(request, tenantId, role);

            if (_environment.IsDevelopment())
                return Created(string.Empty, new { user, inviteToken });

            return Created(string.Empty, user);
        }
        catch (AuthException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>Troca o papel de um usuário.</summary>
    [HttpPatch("{id:guid}/role")]
    [Authorize(Roles = AccessPolicy.Roles.UserManage)]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangeUserRoleRequest request)
    {
        try
        {
            var (actorId, role) = GetActor();
            var tenantId = GetTenantId();
            return Ok(await _authService.ChangeRoleAsync(id, request.Role, actorId, tenantId, role));
        }
        catch (AuthException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>Ativa ou desativa um usuário (desativado não autentica).</summary>
    [HttpPatch("{id:guid}/active")]
    [Authorize(Roles = AccessPolicy.Roles.UserManage)]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetUserActiveRequest request)
    {
        try
        {
            var (actorId, role) = GetActor();
            var tenantId = GetTenantId();
            return Ok(await _authService.SetActiveAsync(id, request.IsActive, actorId, tenantId, role));
        }
        catch (AuthException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    private (Guid userId, UserRole role) GetActor()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            throw new AuthException("user_not_authenticated", 401);

        var role = User.IsInRole("Admin") ? UserRole.Admin
            : User.IsInRole("Staff") ? UserRole.Staff
            : UserRole.Parent;

        return (userId, role);
    }

    private Guid GetTenantId()
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            throw new AuthException("tenant_not_resolved", 401);

        return tenantId;
    }
}
