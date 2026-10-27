using Ciclo.Core.Entities;
using Ciclo.Core.Exceptions;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// B9 — matriz de acesso do responsável.
///
/// A regra "só o aluno vinculado" estava replicada em ~10 pontos, cada um com o seu tipo de
/// exceção; dois caminhos devolviam <c>forbidden</c> e os demais <c>not_linked_to_student</c>
/// para a MESMA condição. Agora a regra vive em <c>StudentAccess</c> e estes testes cobrem
/// todos os serviços que a consomem — inclusive o caso positivo, para a correção não virar
/// "negar todo mundo".
/// </summary>
public sealed class StudentAccessTests
{
    private sealed record Fixture(
        AppDbContext Db, Guid TenantId, Student Student, Enrollment Enrollment, Guid ParentId);

    private static async Task<Fixture> CreateAsync(bool linked)
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, 2026);
        var enrollment = await DocumentTestData.AddEnrollmentAsync(
            db, tenantId, student.Id, period.Id, EnrollmentStatus.Pendente);

        var parentId = Guid.NewGuid();
        if (linked)
        {
            db.StudentParents.Add(new StudentParent { StudentId = student.Id, ParentId = parentId });
            await db.SaveChangesAsync();
        }

        return new Fixture(db, tenantId, student, enrollment, parentId);
    }

    [Fact]
    public async Task UnlinkedParent_IsDeniedOnStudentDetail()
    {
        var f = await CreateAsync(linked: false);
        await using var _ = f.Db;

        var ex = await Assert.ThrowsAsync<AccessDeniedException>(
            () => new StudentService(f.Db).GetByIdAsync(f.Student.Id, f.TenantId, f.ParentId, UserRole.Parent));

        Assert.Equal("not_linked_to_student", ex.Message);
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task UnlinkedParent_IsDeniedOnEnrollmentHistory()
    {
        var f = await CreateAsync(linked: false);
        await using var _ = f.Db;

        await Assert.ThrowsAsync<AccessDeniedException>(
            () => new StudentService(f.Db).GetEnrollmentHistoryAsync(
                f.Student.Id, f.TenantId, f.ParentId, UserRole.Parent));
    }

    [Fact]
    public async Task UnlinkedParent_IsDeniedOnDocuments()
    {
        var f = await CreateAsync(linked: false);
        await using var _ = f.Db;

        var service = new DocumentService(f.Db, new NoopStorage(), new NoopNotifications(), NullLogger<DocumentService>.Instance, NoopAuditTrail.Instance);

        await Assert.ThrowsAsync<AccessDeniedException>(
            () => service.GetStudentDocumentsAsync(
                f.Student.Id, f.TenantId, f.ParentId, UserRole.Parent, 1, 20));
    }

    [Fact]
    public async Task UnlinkedParent_IsDeniedOnEnrollmentDetail()
    {
        var f = await CreateAsync(linked: false);
        await using var _ = f.Db;

        await Assert.ThrowsAsync<AccessDeniedException>(
            () => new EnrollmentService(f.Db, NoopAuditTrail.Instance).GetEnrollmentByIdAsync(
                f.Enrollment.Id, f.TenantId, f.ParentId, UserRole.Parent));
    }

    [Fact]
    public async Task UnlinkedParent_SeesNoStudentsInTheList()
    {
        var f = await CreateAsync(linked: false);
        await using var _ = f.Db;

        var result = await new StudentService(f.Db).GetAllAsync(
            null, null, null, 1, 20, f.TenantId, f.ParentId, UserRole.Parent);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    /// <summary>Controle positivo: vinculado, todos os caminhos funcionam.</summary>
    [Fact]
    public async Task LinkedParent_IsAllowedOnEveryPath()
    {
        var f = await CreateAsync(linked: true);
        await using var _ = f.Db;

        var student = await new StudentService(f.Db)
            .GetByIdAsync(f.Student.Id, f.TenantId, f.ParentId, UserRole.Parent);
        var history = await new StudentService(f.Db)
            .GetEnrollmentHistoryAsync(f.Student.Id, f.TenantId, f.ParentId, UserRole.Parent);
        var documents = await new DocumentService(f.Db, new NoopStorage(), new NoopNotifications(), NullLogger<DocumentService>.Instance, NoopAuditTrail.Instance)
            .GetStudentDocumentsAsync(f.Student.Id, f.TenantId, f.ParentId, UserRole.Parent, 1, 20);
        var enrollment = await new EnrollmentService(f.Db, NoopAuditTrail.Instance)
            .GetEnrollmentByIdAsync(f.Enrollment.Id, f.TenantId, f.ParentId, UserRole.Parent);
        var list = await new StudentService(f.Db)
            .GetAllAsync(null, null, null, 1, 20, f.TenantId, f.ParentId, UserRole.Parent);

        Assert.Equal(f.Student.Id, student.Id);
        Assert.Single(history);
        Assert.Empty(documents.Items);
        Assert.Equal(f.Enrollment.Id, enrollment.Id);
        Assert.Single(list.Items);
    }

    /// <summary>Staff não é responsável: vê a escola inteira, sem exigência de vínculo.</summary>
    [Fact]
    public async Task Staff_IsNotSubjectToTheLinkRule()
    {
        var f = await CreateAsync(linked: false);
        await using var _ = f.Db;

        var student = await new StudentService(f.Db)
            .GetByIdAsync(f.Student.Id, f.TenantId, Guid.NewGuid(), UserRole.Staff);
        var list = await new StudentService(f.Db)
            .GetAllAsync(null, null, null, 1, 20, f.TenantId, Guid.NewGuid(), UserRole.Staff);

        Assert.Equal(f.Student.Id, student.Id);
        Assert.Single(list.Items);
    }
}
