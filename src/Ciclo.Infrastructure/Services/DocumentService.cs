using Microsoft.EntityFrameworkCore;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Storage;

namespace Ciclo.Infrastructure.Services;

public interface IDocumentService
{
    // Documents
    Task<DocumentDto> UploadAsync(Stream fileStream, string fileName, string contentType, Guid studentId, Guid documentTypeId, Guid tenantId, Guid userId, UserRole role);
    Task<PagedResponse<DocumentDto>> GetStudentDocumentsAsync(Guid studentId, Guid tenantId, Guid userId, UserRole role, int page, int pageSize);
    Task<(Stream Stream, string ContentType, string FileName)> DownloadAsync(Guid documentId, Guid tenantId, Guid userId, UserRole role);
    Task VerifyAsync(Guid documentId, VerifyDocumentRequest request, Guid tenantId);
    Task<PagedResponse<DocumentDto>> GetPendingAsync(Guid tenantId, int page, int pageSize);
    Task<List<DocumentDto>> GetExpiringAsync(Guid tenantId, int days);

    // Document Types
    Task<DocumentTypeDto> CreateDocumentTypeAsync(CreateDocumentTypeRequest request, Guid tenantId);
    Task<List<DocumentTypeDto>> GetDocumentTypesAsync(Guid tenantId);
    Task<DocumentTypeDto> UpdateDocumentTypeAsync(Guid id, CreateDocumentTypeRequest request, Guid tenantId);
    Task DeleteDocumentTypeAsync(Guid id, Guid tenantId);
}

public class DocumentService : IDocumentService
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly INotificationService _notificationService;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png"
    };

    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    public DocumentService(AppDbContext db, IFileStorage storage, INotificationService notificationService)
    {
        _db = db;
        _storage = storage;
        _notificationService = notificationService;
    }

    // ── Documents ──────────────────────────────────────────────────

    public async Task<DocumentDto> UploadAsync(
        Stream fileStream, string fileName, string contentType,
        Guid studentId, Guid documentTypeId, Guid tenantId, Guid userId, UserRole role)
    {
        // Validate file
        if (fileStream.Length > MaxFileSize)
            throw new DocumentException("file_too_large", 400);

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            throw new DocumentException("invalid_extension", 400);

        // Validate student
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId);
        if (student is null)
            throw new DocumentException("student_not_found", 404);

        // Parent: must be linked
        if (role == UserRole.Parent)
        {
            var isLinked = await _db.StudentParents
                .AnyAsync(sp => sp.StudentId == studentId && sp.ParentId == userId);
            if (!isLinked)
                throw new DocumentException("not_linked_to_student", 403);
        }

        // Validate document type
        var docType = await _db.DocumentTypes
            .FirstOrDefaultAsync(dt => dt.Id == documentTypeId);
        if (docType is null)
            throw new DocumentException("document_type_not_found", 404);
        if (!docType.IsActive)
            throw new DocumentException("document_type_inactive", 400);

        // Save file
        fileStream.Position = 0;
        var filePath = await _storage.SaveAsync(tenantId, studentId, fileName, fileStream);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StudentId = studentId,
            DocumentTypeId = documentTypeId,
            NomeArquivo = fileName,
            CaminhoArquivo = filePath,
            Status = DocumentStatus.Pendente,
            CreatedAt = DateTime.UtcNow
        };

        _db.Documents.Add(document);
        await _db.SaveChangesAsync();

        return MapToDto(document);
    }

    public async Task<PagedResponse<DocumentDto>> GetStudentDocumentsAsync(
        Guid studentId, Guid tenantId, Guid userId, UserRole role, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize > 100) pageSize = 100;

        // Parent: must be linked
        if (role == UserRole.Parent)
        {
            var isLinked = await _db.StudentParents
                .AnyAsync(sp => sp.StudentId == studentId && sp.ParentId == userId);
            if (!isLinked)
                throw new DocumentException("not_linked_to_student", 403);
        }

        var query = _db.Documents
            .Include(d => d.DocumentType)
            .Include(d => d.Student)
            .Where(d => d.StudentId == studentId);

        var total = await query.CountAsync();

        var docs = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DocumentDto(
                d.Id, d.StudentId, d.Student.Nome, d.DocumentTypeId, d.DocumentType.Nome,
                d.NomeArquivo, d.Status.ToString(), d.DataValidade, d.MotivoRejeicao, d.CreatedAt))
            .ToListAsync();

        return new PagedResponse<DocumentDto>(docs, total, page, pageSize);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> DownloadAsync(
        Guid documentId, Guid tenantId, Guid userId, UserRole role)
    {
        var doc = await _db.Documents
            .Include(d => d.Student)
            .ThenInclude(s => s.StudentParents)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (doc is null)
            throw new DocumentException("document_not_found", 404);

        // Parent: must be linked to document's student
        if (role == UserRole.Parent)
        {
            if (!doc.Student.StudentParents.Any(sp => sp.ParentId == userId))
                throw new DocumentException("not_linked_to_student", 403);
        }

        var stream = await GetFileStreamSafeAsync(doc.CaminhoArquivo);
        var ext = Path.GetExtension(doc.NomeArquivo).ToLowerInvariant();
        var contentType = ext switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream"
        };

        return (stream, contentType, doc.NomeArquivo);
    }

    public async Task VerifyAsync(Guid documentId, VerifyDocumentRequest request, Guid tenantId)
    {
        var doc = await _db.Documents
            .Include(d => d.DocumentType)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (doc is null)
            throw new DocumentException("document_not_found", 404);

        if (request.Approved)
        {
            doc.Status = DocumentStatus.Aprovado;
            doc.VerifiedAt = DateTime.UtcNow;
            doc.MotivoRejeicao = null;

            // Calculate validity
            if (doc.DocumentType.ValidadeMeses > 0)
                doc.DataValidade = DateTime.UtcNow.AddMonths(doc.DocumentType.ValidadeMeses);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.MotivoRejeicao) ||
                request.MotivoRejeicao.Length < 10 || request.MotivoRejeicao.Length > 500)
                throw new DocumentException("motivo_required_10_to_500_chars", 400);

            doc.Status = DocumentStatus.Rejeitado;
            doc.MotivoRejeicao = request.MotivoRejeicao;
        }

        // FR-008: Auto-notification on document rejection (before SaveChanges = same transaction)
        if (!request.Approved)
        {
            var titulo = $"Documento rejeitado: {doc.DocumentType.Nome}";
            var mensagem = $"O documento '{doc.NomeArquivo}' foi rejeitado. Motivo: {doc.MotivoRejeicao}";
            await _notificationService.SendByStudentAsync(
                doc.StudentId, titulo, mensagem, NotificationType.DocumentoPendente, doc.Id, tenantId);
        }

        await _db.SaveChangesAsync();
    }

    public async Task<PagedResponse<DocumentDto>> GetPendingAsync(Guid tenantId, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize > 100) pageSize = 100;

        var query = _db.Documents
            .Include(d => d.DocumentType)
            .Include(d => d.Student)
            .Where(d => d.Status == DocumentStatus.Pendente);

        var total = await query.CountAsync();
        var docs = await query
            .OrderBy(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DocumentDto(
                d.Id, d.StudentId, d.Student.Nome, d.DocumentTypeId, d.DocumentType.Nome,
                d.NomeArquivo, d.Status.ToString(), d.DataValidade, d.MotivoRejeicao, d.CreatedAt,
                d.Student.Foto))
            .ToListAsync();

        return new PagedResponse<DocumentDto>(docs, total, page, pageSize);
    }

    public async Task<List<DocumentDto>> GetExpiringAsync(Guid tenantId, int days)
    {
        var cutoff = DateTime.UtcNow.AddDays(days);

        var docs = await _db.Documents
            .Include(d => d.DocumentType)
            .Include(d => d.Student)
            .Where(d => d.Status == DocumentStatus.Aprovado
                && d.DataValidade != null
                && d.DataValidade <= cutoff)
            .OrderBy(d => d.DataValidade)
            .Select(d => new DocumentDto(
                d.Id, d.StudentId, d.Student.Nome, d.DocumentTypeId, d.DocumentType.Nome,
                d.NomeArquivo, d.Status.ToString(), d.DataValidade, d.MotivoRejeicao, d.CreatedAt))
            .ToListAsync();

        return docs;
    }

    // ── Document Types ─────────────────────────────────────────────

    public async Task<DocumentTypeDto> CreateDocumentTypeAsync(CreateDocumentTypeRequest request, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(request.Nome))
            throw new DocumentException("type_name_required", 400);

        var type = new DocumentType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = request.Nome,
            Descricao = request.Descricao,
            IsRequired = request.IsRequired,
            ValidadeMeses = request.ValidadeMeses,
            IsActive = true
        };

        _db.DocumentTypes.Add(type);
        await _db.SaveChangesAsync();

        return new DocumentTypeDto(type.Id, type.Nome, type.Descricao, type.IsRequired, type.ValidadeMeses, type.IsActive);
    }

    public async Task<List<DocumentTypeDto>> GetDocumentTypesAsync(Guid tenantId)
    {
        return await _db.DocumentTypes
            .Select(dt => new DocumentTypeDto(dt.Id, dt.Nome, dt.Descricao, dt.IsRequired, dt.ValidadeMeses, dt.IsActive))
            .ToListAsync();
    }

    public async Task<DocumentTypeDto> UpdateDocumentTypeAsync(Guid id, CreateDocumentTypeRequest request, Guid tenantId)
    {
        var type = await _db.DocumentTypes.FirstOrDefaultAsync(dt => dt.Id == id);
        if (type is null)
            throw new DocumentException("document_type_not_found", 404);

        if (string.IsNullOrWhiteSpace(request.Nome))
            throw new DocumentException("type_name_required", 400);

        type.Nome = request.Nome;
        type.Descricao = request.Descricao;
        type.IsRequired = request.IsRequired;
        type.ValidadeMeses = request.ValidadeMeses;

        await _db.SaveChangesAsync();

        return new DocumentTypeDto(type.Id, type.Nome, type.Descricao, type.IsRequired, type.ValidadeMeses, type.IsActive);
    }

    public async Task DeleteDocumentTypeAsync(Guid id, Guid tenantId)
    {
        var type = await _db.DocumentTypes.FirstOrDefaultAsync(dt => dt.Id == id);
        if (type is null)
            throw new DocumentException("document_type_not_found", 404);

        // Soft-delete
        type.IsActive = false;
        await _db.SaveChangesAsync();
    }

    // ── Helpers ────────────────────────────────────────────────────

    private async Task<Stream> GetFileStreamSafeAsync(string caminhoArquivo)
    {
        try
        {
            return await _storage.GetAsync(caminhoArquivo);
        }
        catch (FileNotFoundException)
        {
            throw new DocumentException("document_file_not_found", 404);
        }
        catch (UnauthorizedAccessException)
        {
            // Caminho armazenado aponta para fora do storage (ex.: seed antigo)
            throw new DocumentException("document_file_not_found", 404);
        }
    }

    private static DocumentDto MapToDto(Document d)
    {
        return new DocumentDto(
            d.Id, d.StudentId, d.Student.Nome, d.DocumentTypeId, d.DocumentType.Nome,
            d.NomeArquivo, d.Status.ToString(), d.DataValidade, d.MotivoRejeicao, d.CreatedAt);
    }
}

public class DocumentException : Exception
{
    public int StatusCode { get; }
    public DocumentException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}
