using Microsoft.EntityFrameworkCore;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Security;

namespace Ciclo.Infrastructure.Services;

/// <summary>
/// Spec 210 — máquina de estados da solicitação do titular (Submitted → Approved | Rejected →
/// Completed) e dono da trilha de acesso a dados sensíveis. A execução de cada tipo é delegada
/// ao serviço que já é dono da operação, então o fluxo não duplica projeção de exportação nem
/// sobrescrita de identidade.
/// </summary>
public interface IPrivacyRequestService
{
    Task<PrivacyRequestDto> SubmitAsync(Guid tenantId, Guid requesterUserId, UserRole requesterRole,
        CreatePrivacyRequestRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<PrivacyRequestDto>> ListAsync(Guid tenantId, Guid requesterUserId,
        UserRole requesterRole, CancellationToken ct = default);

    Task<PrivacyRequestDto> DecideAsync(Guid tenantId, Guid requestId,
        DecidePrivacyRequestRequest decision, CancellationToken ct = default);

    Task<PrivacyRequestCompletionDto> CompleteAsync(Guid tenantId, Guid requestId,
        CancellationToken ct = default);

    /// <summary>
    /// Exporta o documento do titular e registra a leitura na trilha. A exportação em si é
    /// somente leitura; o único efeito persistido aqui é a entrada de auditoria.
    /// </summary>
    Task<PrivacyExportDto> ExportAsync(Guid tenantId, Guid studentId, CancellationToken ct = default);

    /// <summary>
    /// Porta de autorização do responsável: equipe da escola enxerga o tenant; o responsável só
    /// enxerga o aluno com o qual tem vínculo em `StudentParents`.
    /// </summary>
    Task EnsureCanReadStudentAsync(Guid tenantId, Guid requesterUserId, UserRole requesterRole,
        Guid studentId, CancellationToken ct = default);
}

public class PrivacyRequestService : IPrivacyRequestService
{
    private const int MaxTextLength = 500;

    private readonly AppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IPrivacyExportService _export;
    private readonly IAnonymizationService _anonymization;
    private readonly IConsentService _consents;

    public PrivacyRequestService(AppDbContext db, IAuditTrail audit, IPrivacyExportService export,
        IAnonymizationService anonymization, IConsentService consents)
    {
        _db = db;
        _audit = audit;
        _export = export;
        _anonymization = anonymization;
        _consents = consents;
    }

    /// <summary>
    /// Abre uma solicitação. O aluno precisa existir no tenant e, para quem não é da equipe da
    /// escola, precisa estar vinculado ao solicitante — qualquer outro aluno responde 403.
    /// </summary>
    public async Task<PrivacyRequestDto> SubmitAsync(Guid tenantId, Guid requesterUserId,
        UserRole requesterRole, CreatePrivacyRequestRequest request, CancellationToken ct = default)
    {
        var type = ParseType(request.Type);

        if (request.StudentId == Guid.Empty)
            throw new PrivacyException("invalid_request", 400);

        await EnsureStudentExistsAsync(tenantId, request.StudentId, ct);
        await EnsureCanReadStudentAsync(tenantId, requesterUserId, requesterRole, request.StudentId, ct);

        var row = new PrivacyRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Type = type,
            Status = PrivacyRequestStatus.Submitted,
            SubjectStudentId = request.StudentId,
            RequestedByUserId = requesterUserId,
            Reason = Normalize(request.Reason),
            LegalHold = false,
            SubmittedAt = DateTime.UtcNow,
        };

        _db.PrivacyRequests.Add(row);
        await _db.SaveChangesAsync(ct);

        return ToDto(row);
    }

    /// <summary>
    /// A equipe da escola lê o tenant inteiro; o responsável lê apenas as solicitações dos
    /// alunos com vínculo — desvincular o aluno remove a solicitação da visão dele.
    /// </summary>
    public async Task<IReadOnlyList<PrivacyRequestDto>> ListAsync(Guid tenantId, Guid requesterUserId,
        UserRole requesterRole, CancellationToken ct = default)
    {
        var query = _db.PrivacyRequests
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId);

        if (!IsSchoolStaff(requesterRole))
        {
            query = query.Where(p => _db.StudentParents
                .Any(sp => sp.StudentId == p.SubjectStudentId && sp.ParentId == requesterUserId));
        }

        var rows = await query
            .OrderByDescending(p => p.SubmittedAt)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    /// <summary>
    /// Toda recusa acontece antes da primeira atribuição: uma decisão inválida não altera a linha
    /// e não grava entrada de auditoria.
    /// </summary>
    public async Task<PrivacyRequestDto> DecideAsync(Guid tenantId, Guid requestId,
        DecidePrivacyRequestRequest decision, CancellationToken ct = default)
    {
        var row = await LoadAsync(tenantId, requestId, ct);

        if (row.Status != PrivacyRequestStatus.Submitted)
            throw new PrivacyException("invalid_state", 409);

        var status = ParseDecision(decision.Decision);
        var resolution = Normalize(decision.Resolution);

        row.Status = status;
        row.Resolution = resolution;
        row.LegalHold = decision.LegalHold;
        row.RetainUntil = decision.RetainUntil;
        row.DecidedAt = DateTime.UtcNow;

        // A trilha é registrada antes da gravação: a decisão e sua entrada são persistidas pelo
        // mesmo SaveChanges, dentro da mesma transação implícita.
        var dto = ToDto(row);
        _audit.Record(new AuditEntry(
            AuditActions.PrivacyRequestDecided,
            AuditTargetTypes.PrivacyRequest,
            row.Id,
            null,
            dto));

        await _db.SaveChangesAsync(ct);

        return dto;
    }

    /// <summary>
    /// Executa uma solicitação aprovada. Bloqueio legal é avaliado na hora da execução (AD-005),
    /// então uma marca acrescentada depois da aprovação ainda recusa a anonimização.
    /// </summary>
    public async Task<PrivacyRequestCompletionDto> CompleteAsync(Guid tenantId, Guid requestId,
        CancellationToken ct = default)
    {
        var row = await LoadAsync(tenantId, requestId, ct);

        if (row.Status != PrivacyRequestStatus.Approved)
            throw new PrivacyException("invalid_state", 409);

        if (row.Type == PrivacyRequestType.Anonymization && row.LegalHold)
            throw new PrivacyLegalHoldException();

        PrivacyExportDto? document = null;

        switch (row.Type)
        {
            case PrivacyRequestType.Export:
                document = await _export.ExportStudentAsync(tenantId, row.SubjectStudentId, ct);
                break;

            case PrivacyRequestType.Anonymization:
                await _anonymization.AnonymizeStudentAsync(tenantId, row.SubjectStudentId, ct);
                break;

            case PrivacyRequestType.ConsentRevocation:
                await RevokeAllConsentsAsync(tenantId, row.SubjectStudentId, ct);
                break;

            default:
                throw new PrivacyException("invalid_request", 400);
        }

        row.Status = PrivacyRequestStatus.Completed;
        row.CompletedAt = DateTime.UtcNow;

        var dto = ToDto(row);
        _audit.Record(new AuditEntry(
            AuditActions.PrivacyRequestDecided,
            AuditTargetTypes.PrivacyRequest,
            row.Id,
            null,
            dto));

        await _db.SaveChangesAsync(ct);

        return new PrivacyRequestCompletionDto(dto, document);
    }

    /// <inheritdoc />
    public async Task<PrivacyExportDto> ExportAsync(Guid tenantId, Guid studentId,
        CancellationToken ct = default)
    {
        var document = await _export.ExportStudentAsync(tenantId, studentId, ct);

        // A leitura de dados sensíveis só existe com a trilha: a falha da auditoria derruba a
        // operação. O payload guarda apenas o marcador temporal, nunca o conteúdo exportado.
        _audit.Record(new AuditEntry(
            AuditActions.PrivacyExported,
            AuditTargetTypes.Student,
            studentId,
            null,
            new { generatedAt = document.GeneratedAt }));

        await _db.SaveChangesAsync(ct);

        return document;
    }

    /// <inheritdoc />
    public async Task EnsureCanReadStudentAsync(Guid tenantId, Guid requesterUserId,
        UserRole requesterRole, Guid studentId, CancellationToken ct = default)
    {
        if (IsSchoolStaff(requesterRole))
            return;

        var linked = await _db.StudentParents
            .AnyAsync(sp => sp.StudentId == studentId && sp.ParentId == requesterUserId, ct);

        // Não revelar se o aluno existe: aluno de outra escola cai aqui como qualquer aluno sem vínculo.
        if (!linked)
            throw new PrivacyException("forbidden", 403);
    }

    /// <summary>Equipe da escola (Admin/Staff) enxerga o tenant; o responsável só os próprios filhos.</summary>
    private static bool IsSchoolStaff(UserRole role) =>
        AccessPolicy.Allows(AccessPolicy.Roles.PrivacyConsentWrite, role);

    private async Task<PrivacyRequest> LoadAsync(Guid tenantId, Guid requestId, CancellationToken ct)
    {
        var row = await _db.PrivacyRequests
            .FirstOrDefaultAsync(p => p.Id == requestId && p.TenantId == tenantId, ct);

        // Linha de outra escola é invisível: 404, nunca 403, para não revelar existência.
        if (row is null)
            throw new KeyNotFoundException("privacy_request_not_found");

        return row;
    }

    private async Task EnsureStudentExistsAsync(Guid tenantId, Guid studentId, CancellationToken ct)
    {
        var exists = await _db.Students
            .AnyAsync(s => s.Id == studentId && s.TenantId == tenantId, ct);

        if (!exists)
            throw new KeyNotFoundException("student_not_found");
    }

    private async Task RevokeAllConsentsAsync(Guid tenantId, Guid studentId, CancellationToken ct)
    {
        var granted = await _consents.ListBySubjectAsync(tenantId, ConsentSubjectType.Student, studentId, ct);
        var grantedStatus = ConsentStatus.Granted.ToString();

        foreach (var consent in granted.Where(c => c.Status == grantedStatus))
        {
            await _consents.RevokeAsync(tenantId, consent.Id, ct);
        }
    }

    private static PrivacyRequestType ParseType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Enum.TryParse<PrivacyRequestType>(value.Trim(), ignoreCase: true, out var type)
            || !Enum.IsDefined(type))
        {
            throw new PrivacyException("invalid_request", 400);
        }

        return type;
    }

    private static PrivacyRequestStatus ParseDecision(string? value)
    {
        if (string.Equals(value?.Trim(), "approve", StringComparison.OrdinalIgnoreCase))
            return PrivacyRequestStatus.Approved;

        if (string.Equals(value?.Trim(), "reject", StringComparison.OrdinalIgnoreCase))
            return PrivacyRequestStatus.Rejected;

        throw new PrivacyException("invalid_request", 400);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();

        if (trimmed.Length > MaxTextLength)
            throw new PrivacyException("invalid_request", 400);

        return trimmed;
    }

    private static PrivacyRequestDto ToDto(PrivacyRequest row) => new(
        row.Id,
        row.Type.ToString(),
        row.Status.ToString(),
        row.SubjectStudentId,
        row.RequestedByUserId,
        row.Reason,
        row.Resolution,
        row.LegalHold,
        row.RetainUntil,
        row.SubmittedAt,
        row.DecidedAt,
        row.CompletedAt);
}
