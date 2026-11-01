using Microsoft.EntityFrameworkCore;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;

namespace Ciclo.Infrastructure.Services;

/// <summary>
/// Spec 210 — anonimização do aluno. A linha existente é sobrescrita no lugar: `Id`, `TenantId`,
/// `Status`, `AcademicYearId` e `SchoolClassId` permanecem, então matrículas, notas, documentos e
/// vínculos com responsáveis continuam apontando para o mesmo aluno.
/// </summary>
public interface IAnonymizationService
{
    Task<StudentExportDto> AnonymizeStudentAsync(Guid tenantId, Guid studentId, CancellationToken ct = default);
}

public class AnonymizationService : IAnonymizationService
{
    private const string AnonymousName = "Titular anonimizado";

    // Sentinela determinística: duas execuções produzem exatamente o mesmo valor.
    private static readonly DateTime AnonymousBirthDate = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly AppDbContext _db;
    private readonly IAuditTrail _audit;

    public AnonymizationService(AppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Sobrescreve os campos identificadores dentro de uma transação única. Recusa antes de
    /// qualquer atribuição quando há bloqueio legal aberto, e é no-op quando o aluno já está
    /// anonimizado — nesse caso nenhuma gravação e nenhuma entrada de auditoria acontecem.
    /// </summary>
    public async Task<StudentExportDto> AnonymizeStudentAsync(Guid tenantId, Guid studentId,
        CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // O filtro de tenant vem antes de qualquer materialização: aluno de outra escola não existe aqui.
        var student = await _db.Students
            .FirstOrDefaultAsync(s => s.Id == studentId && s.TenantId == tenantId, ct);

        if (student is null)
            throw new KeyNotFoundException("student_not_found");

        // Já anonimizado: devolve o estado corrente, sem segunda gravação e sem segunda auditoria.
        if (student.AnonymizedAt is not null)
            return PrivacyExportService.ToStudentExport(student);

        // Bloqueio legal é avaliado na hora da execução (AD-005), antes de qualquer atribuição,
        // então a recusa deixa a linha exatamente como estava.
        var hasOpenLegalHold = await _db.PrivacyRequests
            .AnyAsync(p => p.TenantId == tenantId
                && p.SubjectStudentId == studentId
                && p.LegalHold
                && p.Status != PrivacyRequestStatus.Rejected
                && p.Status != PrivacyRequestStatus.Completed, ct);

        if (hasOpenLegalHold)
            throw new PrivacyLegalHoldException();

        var anonymizedAt = DateTime.UtcNow;

        student.Nome = AnonymousName;
        student.Cpf = null;
        student.Foto = null;
        student.Observacoes = null;
        student.DataNascimento = AnonymousBirthDate;
        student.AnonymizedAt = anonymizedAt;
        student.UpdatedAt = anonymizedAt;

        // A entrada de auditoria é registrada antes da gravação para que a sobrescrita e a trilha
        // sejam persistidas pelo mesmo SaveChanges dentro da mesma transação (AD-002: nunca o
        // valor original, apenas o marcador).
        _audit.Record(new AuditEntry(
            AuditActions.PrivacyAnonymized,
            AuditTargetTypes.Student,
            studentId,
            null,
            new { identity = "anonymized", anonymizedAt }));

        await _db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        return PrivacyExportService.ToStudentExport(student);
    }
}
