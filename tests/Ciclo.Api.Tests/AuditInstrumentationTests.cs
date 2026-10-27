using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Auditing;
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
/// Spec 110 — cobertura por operação sensível: cada transição registra exatamente um evento com
/// ação, alvo e snapshots corretos, e caminhos que falham antes da mutação não registram nada.
/// </summary>
public sealed class AuditInstrumentationTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class AuditInstrumentationDbContext : AppDbContext
    {
        internal AuditInstrumentationDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId => TestTenantId;
        public bool IsResolved => true;
        public void SetTenant(Guid tenantId) => throw new NotSupportedException("tenant is fixed in tests");
    }

    private sealed class UnresolvedTenantContext : ITenantContext
    {
        public Guid TenantId => throw new TenantNotResolvedException("tenant_not_resolved");
        public bool IsResolved => false;
        public void SetTenant(Guid tenantId) { }
    }

    private sealed class Fixture : IDisposable
    {
        internal AuditInstrumentationDbContext Db { get; }
        internal AuditTrail Trail { get; }
        internal Tenant Tenant { get; }
        internal Student Student { get; }
        internal EnrollmentPeriod Period { get; }

        internal Fixture()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var tenantContext = new FixedTenantContext();
            Db = new AuditInstrumentationDbContext(options, tenantContext);

            var actor = new AuditActor();
            actor.SetCorrelationId("corr-tests-0001");
            actor.Capture(Guid.NewGuid(), "Admin", "127.0.0.1", "audit-tests");
            Trail = new AuditTrail(Db, tenantContext, actor);

            Tenant = new Tenant { Id = TestTenantId, Name = "Escola", Slug = "escola", Country = "BR" };
            Student = new Student
            {
                Id = Guid.NewGuid(),
                TenantId = TestTenantId,
                Nome = "Aluno Teste",
                Turma = "1A",
                AnoLetivo = 2026,
                Status = StudentStatus.Ativo
            };
            Period = new EnrollmentPeriod
            {
                Id = Guid.NewGuid(),
                TenantId = TestTenantId,
                Nome = "2026",
                AnoLetivo = 2026,
                DataInicio = DateTime.UtcNow.AddMonths(-1),
                DataFim = DateTime.UtcNow.AddMonths(1)
            };

            Db.Tenants.Add(Tenant);
            Db.Students.Add(Student);
            Db.EnrollmentPeriods.Add(Period);
            Db.SaveChanges();
        }

        internal Enrollment AddEnrollment(EnrollmentStatus status)
        {
            var enrollment = new Enrollment
            {
                Id = Guid.NewGuid(),
                TenantId = TestTenantId,
                StudentId = Student.Id,
                EnrollmentPeriodId = Period.Id,
                Status = status
            };
            Db.Enrollments.Add(enrollment);
            Db.SaveChanges();
            return enrollment;
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task Approve_RecordsExactlyOneEnrollmentApprovedEvent()
    {
        using var fixture = new Fixture();
        var enrollment = fixture.AddEnrollment(EnrollmentStatus.Pendente);
        var service = new EnrollmentService(fixture.Db, fixture.Trail);

        await service.ApproveAsync(enrollment.Id, TestTenantId);

        var audit = Assert.Single(await fixture.Db.AuditEvents.ToListAsync());
        Assert.Equal(AuditActions.EnrollmentApproved, audit.Action);
        Assert.Equal(AuditTargetTypes.Enrollment, audit.TargetType);
        Assert.Equal(enrollment.Id, audit.TargetId);
        Assert.Contains("Pendente", audit.BeforeJson, StringComparison.Ordinal);
        Assert.Contains("Aprovado", audit.AfterJson, StringComparison.Ordinal);
        Assert.Equal("corr-tests-0001", audit.CorrelationId);
    }

    [Fact]
    public async Task Reject_RecordsTheReasonInTheEvent()
    {
        using var fixture = new Fixture();
        var enrollment = fixture.AddEnrollment(EnrollmentStatus.Pendente);
        var service = new EnrollmentService(fixture.Db, fixture.Trail);

        await service.RejectAsync(enrollment.Id, new RejectEnrollmentRequest("documento ilegivel"), TestTenantId);

        var audit = Assert.Single(await fixture.Db.AuditEvents.ToListAsync());
        Assert.Equal(AuditActions.EnrollmentRejected, audit.Action);
        Assert.Contains("documento ilegivel", audit.AfterJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancel_RecordsTheCancelledStatus()
    {
        using var fixture = new Fixture();
        var enrollment = fixture.AddEnrollment(EnrollmentStatus.Pendente);
        var service = new EnrollmentService(fixture.Db, fixture.Trail);

        await service.CancelAsync(enrollment.Id, TestTenantId);

        var audit = Assert.Single(await fixture.Db.AuditEvents.ToListAsync());
        Assert.Equal(AuditActions.EnrollmentCancelled, audit.Action);
        Assert.Contains("Cancelado", audit.AfterJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approve_WithAMissingRequiredDocument_DoesNotRecordAnEvent()
    {
        using var fixture = new Fixture();
        var enrollment = fixture.AddEnrollment(EnrollmentStatus.Pendente);
        fixture.Db.DocumentTypes.Add(new DocumentType
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            Nome = "RG",
            IsRequired = true,
            IsActive = true
        });
        await fixture.Db.SaveChangesAsync();

        var service = new EnrollmentService(fixture.Db, fixture.Trail);

        await Assert.ThrowsAsync<EnrollmentException>(() => service.ApproveAsync(enrollment.Id, TestTenantId));
        Assert.Empty(await fixture.Db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task VerifyApproved_RecordsOneDocumentVerifiedEvent()
    {
        using var fixture = new Fixture();
        var document = AddDocument(fixture);
        var service = CreateDocumentService(fixture);

        await service.VerifyAsync(document.Id, new VerifyDocumentRequest(true, null), TestTenantId);

        var audit = Assert.Single(await fixture.Db.AuditEvents.ToListAsync());
        Assert.Equal(AuditActions.DocumentVerified, audit.Action);
        Assert.Equal(AuditTargetTypes.Document, audit.TargetType);
        Assert.Equal(document.Id, audit.TargetId);
        Assert.Contains("Pendente", audit.BeforeJson, StringComparison.Ordinal);
        Assert.Contains("Aprovado", audit.AfterJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyRejected_RecordsTheReasonInTheEvent()
    {
        using var fixture = new Fixture();
        var document = AddDocument(fixture);
        var service = CreateDocumentService(fixture);

        await service.VerifyAsync(
            document.Id, new VerifyDocumentRequest(false, "documento vencido"), TestTenantId);

        var audit = Assert.Single(await fixture.Db.AuditEvents.ToListAsync());
        Assert.Equal(AuditActions.DocumentRejected, audit.Action);
        Assert.Contains("documento vencido", audit.AfterJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_WithAnInvalidReason_DoesNotRecordAnEvent()
    {
        using var fixture = new Fixture();
        var document = AddDocument(fixture);
        var service = CreateDocumentService(fixture);

        await Assert.ThrowsAsync<DocumentException>(() => service.VerifyAsync(
            document.Id, new VerifyDocumentRequest(false, "curto"), TestTenantId));

        Assert.Empty(await fixture.Db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Revoke_RecordsOneSessionRevokedEventPerRevokedToken()
    {
        using var fixture = new Fixture();
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Token = "token-to-revoke",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        fixture.Db.RefreshTokens.Add(token);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.Users.Add(new User
        {
            Id = token.UserId,
            UserName = "parent@example.com",
            Email = "parent@example.com",
            Name = "Parent",
            TenantId = TestTenantId,
            Role = UserRole.Parent
        });
        await fixture.Db.SaveChangesAsync();

        var userStore = new Mock<IUserStore<User>>();
        var userManager = new Mock<UserManager<User>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var service = new AuthService(
            userManager.Object,
            new Mock<Ciclo.Infrastructure.Auth.IJwtTokenGenerator>().Object,
            fixture.Db,
            new FixedTenantContext(),
            NullLogger<AuthService>.Instance,
            fixture.Trail);

        await service.RevokeTokenAsync("token-to-revoke");

        var audit = Assert.Single(await fixture.Db.AuditEvents.ToListAsync());
        Assert.Equal(AuditActions.SessionRevoked, audit.Action);
        Assert.Equal(AuditTargetTypes.Session, audit.TargetType);
        Assert.Equal(token.Id, audit.TargetId);
        Assert.Contains("revokedAt", audit.AfterJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyRejected_WhenTheTrailFails_DoesNotPersistTheStatusChange()
    {
        using var fixture = new Fixture();
        var document = AddDocument(fixture);
        var failingTrail = new AuditTrail(
            fixture.Db, new UnresolvedTenantContext(), new AuditActor());
        var service = CreateDocumentService(fixture, failingTrail);

        await Assert.ThrowsAsync<TenantNotResolvedException>(() => service.VerifyAsync(
            document.Id, new VerifyDocumentRequest(false, "documento vencido"), TestTenantId));

        fixture.Db.ChangeTracker.Clear();
        var stored = await fixture.Db.Documents.SingleAsync(d => d.Id == document.Id);

        Assert.Equal(DocumentStatus.Pendente, stored.Status);
        Assert.Empty(await fixture.Db.AuditEvents.ToListAsync());
    }

    private static Document AddDocument(Fixture fixture)
    {
        var documentType = new DocumentType
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            Nome = "RG",
            IsRequired = true,
            IsActive = true,
            ValidadeMeses = 0
        };
        var document = new Document
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            StudentId = fixture.Student.Id,
            DocumentTypeId = documentType.Id,
            NomeArquivo = "rg.pdf",
            CaminhoArquivo = "rg.pdf",
            Status = DocumentStatus.Pendente
        };

        fixture.Db.DocumentTypes.Add(documentType);
        fixture.Db.Documents.Add(document);
        fixture.Db.SaveChanges();
        return document;
    }

    private static DocumentService CreateDocumentService(Fixture fixture) =>
        CreateDocumentService(fixture, fixture.Trail);

    private static DocumentService CreateDocumentService(Fixture fixture, IAuditTrail trail)
    {
        var notification = new Mock<INotificationService>();
        notification
            .Setup(n => n.SendByStudentAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NotificationType>(),
                It.IsAny<Guid?>(), It.IsAny<Guid>()))
            // O NotificationService real grava internamente (SaveChangesAsync no fim do envio).
            // O dublê reproduz essa gravação; sem isso o risco de atomicidade da rejeição fica
            // invisível para o teste.
            .Returns(async () =>
            {
                await fixture.Db.SaveChangesAsync();
                return 0;
            });

        return new DocumentService(
            fixture.Db,
            new Mock<IFileStorage>().Object,
            notification.Object,
            trail);
    }
}
