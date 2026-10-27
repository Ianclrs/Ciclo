using System.Reflection;
using Ciclo.Core.Entities;
using Ciclo.Core.Exceptions;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Email;
using Ciclo.Infrastructure.Security;
using Ciclo.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// M4 — a autorização tem um dono único (<see cref="AccessPolicy"/>) e é exigida DENTRO do
/// serviço, não apenas no atributo do controller.
///
/// O defeito original: a leitura recebia <c>userId</c>/<c>role</c> e checava o vínculo, mas a
/// escrita não recebia papel nenhum — a proteção existia só no <c>[Authorize]</c>. Qualquer outro
/// chamador do serviço (outro serviço, job, teste) executava a operação sem checagem.
///
/// Estes testes chamam o serviço diretamente, sem controller e sem token: é exatamente o caminho
/// que antes passava.
/// </summary>
public sealed class AccessPolicyTests
{
    [Fact]
    public async Task StudentWrite_IsDeniedForParent()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        await using var _ = db;

        var service = new StudentService(db);
        var request = new CreateStudentRequest(
            "Aluno Indevido", DateTime.UtcNow.AddYears(-10), null, "5A", 2026, null, null);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => service.CreateAsync(request, tenantId, UserRole.Parent));

        Assert.Equal("forbidden", ex.Message);
    }

    [Fact]
    public async Task StudentDelete_IsDeniedForStaff()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        await using var _ = db;

        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var service = new StudentService(db);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.DeleteAsync(student.Id, tenantId, UserRole.Staff));

        var stored = await db.Students.FindAsync(student.Id);
        Assert.Equal(StudentStatus.Ativo, stored!.Status);
    }

    [Fact]
    public async Task EnrollmentReview_IsDeniedForParent()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        await using var _ = db;

        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, 2026);
        var enrollment = await DocumentTestData.AddEnrollmentAsync(
            db, tenantId, student.Id, period.Id, EnrollmentStatus.Pendente);

        var service = new EnrollmentService(db, NoopAuditTrail.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.ApproveAsync(enrollment.Id, tenantId, Guid.NewGuid(), UserRole.Parent));

        var stored = await db.Enrollments.FindAsync(enrollment.Id);
        Assert.Equal(EnrollmentStatus.Pendente, stored!.Status);
    }

    [Fact]
    public async Task DocumentVerify_IsDeniedForParent()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        await using var _ = db;

        var type = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Certidão");
        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var document = await DocumentTestData.AddDocumentAsync(
            db, tenantId, student.Id, type.Id, DocumentStatus.Pendente);

        var service = new DocumentService(db, new NoopStorage(), new NoopNotifications(), NullLogger<DocumentService>.Instance, NoopAuditTrail.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.VerifyAsync(
                document.Id, new VerifyDocumentRequest(true, null), tenantId, UserRole.Parent));

        var stored = await db.Documents.FindAsync(document.Id);
        Assert.Equal(DocumentStatus.Pendente, stored!.Status);
    }

    /// <summary>
    /// Broadcast é Admin-only no controller; aqui a chamada é direta ao serviço, por Staff.
    /// </summary>
    [Fact]
    public async Task NotificationBroadcast_IsDeniedForStaff()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        await using var _ = db;

        var service = new NotificationService(
            db, new NoopEmailDispatcher(), NullLogger<NotificationService>.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.BroadcastAsync(
                new BroadcastNotificationRequest("Aviso", "Mensagem", NotificationType.Geral, null),
                tenantId,
                UserRole.Staff));
    }

    /// <summary>
    /// Guarda da divergência entre atributo e política: o controller e o serviço precisam ler a
    /// MESMA lista. Um literal aqui voltaria a permitir que as duas cópias divergissem — e a
    /// política passaria a valer só em um dos portões.
    /// </summary>
    [Fact]
    public void Controllers_DeclareRolesOnlyThroughThePolicyTable()
    {
        var controllers = Path.Combine(RepositoryRoot.Find(), "src", "Ciclo.Api", "Controllers");

        var offenders = Directory
            .EnumerateFiles(controllers, "*.cs")
            .Where(file => File.ReadAllText(file).Contains("[Authorize(Roles = \"", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Use AccessPolicy.Roles.* em [Authorize] (não um literal): {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Guarda contra o erro silencioso da tabela: um papel escrito errado numa constante
    /// (<c>"Admim"</c>) negaria o papel correto sem que nada falhasse. Toda entrada precisa ser
    /// um <see cref="UserRole"/> real.
    /// </summary>
    [Fact]
    public void EveryPolicyListContainsOnlyKnownRoles()
    {
        var constants = typeof(AccessPolicy.Roles)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToArray();

        Assert.NotEmpty(constants);

        foreach (var field in constants)
        {
            var csv = (string)field.GetRawConstantValue()!;
            var roles = csv.Split(',').Select(role => role.Trim()).ToArray();

            Assert.NotEmpty(roles);
            Assert.All(roles, role =>
                Assert.True(
                    Enum.TryParse<UserRole>(role, ignoreCase: false, out _),
                    $"{field.Name} contém um papel desconhecido: '{role}'"));
        }
    }

}
