using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Security;
using Ciclo.Infrastructure.Services;
using Ciclo.Api.Middleware;

namespace Ciclo.Api.Controllers;

/// <summary>
/// Spec 210 — superfície HTTP de privacidade e LGPD. Nenhuma rota é anônima: o atributo de
/// classe fecha a porta e cada ação declara o papel permitido pela <see cref="AccessPolicy"/>.
/// Erros de domínio são traduzidos pelo mesmo tradutor usado pelos demais controllers.
/// </summary>
/// <remarks>
/// Rotas publicadas (prefixo de classe <c>api/privacy</c>):
/// <list type="bullet">
/// <item><description>POST /api/privacy/requests — abre a solicitação (Admin, Staff, Parent).</description></item>
/// <item><description>GET /api/privacy/requests — lista as solicitações visíveis ao chamador.</description></item>
/// <item><description>POST /api/privacy/requests/{id}/decision — decide (Admin).</description></item>
/// <item><description>POST /api/privacy/requests/{id}/complete — executa (Admin).</description></item>
/// <item><description>GET /api/privacy/students/{studentId}/export — documento do titular (Admin).</description></item>
/// <item><description>POST /api/privacy/consents — concede consentimento (Admin, Staff).</description></item>
/// <item><description>GET /api/privacy/consents — lista consentimentos do titular.</description></item>
/// <item><description>POST /api/privacy/consents/{id}/revoke — revoga consentimento (Admin, Staff).</description></item>
/// </list>
/// O template declara a restrição <c>:guid</c> em cada parâmetro de rota, como nos demais
/// controllers; um id malformado é recusado pelo binding antes de chegar ao serviço.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/privacy")]
public class PrivacyController : ControllerBase
{
    private readonly IConsentService _consents;
    private readonly IPrivacyRequestService _requests;
    private readonly IPrivacyExportService _export;

    public PrivacyController(IConsentService consents, IPrivacyRequestService requests,
        IPrivacyExportService export)
    {
        _consents = consents;
        _requests = requests;
        _export = export;
    }

    /// <summary>FR-005: o titular ou o responsável abre uma solicitação.</summary>
    [HttpPost("requests")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyRead)]
    public async Task<IActionResult> Submit([FromBody] CreatePrivacyRequestRequest request, CancellationToken ct)
    {
        try
        {
            var (userId, role) = GetUserInfo();
            var tenantId = GetTenantId();
            var result = await _requests.SubmitAsync(tenantId, userId, role, request, ct);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (PrivacyException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-005: lista as solicitações visíveis para o chamador, dentro do tenant.</summary>
    [HttpGet("requests")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        try
        {
            var (userId, role) = GetUserInfo();
            var tenantId = GetTenantId();
            var result = await _requests.ListAsync(tenantId, userId, role, ct);
            return Ok(result);
        }
        catch (PrivacyException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-005: decisão sobre uma solicitação submetida (Admin).</summary>
    [HttpPost("requests/{id:guid}/decision")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyManage)]
    public async Task<IActionResult> Decide(Guid id, [FromBody] DecidePrivacyRequestRequest decision,
        CancellationToken ct)
    {
        try
        {
            var tenantId = GetTenantId();
            var result = await _requests.DecideAsync(tenantId, id, decision, ct);
            return Ok(result);
        }
        catch (PrivacyException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-005: execução de uma solicitação aprovada (Admin).</summary>
    [HttpPost("requests/{id:guid}/complete")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyManage)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        try
        {
            var tenantId = GetTenantId();
            var result = await _requests.CompleteAsync(tenantId, id, ct);
            return Ok(result);
        }
        catch (PrivacyException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-002: documento do titular, entregue como arquivo JSON (Admin).</summary>
    [HttpGet("students/{studentId:guid}/export")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyManage)]
    public async Task<IActionResult> Export(Guid studentId, CancellationToken ct)
    {
        try
        {
            var tenantId = GetTenantId();
            var document = await _requests.ExportAsync(tenantId, studentId, ct);

            // Documento montado em memória: nenhum arquivo temporário e nenhum artefato parcial.
            var stream = _export.Serialize(document);
            return File(stream, "application/json", $"privacy-export-{studentId}.json");
        }
        catch (PrivacyException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-001: registra o consentimento de um titular para uma finalidade.</summary>
    [HttpPost("consents")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyConsentWrite)]
    public async Task<IActionResult> GrantConsent([FromBody] GrantConsentRequest request, CancellationToken ct)
    {
        var subjectType = ParseSubjectType(request.SubjectType);
        if (subjectType is null)
            return ProblemResponse.FromCode("invalid_request", 400);

        try
        {
            var (userId, _) = GetUserInfo();
            var tenantId = GetTenantId();
            var result = await _consents.GrantAsync(
                tenantId, subjectType.Value, request.SubjectId, request.Purpose, request.LegalBasis, userId, ct);
            return Ok(result);
        }
        catch (PrivacyException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-001: consentimentos de um titular (responsável apenas dos próprios filhos).</summary>
    [HttpGet("consents")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyRead)]
    public async Task<IActionResult> ListConsents([FromQuery] Guid subjectId, [FromQuery] string subjectType,
        CancellationToken ct)
    {
        var parsed = ParseSubjectType(subjectType);
        if (parsed is null)
            return ProblemResponse.FromCode("invalid_request", 400);

        try
        {
            var (userId, role) = GetUserInfo();
            var tenantId = GetTenantId();

            if (parsed.Value == ConsentSubjectType.Student)
            {
                // A leitura de consentimento do aluno obedece ao vínculo: Admin/Staff leem o tenant,
                // o responsável lê apenas os alunos vinculados a ele.
                await _requests.EnsureCanReadStudentAsync(tenantId, userId, role, subjectId, ct);
            }
            else if (!AccessPolicy.Allows(AccessPolicy.Roles.PrivacyConsentWrite, role))
            {
                // Consentimento de outro responsável não é do escopo de um responsável.
                return ProblemResponse.FromCode("forbidden", 403);
            }

            var result = await _consents.ListBySubjectAsync(tenantId, parsed.Value, subjectId, ct);
            return Ok(result);
        }
        catch (PrivacyException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>FR-001: revoga um consentimento. Revogar o que já está revogado é no-op.</summary>
    [HttpPost("consents/{id:guid}/revoke")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyConsentWrite)]
    public async Task<IActionResult> RevokeConsent(Guid id, CancellationToken ct)
    {
        try
        {
            var tenantId = GetTenantId();
            var result = await _consents.RevokeAsync(tenantId, id, ct);

            return result is null
                ? ProblemResponse.FromCode("not_found", 404)
                : Ok(result);
        }
        catch (PrivacyException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    private static ConsentSubjectType? ParseSubjectType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Enum.TryParse<ConsentSubjectType>(value.Trim(), ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            return null;
        }

        return parsed;
    }

    private (Guid userId, UserRole role) GetUserInfo()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            throw new PrivacyException("user_not_authenticated", 401);

        var role = User.IsInRole("Admin") ? UserRole.Admin
            : User.IsInRole("Staff") ? UserRole.Staff
            : UserRole.Parent;

        return (userId, role);
    }

    private Guid GetTenantId()
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            throw new PrivacyException("tenant_not_resolved", 401);

        return tenantId;
    }
}
