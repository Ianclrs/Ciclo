using System.Text;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// M6 — validações na camada dona (serviço). Cada teste falha com o código de erro que o
/// cliente recebe, não apenas com "lançou exceção": o código é o contrato.
/// </summary>
public sealed class StudentValidationTests
{
    private const string ValidCpf = "11144477735";

    private static CreateStudentRequest Request(
        DateTime? birthDate = null, string? cpf = null, int? anoLetivo = null, string? foto = null) =>
        new("Aluno Válido",
            birthDate ?? DateTime.UtcNow.AddYears(-10),
            cpf,
            "5A",
            anoLetivo ?? DateTime.UtcNow.Year,
            null,
            foto);

    private static async Task<(Ciclo.Infrastructure.Data.AppDbContext Db, Guid TenantId, StudentService Service)>
        CreateAsync()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        return (db, tenantId, new StudentService(db));
    }

    [Fact]
    public async Task InvalidCpf_IsRejected()
    {
        var (db, tenantId, service) = await CreateAsync();
        await using var _ = db;

        var ex = await Assert.ThrowsAsync<StudentException>(
            () => service.CreateAsync(Request(cpf: "111.444.777-36"), tenantId, UserRole.Admin));

        Assert.Equal("cpf_invalid", ex.Message);
        Assert.Equal(400, ex.StatusCode);
    }

    /// <summary>O CPF é gravado só com dígitos — senão o mesmo CPF entraria duas vezes.</summary>
    [Fact]
    public async Task ValidFormattedCpf_IsStoredNormalized()
    {
        var (db, tenantId, service) = await CreateAsync();
        await using var _ = db;

        var created = await service.CreateAsync(
            Request(cpf: "111.444.777-35"), tenantId, UserRole.Admin);

        var stored = await db.Students.SingleAsync(s => s.Id == created.Id);
        Assert.Equal(ValidCpf, stored.Cpf);
    }

    [Fact]
    public async Task BirthDateTooRecent_IsRejected()
    {
        var (db, tenantId, service) = await CreateAsync();
        await using var _ = db;

        var ex = await Assert.ThrowsAsync<StudentException>(
            () => service.CreateAsync(
                Request(birthDate: DateTime.UtcNow.AddMonths(-6)), tenantId, UserRole.Admin));

        Assert.Equal("data_nascimento_too_recent", ex.Message);
    }

    [Fact]
    public async Task BirthDateTooOld_IsRejected()
    {
        var (db, tenantId, service) = await CreateAsync();
        await using var _ = db;

        var ex = await Assert.ThrowsAsync<StudentException>(
            () => service.CreateAsync(
                Request(birthDate: DateTime.UtcNow.AddYears(-150)), tenantId, UserRole.Admin));

        Assert.Equal("data_nascimento_too_old", ex.Message);
    }

    [Fact]
    public async Task AnoLetivoAboveTheCeiling_IsRejected()
    {
        var (db, tenantId, service) = await CreateAsync();
        await using var _ = db;

        var ex = await Assert.ThrowsAsync<StudentException>(
            () => service.CreateAsync(
                Request(anoLetivo: DateTime.UtcNow.Year + 10), tenantId, UserRole.Admin));

        Assert.Equal("ano_letivo_invalid", ex.Message);
    }

    /// <summary>O defeito do M6: SVG passava porque o prefixo era `data:image/`.</summary>
    [Fact]
    public async Task SvgPhoto_IsRejected()
    {
        var (db, tenantId, service) = await CreateAsync();
        await using var _ = db;

        var svg = "data:image/svg+xml;base64," +
            Convert.ToBase64String(Encoding.UTF8.GetBytes("<svg><script>alert(1)</script></svg>"));

        var ex = await Assert.ThrowsAsync<StudentException>(
            () => service.CreateAsync(Request(foto: svg), tenantId, UserRole.Admin));

        Assert.Equal("foto_invalid_format", ex.Message);
    }

    [Fact]
    public async Task PngPhoto_IsAccepted()
    {
        var (db, tenantId, service) = await CreateAsync();
        await using var _ = db;

        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        var foto = "data:image/png;base64," + Convert.ToBase64String(png);

        var created = await service.CreateAsync(Request(foto: foto), tenantId, UserRole.Admin);

        Assert.Equal(foto, created.Foto);
    }
}

public sealed class DocumentValidationTests
{
    private static async Task<(Ciclo.Infrastructure.Data.AppDbContext Db, Guid TenantId,
        DocumentService Service, Guid StudentId, Guid TypeId, Guid Actor)> CreateAsync()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var type = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "RG");
        var service = new DocumentService(db, new NoopStorage(), new NoopNotifications(), NullLogger<DocumentService>.Instance);

        return (db, tenantId, service, student.Id, type.Id, Guid.NewGuid());
    }

    [Fact]
    public async Task Upload_WithMatchingSignature_IsAccepted()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\nconteudo"));

        var document = await f.Service.UploadAsync(
            stream, "rg.pdf", "application/pdf", f.StudentId, f.TypeId, f.TenantId, f.Actor, UserRole.Admin);

        Assert.Equal("Pendente", document.Status);
    }

    /// <summary>Um executável renomeado para `.pdf` era aceito porque só a extensão era olhada.</summary>
    [Fact]
    public async Task Upload_WithContentNotMatchingExtension_IsRejected()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        using var stream = new MemoryStream("MZ\u0090\u0000executavel"u8.ToArray());

        var ex = await Assert.ThrowsAsync<DocumentException>(
            () => f.Service.UploadAsync(
                stream, "rg.pdf", "application/pdf", f.StudentId, f.TypeId, f.TenantId, f.Actor, UserRole.Admin));

        Assert.Equal("invalid_file_content", ex.Message);
    }

    [Fact]
    public async Task Upload_WithPdfContentNamedPng_IsRejected()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7"));

        var ex = await Assert.ThrowsAsync<DocumentException>(
            () => f.Service.UploadAsync(
                stream, "rg.png", "image/png", f.StudentId, f.TypeId, f.TenantId, f.Actor, UserRole.Admin));

        Assert.Equal("invalid_file_content", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public async Task Expiring_WithOutOfRangeDays_IsRejected(int days)
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        var ex = await Assert.ThrowsAsync<DocumentException>(
            () => f.Service.GetExpiringAsync(f.TenantId, days));

        Assert.Equal("days_out_of_range", ex.Message);
    }

    [Fact]
    public async Task Expiring_WithValidDays_IsAccepted()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        Assert.Empty(await f.Service.GetExpiringAsync(f.TenantId, 30));
    }
}

/// <summary>M6 — paginação normalizada em um dono único, inclusive onde faltava o piso.</summary>
public sealed class PaginationTests
{
    [Fact]
    public async Task DocumentPending_NormalizesZeroPageSize()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        await using var _ = db;

        var service = new DocumentService(db, new NoopStorage(), new NoopNotifications(), NullLogger<DocumentService>.Instance);
        var result = await service.GetPendingAsync(tenantId, 0, 0);

        Assert.Equal(1, result.Page);
        Assert.Equal(Pagination.DefaultPageSize, result.PageSize);
    }

    [Fact]
    public async Task NotificationList_NormalizesZeroPageSize()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        await using var _ = db;

        var service = new NotificationService(
            db, new NoopEmailDispatcher(), NullLogger<NotificationService>.Instance);

        var result = await service.GetForUserAsync(Guid.NewGuid(), false, 0, 0);

        Assert.Equal(1, result.Page);
        Assert.Equal(Pagination.DefaultPageSize, result.PageSize);
    }

    [Fact]
    public async Task StudentList_CapsPageSize()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        await using var _ = db;

        var result = await new StudentService(db)
            .GetAllAsync(null, null, null, 1, 9999, tenantId, Guid.NewGuid(), UserRole.Admin);

        Assert.Equal(Pagination.MaxPageSize, result.PageSize);
    }
}

/// <summary>
/// B2 — transmissão sem destinatários devolve "zero", não um DTO fantasma.
/// Um objeto com <c>Guid.Empty</c> e <c>DateTime.MinValue</c> é exibível pelo cliente e mente
/// sobre o que aconteceu; nulo obriga o chamador a tratar o caso.
/// </summary>
public sealed class BroadcastTests
{
    [Fact]
    public async Task Broadcast_WithoutRecipients_ReturnsZeroAndNoNotification()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        await using var _ = db;

        var service = new NotificationService(
            db, new NoopEmailDispatcher(), NullLogger<NotificationService>.Instance);

        var (count, notification) = await service.BroadcastAsync(
            new BroadcastNotificationRequest("Aviso", "Mensagem", NotificationType.Geral, null),
            tenantId,
            UserRole.Admin);

        Assert.Equal(0, count);
        Assert.Null(notification);
        Assert.Empty(db.Notifications);
    }
}
