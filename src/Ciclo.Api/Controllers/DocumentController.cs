using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Security;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Storage;

using Ciclo.Api.Authorization;
using Ciclo.Api.Middleware;
using Ciclo.Api.RateLimiting;

namespace Ciclo.Api.Controllers;

[ApiController]
[Route("api")]
[RequiresFeature(TenantFeature.Documents)]
public class DocumentController : ControllerBase
{
    private readonly IDocumentService _service;
    private readonly IFileStorage _storage;
    private readonly FileStorageOptions _storageOptions;

    public DocumentController(
        IDocumentService service,
        IFileStorage storage,
        IOptions<FileStorageOptions> storageOptions)
    {
        _service = service;
        _storage = storage;
        _storageOptions = storageOptions.Value;
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

    /// <summary>FR-007 (Spec 170): emite a URL assinada de download, válida por cinco minutos.</summary>
    [HttpGet("documents/{id:guid}/download-url")]
    [Authorize(Roles = AccessPolicy.Roles.DocumentRead)]
    public async Task<IActionResult> GetDownloadUrl(Guid id)
    {
        try
        {
            var (userId, role) = GetUserInfo();
            var tenantId = GetTenantId();
            var result = await _service.GetDownloadUrlAsync(id, tenantId, userId, role);
            return Ok(result);
        }
        catch (DocumentException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>
    /// FR-007 (Spec 170): serve os bytes a partir do token assinado.
    ///
    /// A rota é anônima de propósito: o token É a autorização, e a emissão já exigiu papel e
    /// vínculo com o aluno. O HMAC é conferido em tempo constante sobre o caminho e a expiração,
    /// então um token adulterado não vira leitura de outro arquivo.
    /// </summary>
    [HttpGet("documents/signed/{token}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Download)]
    public async Task<IActionResult> DownloadSigned(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var epoch)
            || !TryDecodeBase64Url(parts[1], out var signature)
            || !TryDecodeBase64Url(parts[2], out var pathBytes))
        {
            return ProblemResponse.FromCode("signed_url_invalid", 403);
        }

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > epoch)
            return ProblemResponse.FromCode("signed_url_expired", 403);

        var filePath = Encoding.UTF8.GetString(pathBytes);

        byte[] expected;
        try
        {
            expected = HMACSHA256.HashData(
                FileStorageKey.Parse(_storageOptions.EncryptionKey),
                Encoding.UTF8.GetBytes($"{filePath}|{epoch}"));
        }
        catch (InvalidOperationException)
        {
            // Sem chave válida nada pode ser verificado: recusa, e não um 500 com detalhe.
            return ProblemResponse.FromCode("signed_url_invalid", 403);
        }

        if (!CryptographicOperations.FixedTimeEquals(signature, expected))
            return ProblemResponse.FromCode("signed_url_invalid", 403);

        try
        {
            var stream = await _storage.OpenReadAsync(filePath);
            return File(stream, DocumentService.MimeForExtension(filePath), Path.GetFileName(filePath));
        }
        catch (UnauthorizedAccessException)
        {
            // Caminho fora da raiz do storage: o token não vale como autorização de leitura.
            return ProblemResponse.FromCode("signed_url_invalid", 403);
        }
        catch (FileNotFoundException)
        {
            return ProblemResponse.FromCode("document_not_found", 404);
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

    /// <summary>
    /// A6b: listagem global com filtros por status, aluno, tipo e nome do aluno.
    /// `pending` e `expiring` continuam existindo porque têm semântica própria (fila de
    /// verificação e janela de vencimento); esta rota é a visão filtrável.
    /// </summary>
    [HttpGet("documents")]
    [Authorize(Roles = AccessPolicy.Roles.DocumentStaffRead)]
    public async Task<IActionResult> GetDocuments(
        [FromQuery] string? status,
        [FromQuery] Guid? studentId,
        [FromQuery] Guid? documentTypeId,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var tenantId = GetTenantId();
            var filter = new DocumentFilter(status, studentId, documentTypeId, search);
            var result = await _service.GetDocumentsAsync(filter, tenantId, page, pageSize);
            return Ok(result);
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

    /// <summary>
    /// Decodifica base64url tolerando o preenchimento ausente. Devolve <c>false</c> em conteúdo
    /// malformado — o chamador transforma isso em <c>signed_url_invalid</c>, nunca em exceção.
    /// </summary>
    private static bool TryDecodeBase64Url(string value, out byte[] decoded)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);

        try
        {
            decoded = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            decoded = [];
            return false;
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
