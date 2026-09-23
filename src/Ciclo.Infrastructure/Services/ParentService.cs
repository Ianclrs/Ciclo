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

        var students = await _db.Students
            .Where(s => linkedStudentIds.Contains(s.Id))
            .ToListAsync();

        var pendingDocs = await _db.Documents
            .CountAsync(d => linkedStudentIds.Contains(d.StudentId) && d.Status == DocumentStatus.Pendente);

        var activeEnrollments = await _db.Enrollments
            .CountAsync(e => linkedStudentIds.Contains(e.StudentId) && e.Status == EnrollmentStatus.Aprovado);

        var unreadNotifications = await _db.UserNotifications
            .CountAsync(un => un.UserId == parentId && !un.IsRead);

        var children = new List<ChildSummaryDto>();
        foreach (var s in students)
        {
            var enrollmentStatus = await _db.Enrollments
                .Where(e => e.StudentId == s.Id && e.Status == EnrollmentStatus.Aprovado)
                .Select(e => (string?)e.Status.ToString())
                .FirstOrDefaultAsync();

            var childPendingDocs = await _db.Documents
                .CountAsync(d => d.StudentId == s.Id && d.Status == DocumentStatus.Pendente);

            children.Add(new ChildSummaryDto(s.Id, s.Nome, s.Turma, s.AnoLetivo, enrollmentStatus, childPendingDocs, s.Foto));
        }

        return new ParentDashboardDto(
            students.Count, unreadNotifications, pendingDocs, activeEnrollments, children);
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
            throw new ForbiddenException("You are not linked to this student.");

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

        return new ChildDetailDto(student, documents, currentEnrollment, []);
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

    // ── FR-006: Grades (placeholder) ─────────────────────────────────

    public async Task<List<GradeDto>> GetChildGradesAsync(Guid parentId, Guid studentId, Guid tenantId)
    {
        await VerifyParentChildLinkAsync(parentId, studentId);
        return [];
    }

    // ── FR-007: Access Control ───────────────────────────────────────

    private async Task VerifyParentChildLinkAsync(Guid parentId, Guid studentId)
    {
        var linked = await _db.StudentParents
            .AnyAsync(sp => sp.StudentId == studentId && sp.ParentId == parentId);

        if (!linked)
            throw new ForbiddenException("You are not linked to this student.");

        // Verify student exists in this tenant (via global query filter on Students)
        var studentExists = await _db.Students.AnyAsync(s => s.Id == studentId);
        if (!studentExists)
            throw new ForbiddenException("You are not linked to this student.");
    }
}
