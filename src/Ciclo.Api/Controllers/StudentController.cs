using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Services;

namespace Ciclo.Api.Controllers;

[ApiController]
[Route("api/students")]
public class StudentController : ControllerBase
{
    private readonly IStudentService _studentService;

    public StudentController(IStudentService studentService)
    {
        _studentService = studentService;
    }

    /// <summary>FR-001: List students with search, filters, and pagination.</summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Staff,Parent")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? turma,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var (userId, role) = GetUserInfo();
            var tenantId = GetTenantId();
            var result = await _studentService.GetAllAsync(search, turma, status, page, pageSize, tenantId, userId, role);
            return Ok(result);
        }
        catch (StudentException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>FR-002: Get student details.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Staff,Parent")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var (userId, role) = GetUserInfo();
            var tenantId = GetTenantId();
            var result = await _studentService.GetByIdAsync(id, tenantId, userId, role);
            return Ok(result);
        }
        catch (StudentException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>FR-003: Create a new student.</summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Create([FromBody] CreateStudentRequest request)
    {
        try
        {
            var tenantId = GetTenantId();
            var result = await _studentService.CreateAsync(request, tenantId);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (StudentException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>FR-004: Update an existing student.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStudentRequest request)
    {
        try
        {
            var tenantId = GetTenantId();
            var result = await _studentService.UpdateAsync(id, request, tenantId);
            return Ok(result);
        }
        catch (StudentException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>FR-005: Soft-delete a student (Admin only).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var tenantId = GetTenantId();
            await _studentService.DeleteAsync(id, tenantId);
            return Ok();
        }
        catch (StudentException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>FR-006: Link a parent to a student.</summary>
    [HttpPost("{id:guid}/link-parent")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> LinkParent(Guid id, [FromBody] LinkParentRequest request)
    {
        try
        {
            var tenantId = GetTenantId();
            await _studentService.LinkParentAsync(id, request.ParentId, tenantId);
            return Created(string.Empty, null);
        }
        catch (StudentException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>FR-007: Unlink a parent from a student.</summary>
    [HttpDelete("{id:guid}/link-parent/{parentId:guid}")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> UnlinkParent(Guid id, Guid parentId)
    {
        try
        {
            var tenantId = GetTenantId();
            await _studentService.UnlinkParentAsync(id, parentId, tenantId);
            return Ok();
        }
        catch (StudentException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>FR-007 (Spec 60): Get enrollment history for a student.</summary>
    [HttpGet("{id:guid}/enrollment-history")]
    [Authorize(Roles = "Admin,Staff,Parent")]
    public async Task<IActionResult> GetEnrollmentHistory(Guid id)
    {
        try
        {
            var (userId, role) = GetUserInfo();
            var tenantId = GetTenantId();
            var result = await _studentService.GetEnrollmentHistoryAsync(id, tenantId, userId, role);
            return Ok(result);
        }
        catch (StudentException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    private (Guid userId, UserRole role) GetUserInfo()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            throw new StudentException("user_not_authenticated", 401);

        var role = User.IsInRole("Admin") ? UserRole.Admin
            : User.IsInRole("Staff") ? UserRole.Staff
            : UserRole.Parent;

        return (userId, role);
    }

    private Guid GetTenantId()
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            throw new StudentException("tenant_not_resolved", 401);

        return tenantId;
    }
}
