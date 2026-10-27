using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Security;
using Ciclo.Infrastructure.Services;

using Ciclo.Api.Middleware;

namespace Ciclo.Api.Controllers;

[ApiController]
[Route("api/parent")]
[Authorize(Roles = AccessPolicy.Roles.ParentOnly)]
public class ParentController : ControllerBase
{
    private readonly IParentService _service;

    public ParentController(IParentService service)
    {
        _service = service;
    }

    /// <summary>FR-001: Get parent dashboard with aggregated summary.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var (userId, _) = GetUserInfo();
        var tenantId = GetTenantId();
        var result = await _service.GetDashboardAsync(userId, tenantId);
        return Ok(result);
    }

    /// <summary>FR-002: List children linked to the parent.</summary>
    [HttpGet("children")]
    public async Task<IActionResult> GetChildren()
    {
        var (userId, _) = GetUserInfo();
        var tenantId = GetTenantId();
        var result = await _service.GetChildrenAsync(userId, tenantId);
        return Ok(result);
    }

    /// <summary>FR-003: Get detailed information for a specific child.</summary>
    [HttpGet("children/{id:guid}")]
    public async Task<IActionResult> GetChildDetail(Guid id)
    {
        var (userId, _) = GetUserInfo();
        var tenantId = GetTenantId();
        var result = await _service.GetChildDetailAsync(userId, id, tenantId);
        return Ok(result);
    }

    /// <summary>FR-004: Get documents for a specific child.</summary>
    [HttpGet("children/{id:guid}/documents")]
    public async Task<IActionResult> GetChildDocuments(Guid id)
    {
        var (userId, _) = GetUserInfo();
        var tenantId = GetTenantId();
        var result = await _service.GetChildDocumentsAsync(userId, id, tenantId);
        return Ok(result);
    }

    /// <summary>FR-005: Upload a document for a child.</summary>
    [HttpPost("children/{id:guid}/documents/upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadDocument(
        Guid id, IFormFile file, [FromForm] Guid documentTypeId)
    {
        try
        {
            if (file is null || file.Length == 0)
                return ProblemResponse.FromCode("file_required", 400);

            var (userId, _) = GetUserInfo();
            var tenantId = GetTenantId();
            var result = await _service.UploadDocumentAsync(
                userId, id, file.OpenReadStream(), file.FileName, file.ContentType, documentTypeId, tenantId);
            return Created(string.Empty, result);
        }
        catch (DocumentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-006: Get grades for a child (placeholder).</summary>
    [HttpGet("children/{id:guid}/grades")]
    public async Task<IActionResult> GetChildGrades(Guid id)
    {
        var (userId, _) = GetUserInfo();
        var tenantId = GetTenantId();
        var result = await _service.GetChildGradesAsync(userId, id, tenantId);
        return Ok(result);
    }

    private (Guid userId, UserRole role) GetUserInfo()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            throw new UnauthorizedAccessException("User not authenticated.");

        var role = User.IsInRole("Admin") ? UserRole.Admin
            : User.IsInRole("Staff") ? UserRole.Staff
            : UserRole.Parent;

        return (userId, role);
    }

    private Guid GetTenantId()
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            throw new UnauthorizedAccessException("Tenant not resolved.");

        return tenantId;
    }
}
