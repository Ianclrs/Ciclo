using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Security;
using Ciclo.Infrastructure.Storage;

namespace Ciclo.Infrastructure.Services;

public interface IDocumentService
{
    // Documents
    Task<DocumentDto> UploadAsync(Stream fileStream, string fileName, string contentType, Guid studentId, Guid documentTypeId, Guid tenantId, Guid userId, UserRole role);
    Task<PagedResponse<DocumentDto>> GetStudentDocumentsAsync(Guid studentId, Guid tenantId, Guid userId, UserRole role, int page, int pageSize);
    Task<(Stream Stream, string ContentType, string FileName)> DownloadAsync(Guid documentId, Guid tenantId, Guid userId, UserRole role);
    Task<SignedDownloadDto> GetDownloadUrlAsync(Guid documentId, Guid tenantId, Guid userId, UserRole role);
    Task VerifyAsync(Guid documentId, VerifyDocumentRequest request, Guid tenantId, UserRole role);
    Task<PagedResponse<DocumentDto>> GetPendingAsync(Guid tenantId, int page, int pageSize);
    Task<PagedResponse<DocumentDto>> GetDocumentsAsync(DocumentFilter filter, Guid tenantId, int page, int pageSize);
    Task<List<DocumentDto>> GetExpiringAsync(Guid tenantId, int days);

    // Document Types
    Task<DocumentTypeDto> CreateDocumentTypeAsync(CreateDocumentTypeRequest request, Guid tenantId, UserRole role);
    Task<List<DocumentTypeDto>> GetDocumentTypesAsync(Guid tenantId);
    Task<DocumentTypeDto> UpdateDocumentTypeAsync(Guid id, CreateDocumentTypeRequest request, Guid tenantId, UserRole role);
    Task DeleteDocumentTypeAsync(Guid id, Guid tenantId, UserRole role);
}

public class DocumentService : IDocumentService
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly INotificationService _notificationService;
    private readonly ILogger<DocumentService> _logger;
    private readonly IAuditTrail _audit;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png"
    };

    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    // FR-007: o token assinado é a autorização da rota de bytes, então a validade é curta.
    private static readonly TimeSpan SignedUrlLifetime = TimeSpan.FromMinutes(5);

    // MIME do conteúdo gravado. A chave é a extensão já validada contra a assinatura real, então
    // o valor é o tipo detectado, não o que o cliente declarou no header da requisição.
    private static readonly Dictionary<string, string> MimeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
    };

    /// <summary>
    /// MIME derivado da extensão de um caminho/nome de arquivo. Público porque a rota de download
    /// assinada serve bytes sem passar pelo serviço: uma segunda cópia deste mapa divergiria na
    /// primeira extensão nova.
    /// </summary>
    public static string MimeForExtension(string pathOrFileName)
    {
        var ext = Path.GetExtension(pathOrFileName);
        return MimeByExtension.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
    }

    public DocumentService(
        AppDbContext db, IFileStorage storage, INotificationService notificationService,
        ILogger<DocumentService> logger, IAuditTrail audit)
    {
        _db = db;
        _storage = storage;
        _notificationService = notificationService;
        _logger = logger;
        _audit = audit;
    }

    // ── Documents ──────────────────────────────────────────────────

    public async Task<DocumentDto> UploadAsync(
        Stream fileStream, string fileName, string contentType,
        Guid studentId, Guid documentTypeId, Guid tenantId, Guid userId, UserRole role)
    {
        // Ordem da spec 170: o que é barato e definitivo vem primeiro, para nada ser gravado
        // (nem em quarentena) antes de a requisição ser válida.
        // M6: a extensão é texto que o cliente escolhe. A assinatura do conteúdo é o que
        // realmente define o tipo — um executável renomeado para .pdf era aceito antes.
        if (!fileStream.CanSeek)
            throw new DocumentException("invalid_file_content", 400);

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            throw new DocumentException("invalid_extension", 400);

        var kind = await FileSignature.DetectAsync(fileStream);
        if (!FileSignature.MatchesExtension(kind, ext))
        {
            await QuarantineAsync(tenantId, fileName, fileStream);
            throw new DocumentException("invalid_file_content", 400);
        }

        if (fileStream.Length > MaxFileSize)
            throw new DocumentException("file_too_large", 400);

        // Validate student
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId);
        if (student is null)
            throw new DocumentException("student_not_found", 404);

        // B9: regra de vínculo em um dono único.
        await StudentAccess.EnsureLinkedAsync(_db, studentId, userId, role);

        // Validate document type
        var docType = await _db.DocumentTypes
            .FirstOrDefaultAsync(dt => dt.Id == documentTypeId);
        if (docType is null)
            throw new DocumentException("document_type_not_found", 404);
        if (!docType.IsActive)
            throw new DocumentException("document_type_inactive", 400);

        // FR-006: a cota é conferida antes de qualquer byte ir para o driver. Não é transacional:
        // dois uploads simultâneos podem passar pela mesma verificação — comportamento declarado.
        var used = await _db.Documents.Where(d => d.TenantId == tenantId).SumAsync(d => d.TamanhoBytes);
        var quota = await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.StorageQuotaBytes)
            .FirstOrDefaultAsync();

        if (used + fileStream.Length > quota)
            throw new DocumentException("storage_quota_exceeded", 413);

        // FR-004: o decorador calcula SHA-256 e tamanho durante a gravação — sem segunda passagem
        // pelo conteúdo e sem exigir que ele caiba em memória.
        fileStream.Position = 0;
        await using var hashing = new HashingReadStream(fileStream, MaxFileSize);

        string filePath;
        try
        {
            filePath = await _storage.SaveAsync(tenantId, studentId, fileName, hashing);
        }
        catch (InvalidDataException)
        {
            // O driver já removeu o arquivo parcial; aqui só o código de problema muda.
            throw new DocumentException("file_too_large", 400);
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StudentId = studentId,
            DocumentTypeId = documentTypeId,
            NomeArquivo = fileName,
            CaminhoArquivo = filePath,
            TamanhoBytes = hashing.BytesRead,
            HashSha256 = hashing.HashHex,
            ContentType = MimeByExtension[ext],
            EnviadoPorUsuarioId = userId,
            Status = DocumentStatus.Pendente,
            CreatedAt = DateTime.UtcNow
        };

        _db.Documents.Add(document);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            // M7: o arquivo é gravado ANTES do commit. Sem compensação, um insert que falha
            // deixa o arquivo em disco para sempre, sem registro que o referencie.
            _db.Entry(document).State = EntityState.Detached;

            try
            {
                await _storage.DeleteAsync(filePath);
            }
#pragma warning disable CA1031 // limpeza não pode mascarar o erro original; a falha é registrada
            catch (Exception cleanup)
#pragma warning restore CA1031
            {
#pragma warning disable CA1848, CA1873
                _logger.LogWarning(cleanup,
                    "Falha ao remover arquivo órfão {FilePath} após erro no insert do documento", filePath);
#pragma warning restore CA1848, CA1873
            }

            throw;
        }

        return MapToDto(document);
    }

    public async Task<PagedResponse<DocumentDto>> GetStudentDocumentsAsync(
        Guid studentId, Guid tenantId, Guid userId, UserRole role, int page, int pageSize)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        // B9: regra de vínculo em um dono único.
        await StudentAccess.EnsureLinkedAsync(_db, studentId, userId, role);

        var query = _db.Documents
            .Include(d => d.DocumentType)
            .Include(d => d.Student)
            .Where(d => d.StudentId == studentId);

        var total = await query.CountAsync();

        var docs = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(DocumentQueries.ToDto)
            .ToListAsync();

        return new PagedResponse<DocumentDto>(docs, total, page, pageSize);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> DownloadAsync(
        Guid documentId, Guid tenantId, Guid userId, UserRole role)
    {
        var doc = await LoadAuthorizedAsync(documentId, tenantId, userId, role);

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

    /// <summary>
    /// FR-007: emite a URL assinada sem servir bytes. A autorização é a mesma do download; o
    /// token devolvido passa a ser a autorização da requisição que lê o arquivo.
    /// </summary>
    public async Task<SignedDownloadDto> GetDownloadUrlAsync(
        Guid documentId, Guid tenantId, Guid userId, UserRole role)
    {
        var doc = await LoadAuthorizedAsync(documentId, tenantId, userId, role);
        var url = await _storage.GetSignedUrlAsync(doc.CaminhoArquivo, SignedUrlLifetime);

        return new SignedDownloadDto(url, DateTimeOffset.UtcNow.Add(SignedUrlLifetime));
    }

    public async Task VerifyAsync(Guid documentId, VerifyDocumentRequest request, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.DocumentVerify, role);

        var doc = await _db.Documents
            .Include(d => d.DocumentType)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (doc is null)
            throw new DocumentException("document_not_found", 404);

        var previousStatus = doc.Status.ToString();
        var previousValidity = doc.DataValidade;

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

        // O evento entra no change tracker ANTES de qualquer SaveChanges: o envio de notificação
        // grava internamente, então registrar depois dele deixaria a rejeição persistida sem
        // auditoria se a trilha falhasse (FR-003).
        _audit.Record(new AuditEntry(
            request.Approved ? AuditActions.DocumentVerified : AuditActions.DocumentRejected,
            AuditTargetTypes.Document,
            doc.Id,
            new { Status = previousStatus, DataValidade = previousValidity },
            new { Status = doc.Status.ToString(), DataValidade = doc.DataValidade, MotivoRejeicao = doc.MotivoRejeicao }));

        // FR-008: Auto-notification on document rejection (before SaveChanges = same transaction)
        if (!request.Approved)
        {
            var titulo = $"Documento rejeitado: {doc.DocumentType.Nome}";
            var mensagem = $"O documento '{doc.NomeArquivo}' foi rejeitado. Motivo: {doc.MotivoRejeicao}";
            await _notificationService.SendByStudentAsync(
                doc.StudentId, titulo, mensagem, NotificationType.DocumentoPendente, doc.Id, tenantId, role);
        }

        await _db.SaveChangesAsync();
    }

    public async Task<PagedResponse<DocumentDto>> GetPendingAsync(Guid tenantId, int page, int pageSize)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        var query = _db.Documents
            .Include(d => d.DocumentType)
            .Include(d => d.Student)
            .Where(d => d.Status == DocumentStatus.Pendente);

        var total = await query.CountAsync();
        var docs = await query
            .OrderBy(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(DocumentQueries.ToDto)
            .ToListAsync();

        return new PagedResponse<DocumentDto>(docs, total, page, pageSize);
    }

    public async Task<PagedResponse<DocumentDto>> GetDocumentsAsync(
        DocumentFilter filter, Guid tenantId, int page, int pageSize)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        var query = _db.Documents
            .Include(d => d.DocumentType)
            .Include(d => d.Student)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            // Mesma regra do M5: filtro inválido não vira "todos" em silêncio.
            if (!Enum.TryParse<DocumentStatus>(filter.Status.Trim(), ignoreCase: true, out var status)
                || !Enum.IsDefined(status))
                throw new DocumentException("status_invalid", 400);

            query = query.Where(d => d.Status == status);
        }

        if (filter.StudentId is { } studentId)
            query = query.Where(d => d.StudentId == studentId);

        if (filter.DocumentTypeId is { } documentTypeId)
            query = query.Where(d => d.DocumentTypeId == documentTypeId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.WhereStudentNameContains(filter.Search.Trim().ToLowerInvariant());

        var total = await query.CountAsync();

        var documents = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(DocumentQueries.ToDto)
            .ToListAsync();

        return new PagedResponse<DocumentDto>(documents, total, page, pageSize);
    }

    public async Task<List<DocumentDto>> GetExpiringAsync(Guid tenantId, int days)
    {
        // M6: sem limite, `days=999999` varria a tabela inteira e `days=-1` devolvia o passado.
        if (days < 1 || days > 365)
            throw new DocumentException("days_out_of_range", 400);

        var cutoff = DateTime.UtcNow.AddDays(days);

        var docs = await _db.Documents
            .Include(d => d.DocumentType)
            .Include(d => d.Student)
            .Where(d => d.Status == DocumentStatus.Aprovado
                && d.DataValidade != null
                && d.DataValidade <= cutoff)
            .OrderBy(d => d.DataValidade)
            .Select(DocumentQueries.ToDto)
            .ToListAsync();

        return docs;
    }

    // ── Document Types ─────────────────────────────────────────────

    public async Task<DocumentTypeDto> CreateDocumentTypeAsync(CreateDocumentTypeRequest request, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.DocumentTypeManage, role);

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

    public async Task<DocumentTypeDto> UpdateDocumentTypeAsync(Guid id, CreateDocumentTypeRequest request, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.DocumentTypeManage, role);

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

    public async Task DeleteDocumentTypeAsync(Guid id, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.DocumentTypeManage, role);

        var type = await _db.DocumentTypes.FirstOrDefaultAsync(dt => dt.Id == id);
        if (type is null)
            throw new DocumentException("document_type_not_found", 404);

        // Soft-delete
        type.IsActive = false;
        await _db.SaveChangesAsync();
    }

    // ── Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Dono único da carga e da autorização de um documento já identificado. Download e emissão
    /// de URL assinada passam por aqui, então as duas rotas não podem divergir no controle de
    /// acesso.
    /// </summary>
    private async Task<Document> LoadAuthorizedAsync(
        Guid documentId, Guid tenantId, Guid userId, UserRole role)
    {
        var doc = await _db.Documents
            .Include(d => d.Student)
            .ThenInclude(s => s.StudentParents)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.TenantId == tenantId);

        if (doc is null)
            throw new DocumentException("document_not_found", 404);

        // B9: regra de vínculo em um dono único.
        await StudentAccess.EnsureLinkedAsync(_db, doc.StudentId, userId, role);

        return doc;
    }

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

    /// <summary>
    /// Retém os bytes suspeitos antes de recusar. Falha na retenção é registrada e não altera a
    /// resposta: o cliente continua recebendo a mesma recusa, e o arquivo perdido fica no log.
    /// </summary>
    private async Task QuarantineAsync(Guid tenantId, string fileName, Stream fileStream)
    {
        try
        {
            fileStream.Position = 0;
            await _storage.SaveQuarantineAsync(tenantId, fileName, fileStream);
        }
#pragma warning disable CA1031 // a recusa original é o que importa; a falha aqui só é registrada
        catch (Exception ex)
#pragma warning restore CA1031
        {
#pragma warning disable CA1848, CA1873
            _logger.LogError(ex, "Falha ao reter {FileName} na quarentena", fileName);
#pragma warning restore CA1848, CA1873
        }
    }

    private static DocumentDto MapToDto(Document d)
    {
        return new DocumentDto(
            d.Id, d.StudentId, d.Student.Nome, d.DocumentTypeId, d.DocumentType.Nome,
            d.NomeArquivo, d.Status.ToString(), d.DataValidade, d.MotivoRejeicao, d.CreatedAt,
            d.TamanhoBytes, d.HashSha256, d.ContentType, d.EnviadoPorUsuarioId);
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
