using System.Security.Cryptography;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 170 FR-004/FR-005/FR-006/FR-008 — o que o serviço de documentos faz com o conteúdo antes,
/// durante e depois de gravar.
///
/// O dublê de armazenamento aqui CONSOME o stream, como o driver real: é essa leitura que alimenta
/// o SHA-256 e o tamanho persistidos. Um dublê que ignorasse o conteúdo provaria só que o upload
/// não calcula nada. A cota é verificada no dublê por <c>Times.Never</c>/<c>Times.Once</c> em
/// <c>SaveAsync</c>: "não gravou nada" é o invariante do FR-006, e ele não é observável pelo
/// banco.
/// </summary>
public sealed class DocumentStorageScaleTests
{
    private const int MaxFileSize = 10 * 1024 * 1024;

    private sealed record Fixture(
        AppDbContext Db,
        Mock<IFileStorage> Storage,
        DocumentService Service,
        Guid TenantId,
        Guid UserId,
        Student Student,
        DocumentType Type);

    private static async Task<Fixture> CreateAsync(long? quota = null)
    {
        var (db, tenantId) = DocumentTestData.CreateContext();

        var type = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "RG");
        var student = await DocumentTestData.AddStudentAsync(db, tenantId);

        if (quota is long fixedQuota)
        {
            var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);
            tenant.StorageQuotaBytes = fixedQuota;
            await db.SaveChangesAsync();
        }

        var storage = new Mock<IFileStorage>();
        storage
            .Setup(s => s.SaveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns((Guid tenant, Guid studentId, string fileName, Stream content) =>
                ConsumeAsync($"{tenant}/{studentId}/{fileName}", content));
        storage
            .Setup(s => s.SaveQuarantineAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>()))
            .ReturnsAsync("quarantine/retencao");

        var service = new DocumentService(
            db, storage.Object, new NoopNotifications(),
            NullLogger<DocumentService>.Instance, NoopAuditTrail.Instance);

        return new Fixture(db, storage, service, tenantId, Guid.NewGuid(), student, type);
    }

    /// <summary>
    /// Consome o stream como o driver real: sem essa leitura o SHA-256 e o tamanho
    /// persistidos seriam de um conteúdo que ninguém gravou.
    /// </summary>
    private static async Task<string> ConsumeAsync(string key, Stream content)
    {
        await content.CopyToAsync(Stream.Null);
        return key;
    }

    /// <summary>
    /// O contentType declarado é deliberadamente genérico: o MIME persistido precisa vir da
    /// assinatura real, não do header que o cliente escolhe.
    /// </summary>
    private static async Task<DocumentDto> UploadAsync(
        Fixture fixture, byte[] content, string fileName = "rg.pdf")
    {
        using var source = new MemoryStream(content);
        return await fixture.Service.UploadAsync(
            source, fileName, "application/octet-stream",
            fixture.Student.Id, fixture.Type.Id, fixture.TenantId, fixture.UserId, UserRole.Staff);
    }

    private static byte[] PdfContent(int length)
    {
        var content = new byte[length];
        "%PDF-1.7\n"u8.CopyTo(content);

        for (var i = 9; i < content.Length; i++)
            content[i] = (byte)('a' + i % 26);

        return content;
    }

    private static async Task SeedStoredBytesAsync(Fixture fixture, long bytes)
    {
        fixture.Db.Documents.Add(new Document
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.TenantId,
            StudentId = fixture.Student.Id,
            DocumentTypeId = fixture.Type.Id,
            NomeArquivo = "anterior.pdf",
            CaminhoArquivo = "/tmp/anterior.pdf",
            TamanhoBytes = bytes,
            Status = DocumentStatus.Pendente,
            CreatedAt = DateTime.UtcNow,
        });

        await fixture.Db.SaveChangesAsync();
    }

    private static Mock<IFileStorage> ScannerStorage(IEnumerable<string> keys)
    {
        var storage = new Mock<IFileStorage>();
        storage
            .Setup(s => s.ListKeysAsync(It.IsAny<Guid?>()))
            .ReturnsAsync(new List<string>(keys));
        storage
            .Setup(s => s.DeleteAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        return storage;
    }

    [Fact]
    public async Task UploadAsync_PersistsSizeHashContentTypeAndAuthor()
    {
        var fixture = await CreateAsync();
        await using var _ = fixture.Db;
        var content = PdfContent(4096);

        var dto = await UploadAsync(fixture, content);

        Assert.Equal(content.Length, dto.TamanhoBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)), dto.HashSha256);
        Assert.Equal("application/pdf", dto.ContentType);
        Assert.Equal(fixture.UserId, dto.EnviadoPorUsuarioId);

        var stored = await fixture.Db.Documents.SingleAsync(d => d.Id == dto.Id);
        Assert.Equal(content.Length, stored.TamanhoBytes);
        Assert.Equal(dto.HashSha256, stored.HashSha256);
        Assert.Equal("application/pdf", stored.ContentType);
        Assert.Equal(fixture.UserId, stored.EnviadoPorUsuarioId);
    }

    [Fact]
    public async Task UploadAsync_OverQuota_IsRejectedWithoutWritingBytes()
    {
        var content = PdfContent(2048);
        var fixture = await CreateAsync(quota: 2047);
        await using var _ = fixture.Db;

        var exception = await Assert.ThrowsAsync<DocumentException>(() => UploadAsync(fixture, content));

        Assert.Equal("storage_quota_exceeded", exception.Message);
        Assert.Equal(413, exception.StatusCode);
        fixture.Storage.Verify(
            s => s.SaveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>()),
            Times.Never);
        Assert.Empty(await fixture.Db.Documents.ToListAsync());
    }

    [Fact]
    public async Task UploadAsync_EqualToRemainingQuota_IsAccepted()
    {
        var content = PdfContent(2048);
        var fixture = await CreateAsync(quota: 1000 + 2048);
        await using var _ = fixture.Db;
        await SeedStoredBytesAsync(fixture, 1000);

        var dto = await UploadAsync(fixture, content);

        Assert.Equal(content.Length, dto.TamanhoBytes);
    }

    [Fact]
    public async Task UploadAsync_OneByteAboveRemainingQuota_IsRejected()
    {
        var content = PdfContent(2048);
        var fixture = await CreateAsync(quota: 1000 + 2048 - 1);
        await using var _ = fixture.Db;
        await SeedStoredBytesAsync(fixture, 1000);

        var exception = await Assert.ThrowsAsync<DocumentException>(() => UploadAsync(fixture, content));

        Assert.Equal("storage_quota_exceeded", exception.Message);
        fixture.Storage.Verify(
            s => s.SaveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadAsync_SuspiciousContent_IsQuarantinedAndRejected()
    {
        // Assinatura JPEG com extensão .pdf: o conteúdo é retido, a resposta continua a mesma.
        var content = new byte[600];
        content[0] = 0xFF;
        content[1] = 0xD8;
        content[2] = 0xFF;
        content[3] = 0xE0;

        var fixture = await CreateAsync();
        await using var _ = fixture.Db;

        var exception = await Assert.ThrowsAsync<DocumentException>(() => UploadAsync(fixture, content));

        Assert.Equal("invalid_file_content", exception.Message);
        Assert.Equal(400, exception.StatusCode);
        fixture.Storage.Verify(
            s => s.SaveQuarantineAsync(fixture.TenantId, "rg.pdf", It.IsAny<Stream>()),
            Times.Once);
        fixture.Storage.Verify(
            s => s.SaveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>()),
            Times.Never);
        Assert.Empty(await fixture.Db.Documents.ToListAsync());
    }

    [Fact]
    public async Task UploadAsync_AboveMaximumSize_IsRejectedWithoutWritingBytes()
    {
        var content = PdfContent(MaxFileSize + 1);
        var fixture = await CreateAsync();
        await using var _ = fixture.Db;

        var exception = await Assert.ThrowsAsync<DocumentException>(() => UploadAsync(fixture, content));

        Assert.Equal("file_too_large", exception.Message);
        Assert.Equal(400, exception.StatusCode);
        fixture.Storage.Verify(
            s => s.SaveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>()),
            Times.Never);
    }

    [Fact]
    public async Task ScanAsync_ReportsKeysWithoutDocument()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        await using var _ = db;
        var type = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "RG");
        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var documented = await DocumentTestData.AddDocumentAsync(
            db, tenantId, student.Id, type.Id, DocumentStatus.Pendente);

        var storage = ScannerStorage([documented.CaminhoArquivo, "/tmp/orfao.pdf"]);
        var scanner = new OrphanFileScanner(
            storage.Object, db, NullLogger<OrphanFileScanner>.Instance);

        var result = await scanner.ScanAsync(tenantId: null, deleteOrphans: false);

        Assert.Equal(2, result.Scanned);
        Assert.Equal(1, result.Orphaned);
        Assert.Equal(0, result.Deleted);
        Assert.Empty(result.Failures);
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ScanAsync_WithDelete_RemovesOrphansAndCountsThem()
    {
        var (db, _) = DocumentTestData.CreateContext();
        await using var __ = db;

        var storage = ScannerStorage(["/tmp/orfao-a.pdf", "/tmp/orfao-b.pdf"]);
        var scanner = new OrphanFileScanner(
            storage.Object, db, NullLogger<OrphanFileScanner>.Instance);

        var result = await scanner.ScanAsync(tenantId: null, deleteOrphans: true);

        Assert.Equal(2, result.Orphaned);
        Assert.Equal(2, result.Deleted);
        Assert.Empty(result.Failures);
        storage.Verify(s => s.DeleteAsync("/tmp/orfao-a.pdf"), Times.Once);
        storage.Verify(s => s.DeleteAsync("/tmp/orfao-b.pdf"), Times.Once);
    }

    [Fact]
    public async Task ScanAsync_WithEmptyRoot_ReturnsEmptyResult()
    {
        var (db, _) = DocumentTestData.CreateContext();
        await using var __ = db;

        var scanner = new OrphanFileScanner(
            ScannerStorage([]).Object, db, NullLogger<OrphanFileScanner>.Instance);

        var result = await scanner.ScanAsync(tenantId: null, deleteOrphans: false);

        Assert.Equal(0, result.Scanned);
        Assert.Equal(0, result.Orphaned);
        Assert.Equal(0, result.Deleted);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task ScanAsync_IsolatesFailuresPerKey()
    {
        var (db, _) = DocumentTestData.CreateContext();
        await using var __ = db;

        var storage = ScannerStorage(["/tmp/travado.pdf", "/tmp/orfao.pdf"]);
        storage
            .Setup(s => s.DeleteAsync("/tmp/travado.pdf"))
            .ThrowsAsync(new IOException("arquivo em uso"));

        var scanner = new OrphanFileScanner(
            storage.Object, db, NullLogger<OrphanFileScanner>.Instance);

        var result = await scanner.ScanAsync(tenantId: null, deleteOrphans: true);

        Assert.Equal(2, result.Orphaned);
        Assert.Equal(1, result.Deleted);
        Assert.Single(result.Failures);
        Assert.Contains("/tmp/travado.pdf", result.Failures[0], StringComparison.Ordinal);
        storage.Verify(s => s.DeleteAsync("/tmp/orfao.pdf"), Times.Once);
    }
}
