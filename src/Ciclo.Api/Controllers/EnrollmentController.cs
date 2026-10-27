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
[Route("api/enrollment-periods")]
public class EnrollmentPeriodController : ControllerBase
{
    private readonly IEnrollmentService _service;

    public EnrollmentPeriodController(IEnrollmentService service)
    {
        _service = service;
    }

    /// <summary>FR-001: Create an enrollment period.</summary>
    [HttpPost]
    [Authorize(Roles = AccessPolicy.Roles.EnrollmentPeriodManage)]
    public async Task<IActionResult> Create([FromBody] CreateEnrollmentPeriodRequest request)
    {
        try
        {
            var tenantId = GetTenantId();
            var result = await _service.CreatePeriodAsync(request, tenantId, GetRole());
            return Created(string.Empty, result);
        }
        catch (EnrollmentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-002: List enrollment periods.</summary>
    [HttpGet]
    [Authorize(Roles = AccessPolicy.Roles.EnrollmentPeriodRead)]
    public async Task<IActionResult> GetAll([FromQuery] bool includeInactive = false)
    {
        try
        {
            var tenantId = GetTenantId();
            var result = await _service.GetPeriodsAsync(includeInactive, tenantId);
            return Ok(result);
        }
        catch (EnrollmentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-003: Close an enrollment period.</summary>
    [HttpPut("{id:guid}/close")]
    [Authorize(Roles = AccessPolicy.Roles.EnrollmentPeriodManage)]
    public async Task<IActionResult> Close(Guid id)
    {
        try
        {
            var tenantId = GetTenantId();
            await _service.ClosePeriodAsync(id, tenantId, GetRole());
            return Ok();
        }
        catch (EnrollmentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    private Guid GetTenantId()
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            throw new EnrollmentException("tenant_not_resolved", 401);
        return tenantId;
    }

    private UserRole GetRole() =>
        User.IsInRole("Admin") ? UserRole.Admin
        : User.IsInRole("Staff") ? UserRole.Staff
        : UserRole.Parent;
}

[ApiController]
[Route("api/enrollments")]
public class EnrollmentController : ControllerBase
{
    private readonly IEnrollmentService _service;

    public EnrollmentController(IEnrollmentService service)
    {
        _service = service;
    }

    /// <summary>FR-004: Create a new enrollment (draft).</summary>
    [HttpPost]
    [Authorize(Roles = AccessPolicy.Roles.EnrollmentWrite)]
    public async Task<IActionResult> Create([FromBody] CreateEnrollmentRequest request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (_, role) = GetUserInfo();
            var result = await _service.CreateEnrollmentAsync(request, tenantId, role);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (EnrollmentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-005: List enrollments with filters.</summary>
    [HttpGet]
    [Authorize(Roles = AccessPolicy.Roles.EnrollmentRead)]
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
            var result = await _service.GetEnrollmentsAsync(status, periodId, page, pageSize, tenantId, userId, role);
            return Ok(result);
        }
        catch (EnrollmentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-006: Get enrollment details.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = AccessPolicy.Roles.EnrollmentRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var (userId, role) = GetUserInfo();
            var tenantId = GetTenantId();
            var result = await _service.GetEnrollmentByIdAsync(id, tenantId, userId, role);
            return Ok(result);
        }
        catch (EnrollmentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-007: Submit enrollment for approval.</summary>
    [HttpPost("{id:guid}/submit")]
    [Authorize(Roles = AccessPolicy.Roles.EnrollmentWrite)]
    public async Task<IActionResult> Submit(Guid id)
    {
        try
        {
            var tenantId = GetTenantId();
            var (userId, role) = GetUserInfo();
            var result = await _service.SubmitAsync(id, tenantId, userId, role);
            return Ok(result);
        }
        catch (EnrollmentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-008: Approve enrollment.</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = AccessPolicy.Roles.EnrollmentReview)]
    public async Task<IActionResult> Approve(Guid id)
    {
        try
        {
            var tenantId = GetTenantId();
            var (userId, role) = GetUserInfo();
            var result = await _service.ApproveAsync(id, tenantId, userId, role);
            return Ok(result);
        }
        catch (EnrollmentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-009: Reject enrollment.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = AccessPolicy.Roles.EnrollmentReview)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectEnrollmentRequest request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (userId, role) = GetUserInfo();
            await _service.RejectAsync(id, request, tenantId, userId, role);
            return Ok();
        }
        catch (EnrollmentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-010: Cancel enrollment (Admin only).</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = AccessPolicy.Roles.EnrollmentCancel)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        try
        {
            var tenantId = GetTenantId();
            var (userId, role) = GetUserInfo();
            await _service.CancelAsync(id, tenantId, userId, role);
            return Ok();
        }
        catch (EnrollmentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>
    /// M12 - reabre uma matricula em estado terminal (Aprovado, Rejeitado ou Cancelado).
    /// Admin only: reabrir desfaz uma decisao administrativa e fica registrado em
    /// `StatusChangedBy`/`StatusChangedAt`.
    /// </summary>
    [HttpPost("{id:guid}/reopen")]
    [Authorize(Roles = AccessPolicy.Roles.EnrollmentReopen)]
    public async Task<IActionResult> Reopen(Guid id)
    {
        try
        {
            var tenantId = GetTenantId();
            var (userId, role) = GetUserInfo();
            var result = await _service.ReopenAsync(id, tenantId, userId, role);
            return Ok(result);
        }
        catch (EnrollmentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    private (Guid userId, UserRole role) GetUserInfo()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            throw new EnrollmentException("user_not_authenticated", 401);

        var role = User.IsInRole("Admin") ? UserRole.Admin
            : User.IsInRole("Staff") ? UserRole.Staff
            : UserRole.Parent;

        return (userId, role);
    }

    private Guid GetTenantId()
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            throw new EnrollmentException("tenant_not_resolved", 401);
        return tenantId;
    }
}
