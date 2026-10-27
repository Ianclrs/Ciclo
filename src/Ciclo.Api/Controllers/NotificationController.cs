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
[Route("api/notifications")]
public class NotificationController : ControllerBase
{
    private readonly INotificationService _service;

    public NotificationController(INotificationService service)
    {
        _service = service;
    }

    /// <summary>FR-001: Create notifications for specific users.</summary>
    [HttpPost]
    [Authorize(Roles = AccessPolicy.Roles.NotificationWrite)]
    public async Task<IActionResult> Create([FromBody] CreateNotificationRequest request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (_, role) = GetUserInfo();
            var result = await _service.CreateAsync(request, tenantId, role);
            return Created(string.Empty, result);
        }
        catch (NotificationException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-002: Broadcast notification to all parents in the tenant.</summary>
    [HttpPost("broadcast")]
    [Authorize(Roles = AccessPolicy.Roles.NotificationBroadcast)]
    public async Task<IActionResult> Broadcast([FromBody] BroadcastNotificationRequest request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (_, role) = GetUserInfo();
            var (count, notification) = await _service.BroadcastAsync(request, tenantId, role);
            if (count == 0) return Ok(new { recipientCount = 0 });
            return Created(string.Empty, new { recipientCount = count, notification });
        }
        catch (NotificationException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-003: Notify parents of a specific student.</summary>
    [HttpPost("by-student/{studentId:guid}")]
    [Authorize(Roles = AccessPolicy.Roles.NotificationWrite)]
    public async Task<IActionResult> SendByStudent(Guid studentId, [FromBody] CreateNotificationRequest request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (_, role) = GetUserInfo();
            var count = await _service.SendByStudentAsync(
                studentId, request.Titulo, request.Mensagem, request.Tipo, request.ReferenceId, tenantId, role);
            if (count == 0) return Ok(new { recipientCount = 0 });
            return Created(string.Empty, new { recipientCount = count });
        }
        catch (NotificationException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-004: List notifications for the current user.</summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetForUser(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var (userId, _) = GetUserInfo();
            var result = await _service.GetForUserAsync(userId, unreadOnly, page, pageSize);
            return Ok(result);
        }
        catch (NotificationException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-005: Mark a notification as read.</summary>
    [HttpPut("{id:guid}/read")]
    [Authorize]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        try
        {
            var (userId, _) = GetUserInfo();
            await _service.MarkReadAsync(id, userId);
            return Ok();
        }
        catch (NotificationException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-006: Mark all notifications as read for the current user.</summary>
    [HttpPut("read-all")]
    [Authorize]
    public async Task<IActionResult> MarkAllRead()
    {
        try
        {
            var (userId, _) = GetUserInfo();
            var count = await _service.MarkAllReadAsync(userId);
            return Ok(new { updatedCount = count });
        }
        catch (NotificationException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-007: Get unread notification count for the current user.</summary>
    [HttpGet("unread-count")]
    [Authorize]
    public async Task<IActionResult> GetUnreadCount()
    {
        try
        {
            var (userId, _) = GetUserInfo();
            var count = await _service.GetUnreadCountAsync(userId);
            return Ok(new { count });
        }
        catch (NotificationException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    private (Guid userId, UserRole role) GetUserInfo()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            throw new NotificationException("user_not_authenticated", 401);

        var role = User.IsInRole("Admin") ? UserRole.Admin
            : User.IsInRole("Staff") ? UserRole.Staff
            : UserRole.Parent;

        return (userId, role);
    }

    private Guid GetTenantId()
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            throw new NotificationException("tenant_not_resolved", 401);

        return tenantId;
    }
}
