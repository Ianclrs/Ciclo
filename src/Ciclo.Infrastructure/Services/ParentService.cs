using Microsoft.EntityFrameworkCore;
using Ciclo.Core.Entities;
using Ciclo.Core.Exceptions;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;

namespace Ciclo.Infrastructure.Services;

public interface IParentService
{
    Task<ParentDashboardDto> GetDashboardAsync(Guid parentId, Guid tenantId);
    Task<List<StudentDto>> GetChildrenAsync(Guid parentId, Guid tenantId);
    Task<ChildDetailDto> GetChildDetailAsync(Guid parentId, Guid studentId, Guid tenantId);
    Task<List<DocumentDto>> GetChildDocumentsAsync(Guid parentId, Guid studentId, Guid tenantId);
    Task<DocumentDto> UploadDocumentAsync(Guid parentId, Guid studentId, Stream fileStream, string fileName, string contentType, Guid documentTypeId, Guid tenantId);
    Task<List<GradeDto>> GetChildGradesAsync(Guid parentId, Guid studentId, Guid tenantId);
    Task<ReportCardDto> GetChildReportCardAsync(Guid parentId, Guid studentId, Guid tenantId, CancellationToken cancellationToken = default);
}

public class ParentService : IParentService
{
    private readonly AppDbContext _db;
    private readonly IDocumentService _documentService;

    public ParentService(AppDbContext db, IDocumentService documentService)
    {
        _db = db;
        _documentService = documentService;
    }

    // ── FR-001: Dashboard ────────────────────────────────────────────

    public async Task<ParentDashboardDto> GetDashboardAsync(Guid parentId, Guid tenantId)
    {
        var linkedStudentIds = await _db.StudentParents
            .Where(sp => sp.ParentId == parentId)
            .Select(sp => sp.StudentId)
            .ToListAsync();

        if (linkedStudentIds.Count == 0)
            return new ParentDashboardDto(0, 0, 0, 0, []);

        // Uma única consulta para todos os filhos (M2). O laço anterior fazia duas
        // consultas POR FILHO — o custo crescia com o número de filhos do responsável,
        // e o dashboard era o único lugar com esse padrão.
        var children = await _db.Students
            .Where(s => linkedStudentIds.Contains(s.Id))
            .Select(s => new ChildSummaryDto(
                s.Id,
                s.Nome,
                s.Turma,
                s.AnoLetivo,
                _db.Enrollments
                    .Where(e => e.StudentId == s.Id && e.Status == EnrollmentStatus.Aprovado)
                    .Select(e => (string?)e.Status.ToString())
                    .FirstOrDefault(),
                _db.Documents.Count(d => d.StudentId == s.Id && d.Status == DocumentStatus.Pendente),
                s.Foto))
            .ToListAsync();

        var pendingDocs = await _db.Documents
            .CountAsync(d => linkedStudentIds.Contains(d.StudentId) && d.Status == DocumentStatus.Pendente);

        var activeEnrollments = await _db.Enrollments
            .CountAsync(e => linkedStudentIds.Contains(e.StudentId) && e.Status == EnrollmentStatus.Aprovado);

        var unreadNotifications = await _db.UserNotifications
            .CountAsync(un => un.UserId == parentId && !un.IsRead);

        return new ParentDashboardDto(
            children.Count, unreadNotifications, pendingDocs, activeEnrollments, children);
    }

    // ── FR-002: List Children ────────────────────────────────────────

    public async Task<List<StudentDto>> GetChildrenAsync(Guid parentId, Guid tenantId)
    {
        var linkedIds = await _db.StudentParents
            .Where(sp => sp.ParentId == parentId)
            .Select(sp => sp.StudentId)
            .ToListAsync();

        if (linkedIds.Count == 0)
            return [];

        return await _db.Students
            .Where(s => linkedIds.Contains(s.Id))
            .Select(s => new StudentDto(
                s.Id, s.Nome, s.DataNascimento, s.Cpf, s.Turma, s.AnoLetivo,
                s.Status.ToString(), s.Observacoes, s.Foto, s.CreatedAt, new List<ParentLinkDto>()))
            .ToListAsync();
    }

    // ── FR-003: Child Detail ─────────────────────────────────────────

    public async Task<ChildDetailDto> GetChildDetailAsync(Guid parentId, Guid studentId, Guid tenantId)
    {
        await VerifyParentChildLinkAsync(parentId, studentId);

        var student = await _db.Students
            .Where(s => s.Id == studentId)
            .Select(s => new StudentDto(
                s.Id, s.Nome, s.DataNascimento, s.Cpf, s.Turma, s.AnoLetivo,
                s.Status.ToString(), s.Observacoes, s.Foto, s.CreatedAt, new List<ParentLinkDto>()))
            .FirstOrDefaultAsync();

        if (student is null)
            throw new AccessDeniedException("not_linked_to_student");

        var documents = await _db.Documents
            .Where(d => d.StudentId == studentId)
            .Include(d => d.DocumentType)
            .Include(d => d.Student)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DocumentDto(
                d.Id, d.StudentId, d.Student.Nome, d.DocumentTypeId, d.DocumentType.Nome,
                d.NomeArquivo, d.Status.ToString(), d.DataValidade, d.MotivoRejeicao, d.CreatedAt))
            .ToListAsync();

        var currentEnrollment = await _db.Enrollments
            .Where(e => e.StudentId == studentId && e.Status == EnrollmentStatus.Aprovado)
            .Include(e => e.Period)
            .Include(e => e.Student)
            .Select(e => new EnrollmentDto(
                e.Id, e.StudentId, e.Student.Nome,
                e.EnrollmentPeriodId, e.Period.Nome, e.Status.ToString(),
                e.MotivoRejeicao, e.CreatedAt, e.ApprovedAt))
            .FirstOrDefaultAsync();

        return new ChildDetailDto(
            student, documents, currentEnrollment, ToGradeDtos(await LoadReportCardAsync(studentId, CancellationToken.None)));
    }

    // ── FR-004: Child Documents ──────────────────────────────────────

    public async Task<List<DocumentDto>> GetChildDocumentsAsync(Guid parentId, Guid studentId, Guid tenantId)
    {
        await VerifyParentChildLinkAsync(parentId, studentId);

        return await _db.Documents
            .Where(d => d.StudentId == studentId)
            .Include(d => d.DocumentType)
            .Include(d => d.Student)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DocumentDto(
                d.Id, d.StudentId, d.Student.Nome, d.DocumentTypeId, d.DocumentType.Nome,
                d.NomeArquivo, d.Status.ToString(), d.DataValidade, d.MotivoRejeicao, d.CreatedAt))
            .ToListAsync();
    }

    // ── FR-005: Upload Document ──────────────────────────────────────

    public async Task<DocumentDto> UploadDocumentAsync(Guid parentId, Guid studentId,
        Stream fileStream, string fileName, string contentType, Guid documentTypeId, Guid tenantId)
    {
        await VerifyParentChildLinkAsync(parentId, studentId);

        return await _documentService.UploadAsync(
            fileStream, fileName, contentType, studentId, documentTypeId, tenantId, parentId, UserRole.Parent);
    }

    // ── FR-006 / FR-007: Grades and report card ──────────────────────
    //
    // O vínculo responsável-aluno é a única porta: nenhuma leitura de nota acontece sem
    // VerifyParentChildLinkAsync, e a turma vem do vínculo por referência (SchoolClassId),
    // nunca da string espelho.

    public async Task<List<GradeDto>> GetChildGradesAsync(Guid parentId, Guid studentId, Guid tenantId)
    {
        await VerifyParentChildLinkAsync(parentId, studentId);

        return ToGradeDtos(await LoadReportCardAsync(studentId, CancellationToken.None));
    }

    public async Task<ReportCardDto> GetChildReportCardAsync(
        Guid parentId, Guid studentId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await VerifyParentChildLinkAsync(parentId, studentId);

        return await LoadReportCardAsync(studentId, cancellationToken);
    }

    /// <summary>
    /// Monta o boletim do aluno a partir da turma vinculada. Sem turma não existe avaliação a
    /// ler: devolve boletim vazio com turma e ano nulos, sem lançar. As notas ausentes entram
    /// como <c>null</c> na avaliação e não pesam na média — o componente sem nenhuma nota sai
    /// como <c>sem_nota</c>.
    /// </summary>
    private async Task<ReportCardDto> LoadReportCardAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var student = await _db.Students
            .Where(s => s.Id == studentId)
            .Select(s => new { s.Id, s.Turma, s.AnoLetivo, s.SchoolClassId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new AccessDeniedException("not_linked_to_student");

        if (student.SchoolClassId is null)
            return new ReportCardDto(student.Id, null, null, []);

        var assessments = await _db.Assessments
            .Where(a => a.SchoolClassId == student.SchoolClassId)
            .Select(a => new AssessmentRecord(
                a.Id, a.Title, a.Weight, a.Date, a.IsRecovery, a.CurriculumComponentId, a.CurriculumComponent.Name))
            .ToListAsync(cancellationToken);

        if (assessments.Count == 0)
            return new ReportCardDto(student.Id, student.Turma, student.AnoLetivo, []);

        var gradeByAssessment = await _db.Grades
            .Where(g => g.StudentId == studentId)
            .Select(g => new { g.AssessmentId, g.Value })
            .ToDictionaryAsync(g => g.AssessmentId, g => g.Value, cancellationToken);

        var entries = assessments
            .GroupBy(a => new { a.CurriculumComponentId, a.Componente })
            .OrderBy(group => group.Key.Componente, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                // Só avaliação com nota participa da média: nota não lançada não é zero.
                var scores = group
                    .Where(a => gradeByAssessment.ContainsKey(a.Id))
                    .Select(a => new AssessmentScore(gradeByAssessment[a.Id], a.Weight, a.IsRecovery))
                    .ToList();

                var regularAverage = GradeCalculation.ComputeRegularAverage(scores);

                // A média de recuperação usa o mesmo cálculo do dono, com a marca de recuperação
                // removida — duplicar a fórmula de média ponderada aqui seria um segundo dono.
                var recoveryAverage = GradeCalculation.ComputeRegularAverage(
                    scores.Where(score => score.IsRecovery).Select(score => score with { IsRecovery = false }));

                var finalAverage = GradeCalculation.ComputeFinalAverage(regularAverage, recoveryAverage);

                return new ReportCardEntryDto(
                    group.Key.Componente,
                    regularAverage,
                    recoveryAverage,
                    finalAverage,
                    GradeCalculation.ResolveSituation(finalAverage),
                    group
                        .OrderBy(a => a.Date)
                        .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                        .Select(a => new ReportCardScoreDto(
                            a.Title,
                            a.Weight,
                            gradeByAssessment.TryGetValue(a.Id, out var grade) ? grade : null,
                            a.Date,
                            a.IsRecovery))
                        .ToList());
            })
            .ToList();

        return new ReportCardDto(student.Id, student.Turma, student.AnoLetivo, entries);
    }

    /// <summary>Resumo por componente do boletim — o mesmo shape que a aba de notas já consome.</summary>
    private static List<GradeDto> ToGradeDtos(ReportCardDto reportCard) =>
        reportCard.Componentes
            .Select(entry => new GradeDto(entry.Componente, entry.MediaFinal, null))
            .ToList();

    /// <summary>Projeção da avaliação com o nome do componente, para agrupar sem segunda consulta.</summary>
    private sealed record AssessmentRecord(
        Guid Id,
        string Title,
        decimal Weight,
        DateOnly Date,
        bool IsRecovery,
        Guid CurriculumComponentId,
        string Componente);

    // ── FR-007: Access Control ───────────────────────────────────────

    /// <summary>
    /// B9: a regra vive em <see cref="StudentAccess"/>; este wrapper só fixa o papel do controller
    /// (Parent) e evita repetir a política em cada chamador.
    /// </summary>
    private Task VerifyParentChildLinkAsync(Guid parentId, Guid studentId) =>
        StudentAccess.EnsureLinkedAsync(_db, studentId, parentId, UserRole.Parent);
}
