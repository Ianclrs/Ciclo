using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// M7 — arquivo órfão.
///
/// O upload grava o arquivo em disco ANTES do insert. Se o insert falha, o arquivo ficava para
/// sempre sem nenhum registro apontando para ele: lixo que ninguém vê e que nenhuma rotina
/// remove. A correção é compensação explícita no mesmo ponto que criou o arquivo.
///
/// O teste usa um contexto que falha no commit seguinte — o único jeito de exercitar o caminho
/// de erro sem depender de um banco fora do ar.
/// </summary>
public sealed class OrphanFileTests
{
    /// <summary>
    /// Tenant fixo: o EF cacheia o modelo por TIPO de contexto e o filtro global fica preso à
    /// instância que construiu o modelo — variar o tenant entre testes faria o segundo enxergar
    /// o filtro do primeiro (mesma armadilha documentada em <c>DocumentTestData</c>). O isolamento
    /// vem do nome único do banco InMemory.
    /// </summary>
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FailingDbContext : AppDbContext
    {
        internal bool FailNextSave { get; set; }

        internal FailingDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            FailNextSave
                ? Task.FromException<int>(new InvalidOperationException("insert simulado"))
                : base.SaveChangesAsync(cancellationToken);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        internal FixedTenantContext(Guid tenantId) => TenantId = tenantId;

        public Guid TenantId { get; }

        public bool IsResolved => true;

        public void SetTenant(Guid tenantId) => throw new NotSupportedException();
    }

    [Fact]
    public async Task FailedInsert_RemovesTheFileThatWasAlreadyWritten()
    {
        var tenantId = TestTenantId;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new FailingDbContext(options, new FixedTenantContext(tenantId));
        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var type = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "RG");

        var storage = new NoopStorage();
        var service = new DocumentService(
            db, storage, new NoopNotifications(), NullLogger<DocumentService>.Instance);

        db.FailNextSave = true;
        using var stream = new MemoryStream("%PDF-1.7"u8.ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UploadAsync(
                stream, "rg.pdf", "application/pdf",
                student.Id, type.Id, tenantId, Guid.NewGuid(), UserRole.Admin));

        // O arquivo chegou a ser gravado e foi removido na compensação.
        Assert.Single(storage.Deleted);

        db.FailNextSave = false;
        Assert.Empty(db.Documents);
    }

    /// <summary>Controle positivo: com o commit funcionando, o arquivo NÃO é removido.</summary>
    [Fact]
    public async Task SuccessfulInsert_KeepsTheFile()
    {
        var tenantId = TestTenantId;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new FailingDbContext(options, new FixedTenantContext(tenantId));
        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var type = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "RG");

        var storage = new NoopStorage();
        var service = new DocumentService(
            db, storage, new NoopNotifications(), NullLogger<DocumentService>.Instance);

        using var stream = new MemoryStream("%PDF-1.7"u8.ToArray());
        var document = await service.UploadAsync(
            stream, "rg.pdf", "application/pdf",
            student.Id, type.Id, tenantId, Guid.NewGuid(), UserRole.Admin);

        Assert.NotNull(document);
        Assert.Empty(storage.Deleted);
        Assert.Single(db.Documents);
    }
}
