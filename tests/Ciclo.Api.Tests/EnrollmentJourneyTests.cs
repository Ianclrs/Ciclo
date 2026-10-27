using System.Text;
using Ciclo.Core.Entities;
using Ciclo.Core.Exceptions;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// T1 — jornada ponta-a-ponta no nível dos serviços: matrícula → documento → envio → aprovação.
///
/// A suíte já cobria cada regra isolada; faltava o encadeamento. É aqui que um contrato quebrado
/// entre serviços aparece: um documento aprovado que não conta como entregue, ou um submit que
/// não move o aluno junto com a matrícula.
///
/// (O que continua fora: a jornada via HTTP, com autenticação e controllers. Essa camada é
/// coberta por autorização/rotas, mas não por um fluxo completo.)
/// </summary>
public sealed class EnrollmentJourneyTests
{
    [Fact]
    public async Task EnrollWithRequiredDocument_SubmitApprove_ReachesApproved()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        await using var _ = db;

        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, DateTime.UtcNow.Year + 1);
        var type = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Certidão", isRequired: true);

        var enrollments = new EnrollmentService(db, NoopAuditTrail.Instance);
        var documents = new DocumentService(
            db, new NoopStorage(), new NoopNotifications(), NullLogger<DocumentService>.Instance, NoopAuditTrail.Instance);
        var actor = Guid.NewGuid();

        // 1. matrícula em rascunho
        var enrollment = await enrollments.CreateEnrollmentAsync(
            new CreateEnrollmentRequest(student.Id, period.Id), tenantId, UserRole.Admin);
        Assert.Equal("Rascunho", enrollment.Status);

        // 2. documento obrigatório: upload + verificação
        using var pdf = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\nconteudo"));
        var uploaded = await documents.UploadAsync(
            pdf, "certidao.pdf", "application/pdf",
            student.Id, type.Id, tenantId, actor, UserRole.Admin);
        await documents.VerifyAsync(
            uploaded.Id, new VerifyDocumentRequest(true, null), tenantId, UserRole.Admin);

        // 3. envio: com a documentação em dia, o destino é Pendente (e não DocumentacaoPendente)
        var submitted = await enrollments.SubmitAsync(enrollment.Id, tenantId, actor, UserRole.Staff);
        Assert.Equal("Pendente", submitted.Status);

        // 4. aprovação: a matrícula E o aluno mudam juntos
        var approved = await enrollments.ApproveAsync(enrollment.Id, tenantId, actor, UserRole.Staff);
        Assert.Equal("Aprovado", approved.Status);

        var stored = await db.Students.FirstAsync(s => s.Id == student.Id);
        Assert.Equal(StudentStatus.Ativo, stored.Status);
        Assert.Equal(period.AnoLetivo, stored.AnoLetivo);
    }

    /// <summary>Controle da jornada: sem o documento, o envio cai em DocumentacaoPendente.</summary>
    [Fact]
    public async Task SubmitWithoutRequiredDocument_GoesToDocumentacaoPendente()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        await using var _ = db;

        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, DateTime.UtcNow.Year + 1);
        await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Certidão", isRequired: true);

        var enrollments = new EnrollmentService(db, NoopAuditTrail.Instance);
        var enrollment = await enrollments.CreateEnrollmentAsync(
            new CreateEnrollmentRequest(student.Id, period.Id), tenantId, UserRole.Admin);

        var submitted = await enrollments.SubmitAsync(
            enrollment.Id, tenantId, Guid.NewGuid(), UserRole.Staff);

        Assert.Equal("DocumentacaoPendente", submitted.Status);
    }
}

/// <summary>
/// T1 — ParentService estava coberto só no dashboard. O detalhe do filho é o caminho que reúne
/// aluno, documentos e matrícula, e é justamente o que um vínculo errado deixaria vazar.
/// </summary>
public sealed class ParentServiceTests
{
    private sealed record Fixture(
        Ciclo.Infrastructure.Data.AppDbContext Db,
        Guid TenantId,
        Student Student,
        ParentService Service,
        Guid LinkedParent);

    private static async Task<Fixture> CreateAsync()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var type = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "RG");
        await DocumentTestData.AddDocumentAsync(
            db, tenantId, student.Id, type.Id, DocumentStatus.Aprovado);

        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, 2026);
        await DocumentTestData.AddEnrollmentAsync(
            db, tenantId, student.Id, period.Id, EnrollmentStatus.Aprovado);

        var linkedParent = Guid.NewGuid();
        db.StudentParents.Add(new StudentParent { StudentId = student.Id, ParentId = linkedParent });
        await db.SaveChangesAsync();

        var documents = new DocumentService(
            db, new NoopStorage(), new NoopNotifications(), NullLogger<DocumentService>.Instance, NoopAuditTrail.Instance);

        return new Fixture(db, tenantId, student, new ParentService(db, documents), linkedParent);
    }

    [Fact]
    public async Task ChildDetail_ForLinkedParent_ReturnsDocumentsAndEnrollment()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        var detail = await f.Service.GetChildDetailAsync(f.LinkedParent, f.Student.Id, f.TenantId);

        Assert.Equal(f.Student.Id, detail.Student.Id);
        Assert.Single(detail.Documents);
        Assert.NotNull(detail.CurrentEnrollment);
        Assert.Equal("Aprovado", detail.CurrentEnrollment!.Status);
    }

    [Fact]
    public async Task ChildDetail_ForUnlinkedParent_IsDenied()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        await Assert.ThrowsAsync<AccessDeniedException>(
            () => f.Service.GetChildDetailAsync(Guid.NewGuid(), f.Student.Id, f.TenantId));
    }

    [Fact]
    public async Task ChildDocuments_ForUnlinkedParent_IsDenied()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        await Assert.ThrowsAsync<AccessDeniedException>(
            () => f.Service.GetChildDocumentsAsync(Guid.NewGuid(), f.Student.Id, f.TenantId));
    }

    [Fact]
    public async Task ChildDetail_ForUnknownStudent_IsDeniedWithTheContractCode()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        // Um id que não existe neste tenant recebe 403 (e não 404): distinguir os dois
        // permitiria descobrir quais identificadores existem em outro colégio.
        var ex = await Assert.ThrowsAsync<AccessDeniedException>(
            () => f.Service.GetChildDetailAsync(f.LinkedParent, Guid.NewGuid(), f.TenantId));

        Assert.Equal("not_linked_to_student", ex.Message);
    }
}
