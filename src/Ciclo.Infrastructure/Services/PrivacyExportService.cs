using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;

namespace Ciclo.Infrastructure.Services;

/// <summary>
/// Spec 210 — documento de exportação do titular. Somente leitura: este serviço não abre
/// transação, não grava nada e não toca o disco. O documento existe apenas em memória.
/// </summary>
public interface IPrivacyExportService
{
    Task<PrivacyExportDto> ExportStudentAsync(Guid tenantId, Guid studentId, CancellationToken ct = default);

    /// <summary>
    /// Serializa o documento em UTF-8 para um <see cref="MemoryStream"/>. É o único lugar que
    /// decide a forma do JSON exportado (nomes em camelCase, ordem das seções, arrays vazios).
    /// </summary>
    MemoryStream Serialize(PrivacyExportDto document);
}

public class PrivacyExportService : IPrivacyExportService
{
    // JsonSerializerDefaults.Web fixa camelCase e leitura case-insensitive: duas execuções
    // sobre os mesmos dados produzem exatamente o mesmo documento.
    private static readonly JsonSerializerOptions ExportJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;

    public PrivacyExportService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Projeta as sete seções invariantes do documento. Seção sem linhas vira lista vazia,
    /// nunca nulo, e a única seção que varia entre duas execuções é `GeneratedAt`.
    /// </summary>
    public async Task<PrivacyExportDto> ExportStudentAsync(Guid tenantId, Guid studentId,
        CancellationToken ct = default)
    {
        // O filtro de tenant vem antes de qualquer materialização: aluno de outra escola não existe aqui.
        var student = await _db.Students
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == studentId && s.TenantId == tenantId, ct);

        if (student is null)
            throw new KeyNotFoundException("student_not_found");

        var guardians = await LoadGuardiansAsync(tenantId, studentId, ct);
        var enrollments = await LoadEnrollmentsAsync(tenantId, studentId, ct);
        var grades = await LoadGradesAsync(tenantId, studentId, ct);
        var documents = await LoadDocumentsAsync(tenantId, studentId, ct);
        var consents = await LoadConsentsAsync(tenantId, studentId, ct);
        var requests = await LoadRequestsAsync(tenantId, studentId, ct);

        return new PrivacyExportDto(
            DateTime.UtcNow,
            ToStudentExport(student),
            guardians,
            enrollments,
            grades,
            documents,
            consents,
            requests);
    }

    /// <inheritdoc />
    public MemoryStream Serialize(PrivacyExportDto document)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, ExportJsonOptions);

        // Documento em memória: nenhum arquivo temporário é criado, então não existe artefato
        // parcial para o chamador receber em caso de falha.
        return new MemoryStream(bytes);
    }

    /// <summary>
    /// Projeção única da linha do aluno. A anonimização reusa esta projeção para devolver o
    /// estado corrente da linha sem duplicar a forma do contrato.
    /// </summary>
    public static StudentExportDto ToStudentExport(Student student) => new(
        student.Id,
        student.Nome,
        student.DataNascimento,
        student.Cpf,
        student.Turma,
        student.AnoLetivo,
        student.Status.ToString(),
        student.Observacoes,
        student.Foto,
        student.CreatedAt,
        student.UpdatedAt,
        student.AnonymizedAt);

    /// <summary>
    /// Responsáveis ligados ao aluno, sem qualquer dado de credencial: `PasswordHash`,
    /// `SecurityStamp`, `ConcurrencyStamp` e tokens de refresh nunca entram no documento.
    /// </summary>
    private async Task<IReadOnlyList<GuardianExportDto>> LoadGuardiansAsync(Guid tenantId,
        Guid studentId, CancellationToken ct)
    {
        var rows = await _db.StudentParents
            .AsNoTracking()
            .Where(sp => sp.StudentId == studentId)
            .Join(
                _db.Users.AsNoTracking().Where(u => u.TenantId == tenantId),
                sp => sp.ParentId,
                u => u.Id,
                (sp, u) => new { u.Id, u.Name, u.Email, u.Role, u.IsActive, u.CreatedAt })
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        return rows
            .Select(x => new GuardianExportDto(
                x.Id,
                x.Name,
                x.Email ?? string.Empty,
                x.Role.ToString(),
                x.IsActive,
                x.CreatedAt))
            .ToList();
    }

    private async Task<IReadOnlyList<EnrollmentExportDto>> LoadEnrollmentsAsync(Guid tenantId,
        Guid studentId, CancellationToken ct)
    {
        var rows = await _db.Enrollments
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.StudentId == studentId)
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);

        return rows
            .Select(e => new EnrollmentExportDto(e.Id, e.Status.ToString(), e.CreatedAt, e.ApprovedAt))
            .ToList();
    }

    private async Task<IReadOnlyList<GradeExportDto>> LoadGradesAsync(Guid tenantId,
        Guid studentId, CancellationToken ct)
    {
        var rows = await _db.Grades
            .AsNoTracking()
            .Where(g => g.TenantId == tenantId && g.StudentId == studentId)
            .Join(
                _db.Assessments.AsNoTracking().Where(a => a.TenantId == tenantId),
                g => g.AssessmentId,
                a => a.Id,
                (g, a) => new { g.Id, g.AssessmentId, a.Title, g.Value, g.RecordedAt })
            .OrderBy(x => x.RecordedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        return rows
            .Select(x => new GradeExportDto(x.Id, x.AssessmentId, x.Title, x.Value, x.RecordedAt))
            .ToList();
    }

    /// <summary>
    /// Somente metadados: nem bytes, nem `CaminhoArquivo`, nem `HashSha256`, para que o
    /// documento não se torne um caminho alternativo de leitura do armazenamento da spec 170.
    /// </summary>
    private async Task<IReadOnlyList<DocumentExportDto>> LoadDocumentsAsync(Guid tenantId,
        Guid studentId, CancellationToken ct)
    {
        var rows = await _db.Documents
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.StudentId == studentId)
            .OrderBy(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .ToListAsync(ct);

        return rows
            .Select(d => new DocumentExportDto(
                d.Id,
                d.NomeArquivo,
                d.ContentType,
                d.TamanhoBytes,
                d.Status.ToString(),
                d.CreatedAt))
            .ToList();
    }

    private async Task<IReadOnlyList<ConsentDto>> LoadConsentsAsync(Guid tenantId, Guid studentId,
        CancellationToken ct)
    {
        var rows = await _db.ConsentRecords
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId
                && c.SubjectType == ConsentSubjectType.Student
                && c.SubjectId == studentId)
            .OrderBy(c => c.Purpose)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);

        return rows
            .Select(c => new ConsentDto(
                c.Id,
                c.SubjectType.ToString(),
                c.SubjectId,
                c.Purpose,
                c.LegalBasis,
                c.Status.ToString(),
                c.GrantedAt,
                c.RevokedAt))
            .ToList();
    }

    private async Task<IReadOnlyList<PrivacyRequestDto>> LoadRequestsAsync(Guid tenantId,
        Guid studentId, CancellationToken ct)
    {
        var rows = await _db.PrivacyRequests
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.SubjectStudentId == studentId)
            .OrderBy(p => p.SubmittedAt)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);

        return rows
            .Select(p => new PrivacyRequestDto(
                p.Id,
                p.Type.ToString(),
                p.Status.ToString(),
                p.SubjectStudentId,
                p.RequestedByUserId,
                p.Reason,
                p.Resolution,
                p.LegalHold,
                p.RetainUntil,
                p.SubmittedAt,
                p.DecidedAt,
                p.CompletedAt))
            .ToList();
    }
}
