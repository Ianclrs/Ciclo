using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// A6b — listagem global de documentos com filtros.
///
/// Os filtros são combináveis e o status inválido segue a regra do M5: erro explícito, nunca
/// "devolve tudo". A busca por nome do aluno é case-insensitive pela mesma razão (M5).
/// </summary>
public sealed class DocumentFilterTests
{
    private sealed record Fixture(
        Ciclo.Infrastructure.Data.AppDbContext Db,
        Guid TenantId,
        DocumentService Service,
        Student Ana,
        Student Bruno,
        DocumentType Rg,
        DocumentType Certidao);

    private static async Task<Fixture> CreateAsync()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();

        var rg = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "RG");
        var certidao = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Certidão");

        var ana = await DocumentTestData.AddStudentAsync(db, tenantId);
        ana.Nome = "Ana Souza";
        var bruno = await DocumentTestData.AddStudentAsync(db, tenantId);
        bruno.Nome = "Bruno Lima";
        await db.SaveChangesAsync();

        await DocumentTestData.AddDocumentAsync(db, tenantId, ana.Id, rg.Id, DocumentStatus.Pendente);
        await DocumentTestData.AddDocumentAsync(db, tenantId, ana.Id, certidao.Id, DocumentStatus.Aprovado);
        await DocumentTestData.AddDocumentAsync(db, tenantId, bruno.Id, rg.Id, DocumentStatus.Rejeitado);

        var service = new DocumentService(
            db, new NoopStorage(), new NoopNotifications(),
            NullLogger<DocumentService>.Instance, NoopAuditTrail.Instance);

        return new Fixture(db, tenantId, service, ana, bruno, rg, certidao);
    }

    [Fact]
    public async Task FilterByStatus_IsCaseInsensitiveAndReturnsOnlyThatStatus()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        var approved = await f.Service.GetDocumentsAsync(
            new DocumentFilter(Status: "aprovado"), f.TenantId, 1, 20);
        var pending = await f.Service.GetDocumentsAsync(
            new DocumentFilter(Status: "PENDENTE"), f.TenantId, 1, 20);

        Assert.Single(approved.Items);
        Assert.Equal("Aprovado", approved.Items[0].Status);
        Assert.Single(pending.Items);
        Assert.Equal("Pendente", pending.Items[0].Status);
    }

    [Fact]
    public async Task FilterByStudent_ReturnsOnlyThatStudentDocuments()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        var result = await f.Service.GetDocumentsAsync(
            new DocumentFilter(StudentId: f.Ana.Id), f.TenantId, 1, 20);

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item => Assert.Equal(f.Ana.Id, item.StudentId));
    }

    [Fact]
    public async Task FilterByDocumentType_ReturnsOnlyThatType()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        var result = await f.Service.GetDocumentsAsync(
            new DocumentFilter(DocumentTypeId: f.Rg.Id), f.TenantId, 1, 20);

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item => Assert.Equal(f.Rg.Id, item.DocumentTypeId));
    }

    [Theory]
    [InlineData("ana")]
    [InlineData("ANA")]
    [InlineData("  Ana  ")]
    public async Task FilterByStudentName_IsCaseInsensitiveAndTrimmed(string search)
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        var result = await f.Service.GetDocumentsAsync(
            new DocumentFilter(Search: search), f.TenantId, 1, 20);

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item => Assert.Equal("Ana Souza", item.StudentName));
    }

    [Fact]
    public async Task Filters_Combine()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        var result = await f.Service.GetDocumentsAsync(
            new DocumentFilter(Status: "Pendente", StudentId: f.Ana.Id, DocumentTypeId: f.Rg.Id),
            f.TenantId, 1, 20);

        Assert.Single(result.Items);
        Assert.Equal("RG", result.Items[0].DocumentTypeName);
    }

    [Theory]
    [InlineData("inexistente")]
    [InlineData("99")]
    public async Task InvalidStatus_IsRejectedInsteadOfIgnored(string status)
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        var ex = await Assert.ThrowsAsync<DocumentException>(
            () => f.Service.GetDocumentsAsync(
                new DocumentFilter(Status: status), f.TenantId, 1, 20));

        Assert.Equal("status_invalid", ex.Message);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Pagination_IsNormalized()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        var result = await f.Service.GetDocumentsAsync(new DocumentFilter(), f.TenantId, 0, 0);

        Assert.Equal(1, result.Page);
        Assert.Equal(Pagination.DefaultPageSize, result.PageSize);
        Assert.Equal(3, result.Total);
    }
}
