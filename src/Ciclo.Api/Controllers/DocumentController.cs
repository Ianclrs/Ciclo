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
[Route("api")]
public class DocumentController : ControllerBase
{
    private readonly IDocumentService _service;

    public DocumentController(IDocumentService service)
    {
        _service = service;
    }

    /// <summary>FR-001: Upload a document.</summary>
    [HttpPost("documents/upload")]
    [Authorize(Roles = AccessPolicy.Roles.DocumentUpload)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromForm] Guid studentId,
        [FromForm] Guid documentTypeId)
    {
        try
        {
            if (file is null || file.Length == 0)
                return ProblemResponse.FromCode("file_required", 400);

            var (userId, role) = GetUserInfo();
            var tenantId = GetTenantId();

            await using var stream = file.OpenReadStream();
            var result = await _service.UploadAsync(stream, file.FileName, file.ContentType, studentId, documentTypeId, tenantId, userId, role);
            return Created(string.Empty, result);
        }
        catch (DocumentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-002: List documents for a student.</summary>
    [HttpGet("students/{studentId:guid}/documents")]
    [Authorize(Roles = AccessPolicy.Roles.DocumentRead)]
    public async Task<IActionResult> GetStudentDocuments(
        Guid studentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var (userId, role) = GetUserInfo();
            var tenantId = GetTenantId();
            var result = await _service.GetStudentDocumentsAsync(studentId, tenantId, userId, role, page, pageSize);
            return Ok(result);
        }
        catch (DocumentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-003: Download a document.</summary>
    [HttpGet("documents/{id:guid}/download")]
    [Authorize(Roles = AccessPolicy.Roles.DocumentRead)]
    public async Task<IActionResult> Download(Guid id)
    {
        try
        {
            var (userId, role) = GetUserInfo();
            var tenantId = GetTenantId();
            var (stream, contentType, fileName) = await _service.DownloadAsync(id, tenantId, userId, role);
            return File(stream, contentType, fileName);
        }
        catch (DocumentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-004/FR-005: Verify (approve or reject) a document.</summary>
    [HttpPost("documents/{id:guid}/verify")]
    [Authorize(Roles = AccessPolicy.Roles.DocumentVerify)]
    public async Task<IActionResult> Verify(Guid id, [FromBody] VerifyDocumentRequest request)
    {
        try
        {
            var (_, role) = GetUserInfo();
            var tenantId = GetTenantId();
            await _service.VerifyAsync(id, request, tenantId, role);
            return Ok();
        }
        catch (DocumentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-006: List pending documents.</summary>
    [HttpGet("documents/pending")]
    [Authorize(Roles = AccessPolicy.Roles.DocumentStaffRead)]
    public async Task<IActionResult> GetPending(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var tenantId = GetTenantId();
            var result = await _service.GetPendingAsync(tenantId, page, pageSize);
            return Ok(result);
        }
        catch (DocumentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-007: List documents expiring within N days.</summary>
    [HttpGet("documents/expiring")]
    [Authorize(Roles = AccessPolicy.Roles.DocumentStaffRead)]
    public async Task<IActionResult> GetExpiring([FromQuery] int days = 30)
    {
        try
        {
            var tenantId = GetTenantId();
            var result = await _service.GetExpiringAsync(tenantId, days);
            return Ok(result);
        }
        catch (DocumentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-008: Create document type.</summary>
    [HttpPost("document-types")]
    [Authorize(Roles = AccessPolicy.Roles.DocumentTypeManage)]
    public async Task<IActionResult> CreateDocumentType([FromBody] CreateDocumentTypeRequest request)
    {
        try
        {
            var (_, role) = GetUserInfo();
            var tenantId = GetTenantId();
            var result = await _service.CreateDocumentTypeAsync(request, tenantId, role);
            return Created(string.Empty, result);
        }
        catch (DocumentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-008: List document types.</summary>
    [HttpGet("document-types")]
    [Authorize(Roles = AccessPolicy.Roles.DocumentRead)]
    public async Task<IActionResult> GetDocumentTypes()
    {
        try
        {
            var tenantId = GetTenantId();
            var result = await _service.GetDocumentTypesAsync(tenantId);
            return Ok(result);
        }
        catch (DocumentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-008: Update document type.</summary>
    [HttpPut("document-types/{id:guid}")]
    [Authorize(Roles = AccessPolicy.Roles.DocumentTypeManage)]
    public async Task<IActionResult> UpdateDocumentType(Guid id, [FromBody] CreateDocumentTypeRequest request)
    {
        try
        {
            var (_, role) = GetUserInfo();
            var tenantId = GetTenantId();
            var result = await _service.UpdateDocumentTypeAsync(id, request, tenantId, role);
            return Ok(result);
        }
        catch (DocumentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-008: Soft-delete document type.</summary>
    [HttpDelete("document-types/{id:guid}")]
    [Authorize(Roles = AccessPolicy.Roles.DocumentTypeManage)]
    public async Task<IActionResult> DeleteDocumentType(Guid id)
    {
        try
        {
            var (_, role) = GetUserInfo();
            var tenantId = GetTenantId();
            await _service.DeleteDocumentTypeAsync(id, tenantId, role);
            return Ok();
        }
        catch (DocumentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    private (Guid userId, UserRole role) GetUserInfo()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            throw new DocumentException("user_not_authenticated", 401);

        var role = User.IsInRole("Admin") ? UserRole.Admin
            : User.IsInRole("Staff") ? UserRole.Staff
            : UserRole.Parent;

        return (userId, role);
    }

    private Guid GetTenantId()
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            throw new DocumentException("tenant_not_resolved", 401);
        return tenantId;
    }
}
