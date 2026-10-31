using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Auditing;
using Ciclo.Infrastructure.Auth;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Storage;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 110 — cobertura das operações sensíveis: cada decisão efetivada registra exatamente um
/// evento com ação, alvo e snapshots antes/depois; caminhos que falham antes da mutação não
/// registram nada; e uma falha da trilha impede a confirmação da operação (fail-closed).
/// </summary>
public sealed class AuditOperationCoverageTests
{
    private const string EnrollmentRejectReason = "documento ilegivel";
    private const string DocumentRejectReason = "documento vencido";

    /// <summary>
    /// Mesmas opções da trilha real (camelCase, nulos omitidos): os snapshots são objetos
    /// anônimos, então serializar é a única forma de afirmar o contrato JSON do evento.
    /// </summary>
    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string Snapshot(object? value) => JsonSerializer.Serialize(value, SnapshotJson);

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }

        public bool IsResolved { get; private set; }

        public void SetTenant(Guid tenantId)
        {
            TenantId = tenantId;
            IsResolved = true;
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal AppDbContext Db { get; }
        internal Guid TenantId { get; }
        internal RecordingAuditTrail Trail { get; }
        internal TestTenantContext TenantContext { get; }
        internal Student Student { get; }
        internal EnrollmentPeriod Period { get; }
        internal User User { get; }

        internal Fixture()
        {
            var (db, tenantId) = AuditTestData.CreateContext();
            Db = db;
            TenantId = tenantId;
            Trail = new RecordingAuditTrail();

            TenantContext = new TestTenantContext();
            TenantContext.SetTenant(tenantId);

            Student = new Student
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Nome = "Aluno Teste",
                Turma = "1A",
                AnoLetivo = 2026,
                Status = StudentStatus.Inativo
            };

            Period = new EnrollmentPeriod
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Nome = "2026",
                AnoLetivo = 2026,
                DataInicio = DateTime.UtcNow.AddMonths(-1),
                DataFim = DateTime.UtcNow.AddMonths(1)
            };

            User = new User
            {
                Id = Guid.NewGuid(),
                UserName = "secretaria@example.com",
                Email = "secretaria@example.com",
                Name = "Secretaria",
                TenantId = tenantId,
                Role = UserRole.Admin
            };

            Db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Escola Teste",
                Slug = "escola-teste",
                Country = "BR"
            });
            Db.Students.Add(Student);
            Db.EnrollmentPeriods.Add(Period);
            Db.Users.Add(User);
            Db.SaveChanges();
        }

        internal Enrollment AddEnrollment(EnrollmentStatus status)
        {
            var enrollment = new Enrollment
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                StudentId = Student.Id,
                EnrollmentPeriodId = Period.Id,
                Status = status
            };
            Db.Enrollments.Add(enrollment);
            Db.SaveChanges();
            return enrollment;
        }

        internal Document AddDocument(int validityMonths, DateTime? dataValidade = null)
        {
            var documentType = new DocumentType
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Nome = "RG",
                IsRequired = true,
                IsActive = true,
                ValidadeMeses = validityMonths
            };
            var document = new Document
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                StudentId = Student.Id,
                DocumentTypeId = documentType.Id,
                NomeArquivo = "rg.pdf",
                CaminhoArquivo = "rg.pdf",
                Status = DocumentStatus.Pendente,
                DataValidade = dataValidade
            };

            Db.DocumentTypes.Add(documentType);
            Db.Documents.Add(document);
            Db.SaveChanges();
            return document;
        }

        internal RefreshToken AddRefreshToken(string value, DateTime? revokedAt = null)
        {
            var token = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = User.Id,
                Token = value,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow,
                RevokedAt = revokedAt
            };
            Db.RefreshTokens.Add(token);
            Db.SaveChanges();
            return token;
        }

        internal DocumentService CreateDocumentService(IAuditTrail trail)
        {
            var notification = new Mock<INotificationService>();
            notification
                .Setup(n => n.SendByStudentAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NotificationType>(),
                    It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<UserRole>()))
                .ReturnsAsync(0);

            return new DocumentService(
                Db,
                new Mock<IFileStorage>().Object,
                notification.Object,
                NullLogger<DocumentService>.Instance,
                trail);
        }

        internal AuthService CreateAuthService(IAuditTrail trail)
        {
            var userStore = new Mock<IUserStore<User>>();
            var userManager = new Mock<UserManager<User>>(
                userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

            return new AuthService(
                userManager.Object,
                new Mock<IJwtTokenGenerator>().Object,
                Db,
                TenantContext,
                NullLogger<AuthService>.Instance,
                trail);
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task Approve_WritesEnrollmentApprovedEvent()
    {
        using var fixture = new Fixture();
        var enrollment = fixture.AddEnrollment(EnrollmentStatus.Pendente);
        var service = new EnrollmentService(fixture.Db, fixture.Trail);

        await service.ApproveAsync(enrollment.Id, fixture.TenantId, Guid.NewGuid(), UserRole.Admin);

        var entry = Assert.Single(fixture.Trail.Entries);
        Assert.Equal(AuditActions.EnrollmentApproved, entry.Action);
        Assert.Equal(AuditTargetTypes.Enrollment, entry.TargetType);
        Assert.Equal(enrollment.Id, entry.TargetId);

        Assert.Contains("\"status\":\"Pendente\"", Snapshot(entry.Before), StringComparison.Ordinal);

        var after = Snapshot(entry.After);
        Assert.Contains("\"status\":\"Aprovado\"", after, StringComparison.Ordinal);
        Assert.Contains("\"approvedAt\"", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reject_WritesEnrollmentRejectedEventWithReason()
    {
        using var fixture = new Fixture();
        var enrollment = fixture.AddEnrollment(EnrollmentStatus.Pendente);
        var service = new EnrollmentService(fixture.Db, fixture.Trail);

        await service.RejectAsync(
            enrollment.Id,
            new RejectEnrollmentRequest(EnrollmentRejectReason),
            fixture.TenantId,
            Guid.NewGuid(),
            UserRole.Admin);

        var entry = Assert.Single(fixture.Trail.Entries);
        Assert.Equal(AuditActions.EnrollmentRejected, entry.Action);
        Assert.Equal(AuditTargetTypes.Enrollment, entry.TargetType);
        Assert.Equal(enrollment.Id, entry.TargetId);

        var after = Snapshot(entry.After);
        Assert.Contains("\"status\":\"Rejeitado\"", after, StringComparison.Ordinal);
        Assert.Contains($"\"motivoRejeicao\":\"{EnrollmentRejectReason}\"", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancel_WritesEnrollmentCancelledEvent()
    {
        using var fixture = new Fixture();
        var enrollment = fixture.AddEnrollment(EnrollmentStatus.Pendente);
        var service = new EnrollmentService(fixture.Db, fixture.Trail);

        await service.CancelAsync(enrollment.Id, fixture.TenantId, Guid.NewGuid(), UserRole.Admin);

        var entry = Assert.Single(fixture.Trail.Entries);
        Assert.Equal(AuditActions.EnrollmentCancelled, entry.Action);
        Assert.Equal(AuditTargetTypes.Enrollment, entry.TargetType);
        Assert.Equal(enrollment.Id, entry.TargetId);
        Assert.Contains("\"status\":\"Cancelado\"", Snapshot(entry.After), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedTransition_WritesNoEvent()
    {
        using var fixture = new Fixture();
        var enrollment = fixture.AddEnrollment(EnrollmentStatus.Aprovado);
        var service = new EnrollmentService(fixture.Db, fixture.Trail);

        await Assert.ThrowsAsync<EnrollmentException>(() =>
            service.ApproveAsync(enrollment.Id, fixture.TenantId, Guid.NewGuid(), UserRole.Admin));

        Assert.Empty(fixture.Trail.Entries);
    }

    [Fact]
    public async Task VerifyApproved_WritesDocumentVerifiedEvent()
    {
        using var fixture = new Fixture();
        var previousValidity = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var document = fixture.AddDocument(validityMonths: 12, dataValidade: previousValidity);
        var service = fixture.CreateDocumentService(fixture.Trail);

        await service.VerifyAsync(
            document.Id, new VerifyDocumentRequest(true, null), fixture.TenantId, UserRole.Admin);

        var entry = Assert.Single(fixture.Trail.Entries);
        Assert.Equal(AuditActions.DocumentVerified, entry.Action);
        Assert.Equal(AuditTargetTypes.Document, entry.TargetType);
        Assert.Equal(document.Id, entry.TargetId);

        var before = Snapshot(entry.Before);
        Assert.Contains("\"status\":\"Pendente\"", before, StringComparison.Ordinal);
        Assert.Contains("\"dataValidade\":\"2025-01-01T00:00:00", before, StringComparison.Ordinal);

        var after = Snapshot(entry.After);
        Assert.Contains("\"status\":\"Aprovado\"", after, StringComparison.Ordinal);
        Assert.Contains("\"dataValidade\":\"", after, StringComparison.Ordinal);
        Assert.DoesNotContain("2025-01-01T00:00:00", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyRejected_WritesDocumentRejectedEventWithReason()
    {
        using var fixture = new Fixture();
        var previousValidity = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var document = fixture.AddDocument(validityMonths: 12, dataValidade: previousValidity);
        var service = fixture.CreateDocumentService(fixture.Trail);

        await service.VerifyAsync(
            document.Id,
            new VerifyDocumentRequest(false, DocumentRejectReason),
            fixture.TenantId,
            UserRole.Admin);

        var entry = Assert.Single(fixture.Trail.Entries);
        Assert.Equal(AuditActions.DocumentRejected, entry.Action);
        Assert.Equal(AuditTargetTypes.Document, entry.TargetType);
        Assert.Equal(document.Id, entry.TargetId);

        var after = Snapshot(entry.After);
        Assert.Contains("\"status\":\"Rejeitado\"", after, StringComparison.Ordinal);
        Assert.Contains($"\"motivoRejeicao\":\"{DocumentRejectReason}\"", after, StringComparison.Ordinal);
        Assert.Contains("\"dataValidade\":\"2025-01-01T00:00:00", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoke_WritesSessionRevokedEvent()
    {
        using var fixture = new Fixture();
        var token = fixture.AddRefreshToken("token-to-revoke");
        var service = fixture.CreateAuthService(fixture.Trail);

        await service.RevokeTokenAsync("token-to-revoke");

        var entry = Assert.Single(fixture.Trail.Entries);
        Assert.Equal(AuditActions.SessionRevoked, entry.Action);
        Assert.Equal(AuditTargetTypes.Session, entry.TargetType);
        Assert.Equal(token.Id, entry.TargetId);

        var after = Snapshot(entry.After);
        Assert.Contains("\"revokedAt\":\"", after, StringComparison.Ordinal);
        Assert.DoesNotContain("\"reason\"", after, StringComparison.Ordinal);

        var stored = await fixture.Db.RefreshTokens.SingleAsync(t => t.Id == token.Id);
        Assert.NotNull(stored.RevokedAt);
        Assert.Contains(
            stored.RevokedAt!.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            after,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevokeOnReuse_RecordsTheReason()
    {
        using var fixture = new Fixture();
        fixture.AddRefreshToken("token-reused", DateTime.UtcNow.AddMinutes(-5));
        var active = fixture.AddRefreshToken("token-active");
        var service = fixture.CreateAuthService(fixture.Trail);

        await Assert.ThrowsAsync<AuthException>(() => service.RefreshTokenAsync("token-reused"));

        var entry = Assert.Single(fixture.Trail.Entries);
        Assert.Equal(AuditActions.SessionRevoked, entry.Action);
        Assert.Equal(AuditTargetTypes.Session, entry.TargetType);
        Assert.Equal(active.Id, entry.TargetId);
        Assert.Contains("\"reason\":\"reuse_detected\"", Snapshot(entry.After), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approve_WhenAuditFails_DoesNotChangeTheEnrollment()
    {
        using var fixture = new Fixture();
        var enrollment = fixture.AddEnrollment(EnrollmentStatus.Pendente);
        var service = new EnrollmentService(fixture.Db, new ThrowingAuditTrail());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(enrollment.Id, fixture.TenantId, Guid.NewGuid(), UserRole.Admin));

        // Nada pode ter sido confirmado: a aprovação inteira é descartada com a falha da trilha.
        fixture.Db.ChangeTracker.Clear();

        var storedEnrollment = await fixture.Db.Enrollments.SingleAsync(e => e.Id == enrollment.Id);
        Assert.Equal(EnrollmentStatus.Pendente, storedEnrollment.Status);
        Assert.Null(storedEnrollment.ApprovedAt);

        var storedStudent = await fixture.Db.Students.SingleAsync(s => s.Id == fixture.Student.Id);
        Assert.Equal(StudentStatus.Inativo, storedStudent.Status);

        Assert.Empty(await fixture.Db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Approve_PersistsTheEventUnderTheContextTenant()
    {
        using var fixture = new Fixture();
        var enrollment = fixture.AddEnrollment(EnrollmentStatus.Pendente);
        var trail = new AuditTrail(fixture.Db, fixture.TenantContext, new AuditActor());
        var service = new EnrollmentService(fixture.Db, trail);

        await service.ApproveAsync(enrollment.Id, fixture.TenantId, Guid.NewGuid(), UserRole.Admin);

        var audit = Assert.Single(await fixture.Db.AuditEvents.ToListAsync());
        Assert.Equal(fixture.TenantId, audit.TenantId);
        Assert.Equal(AuditActions.EnrollmentApproved, audit.Action);
        Assert.Equal(enrollment.Id, audit.TargetId);
    }
}
