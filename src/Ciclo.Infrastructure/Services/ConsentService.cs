using Microsoft.EntityFrameworkCore;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;

namespace Ciclo.Infrastructure.Services;

/// <summary>
/// Spec 210 — consentimento por titular e finalidade. O estado muda somente por aqui, e cada
/// concessão ou revogação efetiva grava exatamente uma entrada na trilha da spec 110.
/// </summary>
public interface IConsentService
{
    Task<ConsentDto> GrantAsync(Guid tenantId, ConsentSubjectType subjectType, Guid subjectId,
        string purpose, string legalBasis, Guid? grantedByUserId, CancellationToken ct = default);

    Task<ConsentDto?> RevokeAsync(Guid tenantId, Guid consentId, CancellationToken ct = default);

    Task<IReadOnlyList<ConsentDto>> ListBySubjectAsync(Guid tenantId, ConsentSubjectType subjectType,
        Guid subjectId, CancellationToken ct = default);
}

public class ConsentService : IConsentService
{
    private readonly AppDbContext _db;
    private readonly IAuditTrail _audit;

    public ConsentService(AppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Uma linha por (TenantId, SubjectType, SubjectId, Purpose): conceder de novo atualiza a
    /// linha existente, preservando `Id` e `GrantedAt`, em vez de inserir uma segunda prova.
    /// </summary>
    public async Task<ConsentDto> GrantAsync(Guid tenantId, ConsentSubjectType subjectType, Guid subjectId,
        string purpose, string legalBasis, Guid? grantedByUserId, CancellationToken ct = default)
    {
        var normalizedPurpose = (purpose ?? string.Empty).Trim();
        var normalizedLegalBasis = (legalBasis ?? string.Empty).Trim();

        // Finalidade ou base legal vazia não é consentimento: recusa antes de qualquer escrita.
        if (normalizedPurpose.Length == 0 || normalizedLegalBasis.Length == 0
            || !Enum.IsDefined(subjectType))
            throw new PrivacyException("invalid_request", 400);

        await EnsureSubjectExistsAsync(tenantId, subjectType, subjectId, ct);

        var record = await _db.ConsentRecords.FirstOrDefaultAsync(
            c => c.TenantId == tenantId
                && c.SubjectType == subjectType
                && c.SubjectId == subjectId
                && c.Purpose == normalizedPurpose,
            ct);

        if (record is null)
        {
            record = new ConsentRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SubjectType = subjectType,
                SubjectId = subjectId,
                Purpose = normalizedPurpose,
                LegalBasis = normalizedLegalBasis,
                Status = ConsentStatus.Granted,
                GrantedAt = DateTime.UtcNow,
                GrantedByUserId = grantedByUserId,
                CreatedAt = DateTime.UtcNow,
            };
            _db.ConsentRecords.Add(record);
        }
        else
        {
            record.LegalBasis = normalizedLegalBasis;
            record.Status = ConsentStatus.Granted;
            record.RevokedAt = null;
            record.GrantedByUserId = grantedByUserId ?? record.GrantedByUserId;
        }

        await _db.SaveChangesAsync(ct);

        var dto = ToDto(record);
        _audit.Record(new AuditEntry(
            AuditActions.ConsentGranted,
            TargetTypeOf(record.SubjectType),
            record.SubjectId,
            null,
            dto));

        return dto;
    }

    /// <summary>
    /// Revogar não apaga a concessão: `GrantedAt` e `LegalBasis` permanecem, e só `Status` e
    /// `RevokedAt` avançam. Revogar o que já está revogado é no-op — sem gravação e sem segunda
    /// entrada de auditoria.
    /// </summary>
    public async Task<ConsentDto?> RevokeAsync(Guid tenantId, Guid consentId, CancellationToken ct = default)
    {
        var record = await _db.ConsentRecords
            .FirstOrDefaultAsync(c => c.Id == consentId && c.TenantId == tenantId, ct);

        if (record is null)
            return null;

        if (record.Status == ConsentStatus.Revoked)
            return ToDto(record);

        record.Status = ConsentStatus.Revoked;
        record.RevokedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        var dto = ToDto(record);
        _audit.Record(new AuditEntry(
            AuditActions.ConsentRevoked,
            TargetTypeOf(record.SubjectType),
            record.SubjectId,
            null,
            dto));

        return dto;
    }

    public async Task<IReadOnlyList<ConsentDto>> ListBySubjectAsync(Guid tenantId,
        ConsentSubjectType subjectType, Guid subjectId, CancellationToken ct = default)
    {
        var records = await _db.ConsentRecords
            .Where(c => c.TenantId == tenantId
                && c.SubjectType == subjectType
                && c.SubjectId == subjectId)
            .OrderBy(c => c.Purpose)
            .ToListAsync(ct);

        return records.Select(ToDto).ToList();
    }

    /// <summary>O titular precisa existir no mesmo tenant, senão nada é gravado.</summary>
    private async Task EnsureSubjectExistsAsync(Guid tenantId, ConsentSubjectType subjectType,
        Guid subjectId, CancellationToken ct)
    {
        var exists = subjectType switch
        {
            ConsentSubjectType.Student => await _db.Students
                .AnyAsync(s => s.Id == subjectId && s.TenantId == tenantId, ct),
            ConsentSubjectType.Guardian => await _db.Users
                .AnyAsync(u => u.Id == subjectId && u.TenantId == tenantId, ct),
            _ => false,
        };

        if (!exists)
            throw new KeyNotFoundException("subject_not_found");
    }

    private static string TargetTypeOf(ConsentSubjectType subjectType) =>
        subjectType == ConsentSubjectType.Student ? AuditTargetTypes.Student : "Guardian";

    private static ConsentDto ToDto(ConsentRecord record) => new(
        record.Id,
        record.SubjectType.ToString(),
        record.SubjectId,
        record.Purpose,
        record.LegalBasis,
        record.Status.ToString(),
        record.GrantedAt,
        record.RevokedAt);
}
