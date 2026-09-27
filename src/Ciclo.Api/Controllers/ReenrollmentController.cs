using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Services;

namespace Ciclo.Api.Controllers;

[ApiController]
[Route("api/reenrollments")]
public class ReenrollmentController : ControllerBase
{
    private readonly IReenrollmentService _service;

    public ReenrollmentController(IReenrollmentService service)
    {
        _service = service;
    }

    /// <summary>FR-001: Create a new re-enrollment.</summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Create([FromBody] CreateReenrollmentRequest request)
    {
        try
        {
            var tenantId = GetTenantId();
            var result = await _service.CreateAsync(request, tenantId);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (ReenrollmentException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>FR-002: List re-enrollments with filters.</summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Staff,Parent")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] Guid? periodId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var (userId, role) = GetUserInfo();
            var tenantId = GetTenantId();
            var result = await _service.GetAllAsync(tenantId, status, periodId, page, pageSize, userId, role);
            return Ok(result);
        }
        catch (ReenrollmentException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>FR-003: Get re-enrollment details.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Staff,Parent")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var (userId, role) = GetUserInfo();
            var tenantId = GetTenantId();
            var result = await _service.GetByIdAsync(id, tenantId, userId, role);
            return Ok(result);
        }
        catch (ReenrollmentException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>FR-005: Approve re-enrollment.</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Approve(Guid id)
    {
        try
        {
            var tenantId = GetTenantId();
            var result = await _service.ApproveAsync(id, tenantId);
            return Ok(result);
        }
        catch (ReenrollmentException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>FR-006: Reject re-enrollment.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectEnrollmentRequest request)
    {
        try
        {
            var tenantId = GetTenantId();
            await _service.RejectAsync(id, request.Motivo, tenantId);
            return Ok();
        }
        catch (ReenrollmentException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    private (Guid userId, UserRole role) GetUserInfo()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            throw new ReenrollmentException("user_not_authenticated", 401);

        var role = User.IsInRole("Admin") ? UserRole.Admin
            : User.IsInRole("Staff") ? UserRole.Staff
            : UserRole.Parent;

        return (userId, role);
    }

    private Guid GetTenantId()
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            throw new ReenrollmentException("tenant_not_resolved", 401);
        return tenantId;
    }
}
