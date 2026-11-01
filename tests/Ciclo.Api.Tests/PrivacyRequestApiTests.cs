using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Ciclo.Api.Controllers;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Auth;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 210 FR-003 / FR-004 / FR-005 / NFR-001 / NFR-002 — fluxo da solicitação do titular.
/// Prova a máquina de estados (Submitted → Approved | Rejected → Completed), a recusa atômica que
/// não grava nada, o vínculo do responsável, o isolamento por tenant, a delegação de cada tipo ao
/// dono da operação e a trilha de auditoria no host real (401/403/200).
/// </summary>
[Collection(ApiTestHost.CollectionName)]
public sealed class PrivacyRequestApiTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UnlinkedStudentId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OtherTenantStudentId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid LinkedParentId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid UnlinkedParentId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid StaffId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherTenantRequestId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static readonly Guid ConsentHealthId = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");
    private static readonly Guid ConsentReportId = Guid.Parse("a2a2a2a2-a2a2-a2a2-a2a2-a2a2a2a2a2a2");
    private static readonly Guid ConsentImageId = Guid.Parse("a3a3a3a3-a3a3-a3a3-a3a3-a3a3a3a3a3a3");

    private static readonly string[] ExportSections =
    [
        "student",
        "guardians",
        "enrollments",
        "grades",
        "documents",
        "consents",
        "requests",
    ];

    private readonly ApiTestHost _host;

    public PrivacyRequestApiTests(ApiTestHost host) => _host = host;

    /// <summary>
    /// Contexto derivado com tipo próprio: o EF cacheia o modelo por tipo e o filtro global de
    /// tenant fica preso à instância que construiu o modelo (mesmo motivo de AuditTrailTests).
    /// </summary>
    private sealed class PrivacyRequestDbContext : AppDbContext
    {
        internal PrivacyRequestDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        private readonly Guid _tenantId;

        internal FixedTenantContext(Guid tenantId) => _tenantId = tenantId;

        public Guid TenantId => _tenantId;

        public bool IsResolved => true;

        public void SetTenant(Guid tenantId) => throw new NotSupportedException("tenant is fixed in tests");
    }

    private sealed class CapturingAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Record(AuditEntry entry) => Entries.Add(entry);
    }

    private static PrivacyRequestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // O provider in-memory não implementa transações: sem esta supressão a anonimização
            // delegada pela conclusão derruba o teste por limitação do dublê, não por defeito
            // da operação (a atomicidade real é do provider relacional).
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new PrivacyRequestDbContext(options, new FixedTenantContext(TestTenantId));
    }

    private static PrivacyRequestService CreateService(PrivacyRequestDbContext db, IAuditTrail audit) =>
        new(db, audit,
            new PrivacyExportService(db),
            new AnonymizationService(db, audit),
            new ConsentService(db, audit));

    /// <summary>Aluno vinculado ao responsável, aluno sem vínculo e aluno de outra escola.</summary>
    private static async Task SeedAsync(PrivacyRequestDbContext db)
    {
        var now = DateTime.UtcNow;

        db.Students.Add(new Student
        {
            Id = StudentId,
            TenantId = TestTenantId,
            Nome = "Aluno Solicitado",
            DataNascimento = new DateTime(2010, 5, 4, 0, 0, 0, DateTimeKind.Utc),
            Cpf = "12345678900",
            Turma = "3A",
            AnoLetivo = 2026,
            Observacoes = "Observação sensível",
            Foto = "data:image/png;base64,foto",
            CreatedAt = now.AddDays(-30),
            UpdatedAt = now.AddDays(-1),
        });

        db.Students.Add(new Student
        {
            Id = UnlinkedStudentId,
            TenantId = TestTenantId,
            Nome = "Aluno Sem Vínculo",
            DataNascimento = new DateTime(2011, 6, 5, 0, 0, 0, DateTimeKind.Utc),
            Turma = "4B",
            AnoLetivo = 2026,
        });

        db.Students.Add(new Student
        {
            Id = OtherTenantStudentId,
            TenantId = OtherTenantId,
            Nome = "Aluno de Outra Escola",
            DataNascimento = new DateTime(2012, 7, 6, 0, 0, 0, DateTimeKind.Utc),
            Turma = "5C",
            AnoLetivo = 2026,
        });

        db.StudentParents.Add(new StudentParent { StudentId = StudentId, ParentId = LinkedParentId });

        db.ConsentRecords.Add(new ConsentRecord
        {
            Id = ConsentHealthId,
            TenantId = TestTenantId,
            SubjectType = ConsentSubjectType.Student,
            SubjectId = StudentId,
            Purpose = "saude",
            LegalBasis = "consentimento",
            Status = ConsentStatus.Granted,
            GrantedAt = now.AddDays(-7),
            CreatedAt = now.AddDays(-7),
        });

        db.ConsentRecords.Add(new ConsentRecord
        {
            Id = ConsentReportId,
            TenantId = TestTenantId,
            SubjectType = ConsentSubjectType.Student,
            SubjectId = StudentId,
            Purpose = "boletim",
            LegalBasis = "consentimento",
            Status = ConsentStatus.Granted,
            GrantedAt = now.AddDays(-6),
            CreatedAt = now.AddDays(-6),
        });

        db.ConsentRecords.Add(new ConsentRecord
        {
            Id = ConsentImageId,
            TenantId = TestTenantId,
            SubjectType = ConsentSubjectType.Student,
            SubjectId = StudentId,
            Purpose = "imagem",
            LegalBasis = "consentimento",
            Status = ConsentStatus.Revoked,
            GrantedAt = now.AddDays(-9),
            RevokedAt = now.AddDays(-5),
            CreatedAt = now.AddDays(-9),
        });

        await db.SaveChangesAsync();
    }

    private static Task<PrivacyRequestDto> SubmitAsAsync(PrivacyRequestService service,
        CreatePrivacyRequestRequest request, Guid? requesterId = null, UserRole role = UserRole.Parent) =>
        service.SubmitAsync(TestTenantId, requesterId ?? LinkedParentId, role, request);

    private static async Task<PrivacyRequestDto> SubmitAndApproveAsync(PrivacyRequestService service,
        PrivacyRequestType type, bool legalHold = false, DateTime? retainUntil = null)
    {
        var submitted = await SubmitAsAsync(service, new CreatePrivacyRequestRequest(
            StudentId, type.ToString(), "pedido do responsável"));

        return await service.DecideAsync(TestTenantId, submitted.Id, new DecidePrivacyRequestRequest(
            "approve", "deferido", legalHold, retainUntil));
    }

    // ------------------------------------------------------------ submissão

    [Fact]
    public async Task Submit_ByLinkedGuardian_CreatesASubmittedRequestWithoutAuditEntry()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var audit = new CapturingAuditTrail();
        var service = CreateService(db, audit);

        var dto = await service.SubmitAsync(TestTenantId, LinkedParentId, UserRole.Parent,
            new CreatePrivacyRequestRequest(StudentId, "Export", "  pedido do responsável  "));

        Assert.Equal("Export", dto.Type);
        Assert.Equal("Submitted", dto.Status);
        Assert.Equal(StudentId, dto.SubjectStudentId);
        Assert.Equal(LinkedParentId, dto.RequestedByUserId);
        Assert.Equal("pedido do responsável", dto.Reason);
        Assert.False(dto.LegalHold);
        Assert.Null(dto.DecidedAt);
        Assert.Null(dto.CompletedAt);
        Assert.NotEqual(default, dto.SubmittedAt);

        var row = Assert.Single(await db.PrivacyRequests.ToListAsync());
        Assert.Equal(dto.Id, row.Id);
        Assert.Equal(TestTenantId, row.TenantId);
        Assert.Equal(PrivacyRequestStatus.Submitted, row.Status);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Submit_ByUnlinkedGuardian_ThrowsForbiddenAndPersistsNothing()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var audit = new CapturingAuditTrail();
        var service = CreateService(db, audit);

        var exception = await Assert.ThrowsAsync<PrivacyException>(
            () => service.SubmitAsync(TestTenantId, UnlinkedParentId, UserRole.Parent,
                new CreatePrivacyRequestRequest(StudentId, "Export", null)));

        Assert.Equal("forbidden", exception.Message);
        Assert.Equal(403, exception.StatusCode);
        Assert.Empty(await db.PrivacyRequests.ToListAsync());
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Submit_ByStaffForAnUnlinkedStudent_IsAllowed()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, new CapturingAuditTrail());

        var dto = await service.SubmitAsync(TestTenantId, StaffId, UserRole.Staff,
            new CreatePrivacyRequestRequest(StudentId, "Anonymization", null));

        Assert.Equal("Anonymization", dto.Type);
        Assert.Equal("Submitted", dto.Status);
        Assert.Equal(StaffId, dto.RequestedByUserId);
    }

    [Fact]
    public async Task Submit_WithAnUnknownType_ThrowsInvalidRequest()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, new CapturingAuditTrail());

        var exception = await Assert.ThrowsAsync<PrivacyException>(
            () => SubmitAsAsync(service, new CreatePrivacyRequestRequest(StudentId, "Retention", null)));

        Assert.Equal("invalid_request", exception.Message);
        Assert.Equal(400, exception.StatusCode);
        Assert.Empty(await db.PrivacyRequests.ToListAsync());
    }

    [Fact]
    public async Task Submit_ForAnUnknownStudent_ThrowsKeyNotFound()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, new CapturingAuditTrail());

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => SubmitAsAsync(service, new CreatePrivacyRequestRequest(Guid.NewGuid(), "Export", null)));

        Assert.Empty(await db.PrivacyRequests.ToListAsync());
    }

    [Fact]
    public async Task Submit_ForAStudentOfAnotherTenant_ThrowsKeyNotFound()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, new CapturingAuditTrail());

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => SubmitAsAsync(service, new CreatePrivacyRequestRequest(
                OtherTenantStudentId, "Export", null), StaffId, UserRole.Admin));

        Assert.Empty(await db.PrivacyRequests.ToListAsync());
    }

    // ------------------------------------------------------------ decisão

    [Fact]
    public async Task Decide_Approve_MovesSubmittedToApprovedAndRecordsOneAuditEntry()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var audit = new CapturingAuditTrail();
        var service = CreateService(db, audit);

        var submitted = await SubmitAsAsync(service,
            new CreatePrivacyRequestRequest(StudentId, "Export", null));
        var retainUntil = DateTime.UtcNow.AddYears(1);

        var decided = await service.DecideAsync(TestTenantId, submitted.Id,
            new DecidePrivacyRequestRequest("approve", "  deferido  ", true, retainUntil));

        Assert.Equal(submitted.Id, decided.Id);
        Assert.Equal("Approved", decided.Status);
        Assert.Equal("deferido", decided.Resolution);
        Assert.True(decided.LegalHold);
        Assert.Equal(retainUntil, decided.RetainUntil);
        Assert.NotNull(decided.DecidedAt);
        Assert.Null(decided.CompletedAt);

        var row = await db.PrivacyRequests.SingleAsync();
        Assert.Equal(PrivacyRequestStatus.Approved, row.Status);
        Assert.Equal(retainUntil, row.RetainUntil);
        Assert.NotNull(row.DecidedAt);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.PrivacyRequestDecided, entry.Action);
        Assert.Equal(AuditTargetTypes.PrivacyRequest, entry.TargetType);
        Assert.Equal(submitted.Id, entry.TargetId);
    }

    [Fact]
    public async Task Decide_Reject_MovesSubmittedToRejectedAndStampsDecidedAt()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var audit = new CapturingAuditTrail();
        var service = CreateService(db, audit);

        var submitted = await SubmitAsAsync(service,
            new CreatePrivacyRequestRequest(StudentId, "Anonymization", null));

        var decided = await service.DecideAsync(TestTenantId, submitted.Id,
            new DecidePrivacyRequestRequest("reject", "sem base legal", false, null));

        Assert.Equal("Rejected", decided.Status);
        Assert.Equal("sem base legal", decided.Resolution);
        Assert.NotNull(decided.DecidedAt);

        var row = await db.PrivacyRequests.SingleAsync();
        Assert.Equal(PrivacyRequestStatus.Rejected, row.Status);
        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task Decide_ASecondTime_ThrowsInvalidStateWithoutChangeOrAudit()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var audit = new CapturingAuditTrail();
        var service = CreateService(db, audit);

        var approved = await SubmitAndApproveAsync(service, PrivacyRequestType.Export);
        var entriesAfterFirstDecision = audit.Entries.Count;

        var exception = await Assert.ThrowsAsync<PrivacyException>(
            () => service.DecideAsync(TestTenantId, approved.Id,
                new DecidePrivacyRequestRequest("reject", "tentativa dupla", false, null)));

        Assert.Equal("invalid_state", exception.Message);
        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(entriesAfterFirstDecision, audit.Entries.Count);

        db.ChangeTracker.Clear();
        var row = await db.PrivacyRequests.SingleAsync();
        Assert.Equal(PrivacyRequestStatus.Approved, row.Status);
        Assert.Equal("deferido", row.Resolution);
        Assert.Null(row.CompletedAt);
    }

    [Fact]
    public async Task Decide_WithAnUnknownDecision_LeavesTheRowAndTheAuditUntouched()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var audit = new CapturingAuditTrail();
        var service = CreateService(db, audit);

        var submitted = await SubmitAsAsync(service,
            new CreatePrivacyRequestRequest(StudentId, "Export", null));

        var exception = await Assert.ThrowsAsync<PrivacyException>(
            () => service.DecideAsync(TestTenantId, submitted.Id,
                new DecidePrivacyRequestRequest("maybe", "indefinido", true, null)));

        Assert.Equal("invalid_request", exception.Message);
        Assert.Equal(400, exception.StatusCode);

        db.ChangeTracker.Clear();
        var row = await db.PrivacyRequests.SingleAsync();
        Assert.Equal(PrivacyRequestStatus.Submitted, row.Status);
        Assert.Null(row.DecidedAt);
        Assert.Null(row.Resolution);
        Assert.False(row.LegalHold);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Decide_RequestOfAnotherTenant_ThrowsKeyNotFound()
    {
        using var db = CreateContext();
        await SeedAsync(db);

        db.PrivacyRequests.Add(new PrivacyRequest
        {
            Id = Guid.NewGuid(),
            TenantId = OtherTenantId,
            Type = PrivacyRequestType.Export,
            Status = PrivacyRequestStatus.Submitted,
            SubjectStudentId = OtherTenantStudentId,
            RequestedByUserId = Guid.NewGuid(),
            SubmittedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var otherTenantRequestId = (await db.PrivacyRequests.IgnoreQueryFilters().SingleAsync()).Id;
        var audit = new CapturingAuditTrail();
        var service = CreateService(db, audit);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.DecideAsync(TestTenantId, otherTenantRequestId,
                new DecidePrivacyRequestRequest("approve", null, false, null)));

        Assert.Empty(audit.Entries);
    }

    // ------------------------------------------------------------ conclusão

    [Fact]
    public async Task Complete_OnARequestThatIsNotApproved_ThrowsInvalidStateAndChangesNothing()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var audit = new CapturingAuditTrail();
        var service = CreateService(db, audit);

        var submitted = await SubmitAsAsync(service,
            new CreatePrivacyRequestRequest(StudentId, "Export", null));

        var exception = await Assert.ThrowsAsync<PrivacyException>(
            () => service.CompleteAsync(TestTenantId, submitted.Id));

        Assert.Equal("invalid_state", exception.Message);
        Assert.Equal(409, exception.StatusCode);

        db.ChangeTracker.Clear();
        var row = await db.PrivacyRequests.SingleAsync();
        Assert.Equal(PrivacyRequestStatus.Submitted, row.Status);
        Assert.Null(row.CompletedAt);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Complete_ApprovedExport_ReturnsTheDocumentAndRecordsTheDecidedAction()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var audit = new CapturingAuditTrail();
        var service = CreateService(db, audit);

        var approved = await SubmitAndApproveAsync(service, PrivacyRequestType.Export);

        var completion = await service.CompleteAsync(TestTenantId, approved.Id);

        Assert.Equal("Completed", completion.Request.Status);
        Assert.NotNull(completion.Request.CompletedAt);

        var document = Assert.IsType<PrivacyExportDto>(completion.Export);
        Assert.Equal(StudentId, document.Student.Id);
        Assert.Equal("Aluno Solicitado", document.Student.Nome);
        Assert.NotNull(document.Guardians);
        Assert.NotNull(document.Enrollments);
        Assert.NotNull(document.Grades);
        Assert.NotNull(document.Documents);
        Assert.Equal(3, document.Consents.Count);

        db.ChangeTracker.Clear();
        var row = await db.PrivacyRequests.SingleAsync();
        Assert.Equal(PrivacyRequestStatus.Completed, row.Status);
        Assert.NotNull(row.CompletedAt);

        // A decisão e a conclusão gravam a mesma ação; a entrada da conclusão é a que carrega o
        // estado Completed e o instante de conclusão.
        var completionEntry = Assert.Single(audit.Entries,
            e => e.Action == AuditActions.PrivacyRequestDecided
                && e.After is PrivacyRequestDto { Status: "Completed" });
        Assert.Equal(AuditTargetTypes.PrivacyRequest, completionEntry.TargetType);
        Assert.Equal(approved.Id, completionEntry.TargetId);
    }

    [Fact]
    public async Task Complete_ApprovedAnonymization_AnonymizesAndKeepsTheRetentionDeadline()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var audit = new CapturingAuditTrail();
        var service = CreateService(db, audit);

        var retainUntil = DateTime.UtcNow.AddYears(2);
        var approved = await SubmitAndApproveAsync(
            service, PrivacyRequestType.Anonymization, retainUntil: retainUntil);

        var completion = await service.CompleteAsync(TestTenantId, approved.Id);

        Assert.Equal("Completed", completion.Request.Status);
        Assert.NotNull(completion.Request.CompletedAt);
        Assert.Null(completion.Export);

        db.ChangeTracker.Clear();
        var student = await db.Students.SingleAsync(s => s.Id == StudentId);
        Assert.Equal("Titular anonimizado", student.Nome);
        Assert.Null(student.Cpf);
        Assert.Null(student.Foto);
        Assert.Null(student.Observacoes);
        Assert.Equal(new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc), student.DataNascimento);
        Assert.NotNull(student.AnonymizedAt);

        var row = await db.PrivacyRequests.SingleAsync();
        Assert.Equal(PrivacyRequestStatus.Completed, row.Status);
        Assert.Equal(retainUntil, row.RetainUntil);

        Assert.Single(audit.Entries, e => e.Action == AuditActions.PrivacyAnonymized);
        Assert.Single(audit.Entries,
            e => e.Action == AuditActions.PrivacyRequestDecided
                && e.After is PrivacyRequestDto { Status: "Completed" });
    }

    [Fact]
    public async Task Complete_AnonymizationWithLegalHold_RefusesAndKeepsTheRequestApproved()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var audit = new CapturingAuditTrail();
        var service = CreateService(db, audit);

        var approved = await SubmitAndApproveAsync(
            service, PrivacyRequestType.Anonymization, legalHold: true);
        var entriesAfterDecision = audit.Entries.Count;

        var exception = await Assert.ThrowsAsync<PrivacyLegalHoldException>(
            () => service.CompleteAsync(TestTenantId, approved.Id));

        Assert.Equal("legal_hold_active", exception.Message);
        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(entriesAfterDecision, audit.Entries.Count);

        db.ChangeTracker.Clear();
        var row = await db.PrivacyRequests.SingleAsync();
        Assert.Equal(PrivacyRequestStatus.Approved, row.Status);
        Assert.Null(row.CompletedAt);

        var student = await db.Students.SingleAsync(s => s.Id == StudentId);
        Assert.Equal("Aluno Solicitado", student.Nome);
        Assert.Null(student.AnonymizedAt);
    }

    [Fact]
    public async Task Complete_ApprovedConsentRevocation_RevokesTheGrantedConsents()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var audit = new CapturingAuditTrail();
        var service = CreateService(db, audit);

        var approved = await SubmitAndApproveAsync(service, PrivacyRequestType.ConsentRevocation);

        var completion = await service.CompleteAsync(TestTenantId, approved.Id);

        Assert.Equal("Completed", completion.Request.Status);
        Assert.Null(completion.Export);

        db.ChangeTracker.Clear();
        var consents = await db.ConsentRecords.OrderBy(c => c.Purpose).ToListAsync();
        Assert.All(consents, consent => Assert.Equal(ConsentStatus.Revoked, consent.Status));
        Assert.All(consents, consent => Assert.NotNull(consent.RevokedAt));

        // Duas concessões ativas viram revogação; a já revogada é no-op e não gera entrada.
        Assert.Equal(2, audit.Entries.Count(e => e.Action == AuditActions.ConsentRevoked));
        Assert.Single(audit.Entries,
            e => e.Action == AuditActions.PrivacyRequestDecided
                && e.After is PrivacyRequestDto { Status: "Completed" });
    }

    // ------------------------------------------------------------ listagem

    [Fact]
    public async Task List_IsTenantScopedAndTheGuardianSeesOnlyLinkedStudents()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, new CapturingAuditTrail());

        var linked = await SubmitAsAsync(service,
            new CreatePrivacyRequestRequest(StudentId, "Export", null));

        var staffVisible = await service.SubmitAsync(TestTenantId, StaffId, UserRole.Staff,
            new CreatePrivacyRequestRequest(UnlinkedStudentId, "Export", null));

        var otherTenantRequestId = Guid.NewGuid();
        db.PrivacyRequests.Add(new PrivacyRequest
        {
            Id = otherTenantRequestId,
            TenantId = OtherTenantId,
            Type = PrivacyRequestType.Export,
            Status = PrivacyRequestStatus.Submitted,
            SubjectStudentId = OtherTenantStudentId,
            RequestedByUserId = Guid.NewGuid(),
            SubmittedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var guardianRows = await service.ListAsync(TestTenantId, LinkedParentId, UserRole.Parent);
        Assert.Equal([linked.Id], guardianRows.Select(r => r.Id).ToArray());

        var staffRows = await service.ListAsync(TestTenantId, StaffId, UserRole.Staff);
        Assert.Equal(2, staffRows.Count);
        Assert.Contains(staffRows, r => r.Id == linked.Id);
        Assert.Contains(staffRows, r => r.Id == staffVisible.Id);
        Assert.DoesNotContain(staffRows, r => r.Id == otherTenantRequestId);
    }

    // ------------------------------------------------------------ borda HTTP

    [Fact]
    public void EveryPrivacyEndpoint_RequiresAuthenticationAndNoneIsAnonymous()
    {
        var endpoints = EndpointInventory.All()
            .Where(e => e.Route.StartsWith("api/privacy", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(8, endpoints.Count);
        Assert.All(endpoints, endpoint =>
        {
            Assert.True(endpoint.RequiresAuthentication, $"{endpoint.Route} não exige autenticação.");
            Assert.False(endpoint.AllowsAnonymous, $"{endpoint.Route} está público por decisão.");
        });
    }

    [Fact]
    public async Task PrivacyRequestsRoute_WithoutAToken_Returns401()
    {
        using var client = _host.CreateClient();

        using var response = await client.GetAsync("/api/privacy/requests");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminOnlyRoutes_WithAGuardianToken_Return403()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), UserRole.Parent);
        var studentId = Guid.NewGuid();

        using var decision = await client.PostAsJsonAsync(
            $"/api/privacy/requests/{Guid.NewGuid()}/decision", new { decision = "approve" });
        using var completion = await client.PostAsync(
            $"/api/privacy/requests/{Guid.NewGuid()}/complete", content: null);
        using var export = await client.GetAsync($"/api/privacy/students/{studentId}/export");
        using var consent = await client.PostAsJsonAsync("/api/privacy/consents", new
        {
            subjectId = studentId,
            subjectType = "Student",
            purpose = "saude",
            legalBasis = "consentimento",
        });

        Assert.Equal(HttpStatusCode.Forbidden, decision.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, completion.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, consent.StatusCode);
    }

    [Fact]
    public async Task AdminToken_ListRoute_Returns200WithAnArrayAndNoOtherTenantRow()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), UserRole.Admin);

        using var list = await client.GetAsync("/api/privacy/requests");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var rows = await list.Content.ReadFromJsonAsync<List<PrivacyRequestDto>>();
        Assert.NotNull(rows);
        Assert.Empty(rows!);
    }

    /// <summary>
    /// Fluxo completo com identidade de administrador: submissão, listagem, decisão, conclusão,
    /// download do documento e consentimentos. Aciona as ações do controller sobre banco e
    /// serviços reais; a autorização por papel é coberta pelos testes de borda HTTP desta classe.
    /// </summary>
    [Fact]
    public async Task AdminIdentity_ListDecideCompleteExportAndConsentsSucceed()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        var audit = new CapturingAuditTrail();

        // Linha de outra escola: nunca aparece na listagem do chamador.
        db.PrivacyRequests.Add(new PrivacyRequest
        {
            Id = OtherTenantRequestId,
            TenantId = OtherTenantId,
            Type = PrivacyRequestType.Export,
            Status = PrivacyRequestStatus.Submitted,
            SubjectStudentId = OtherTenantStudentId,
            RequestedByUserId = Guid.NewGuid(),
            SubmittedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        // Submissão pelo responsável vinculado.
        var guardian = CreateController(db, audit, TestTenantId, LinkedParentId, UserRole.Parent);
        var submitted = Assert.IsType<ObjectResult>(await guardian.Submit(
            new CreatePrivacyRequestRequest(StudentId, "Export", "pedido do responsável"), default));
        Assert.Equal(StatusCodes.Status201Created, submitted.StatusCode);
        var request = Assert.IsType<PrivacyRequestDto>(submitted.Value);
        Assert.Equal("Submitted", request.Status);

        // Listagem: somente as linhas do tenant do chamador.
        var admin = CreateController(db, audit, TestTenantId, StaffId, UserRole.Admin);
        var listed = Assert.IsType<OkObjectResult>(await admin.List(default));
        var rows = Assert.IsAssignableFrom<IReadOnlyList<PrivacyRequestDto>>(listed.Value);
        Assert.Equal([request.Id], rows.Select(r => r.Id).ToArray());

        // Decisão: Submitted → Approved.
        var decision = Assert.IsType<OkObjectResult>(await admin.Decide(request.Id,
            new DecidePrivacyRequestRequest("approve", "deferido", false, null), default));
        var decided = Assert.IsType<PrivacyRequestDto>(decision.Value);
        Assert.Equal("Approved", decided.Status);
        Assert.NotNull(decided.DecidedAt);

        // Conclusão: Approved → Completed, com o documento do titular.
        var completion = Assert.IsType<OkObjectResult>(await admin.Complete(request.Id, default));
        var completed = Assert.IsType<PrivacyRequestCompletionDto>(completion.Value);
        Assert.Equal("Completed", completed.Request.Status);
        Assert.NotNull(completed.Request.CompletedAt);
        Assert.NotNull(completed.Export);
        Assert.Equal(StudentId, completed.Export!.Student.Id);

        // Download do documento do titular: JSON, nome determinístico e sete seções.
        var file = Assert.IsType<FileStreamResult>(await admin.Export(StudentId, default));
        Assert.Equal("application/json", file.ContentType);
        Assert.Equal($"privacy-export-{StudentId}.json", file.FileDownloadName);

        using var json = await JsonDocument.ParseAsync(file.FileStream);
        foreach (var section in ExportSections)
            Assert.True(json.RootElement.TryGetProperty(section, out _), $"seção {section} ausente");
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("guardians").ValueKind);

        // Consentimentos do titular continuam legíveis e revogáveis pela escola.
        var consents = Assert.IsType<OkObjectResult>(await admin.ListConsents(StudentId, "Student", default));
        var consentRows = Assert.IsAssignableFrom<IReadOnlyList<ConsentDto>>(consents.Value);
        Assert.Equal(3, consentRows.Count);

        var revoked = Assert.IsType<OkObjectResult>(await admin.RevokeConsent(ConsentHealthId, default));
        Assert.Equal("Revoked", Assert.IsType<ConsentDto>(revoked.Value).Status);

        var granted = Assert.IsType<OkObjectResult>(await admin.GrantConsent(
            new GrantConsentRequest(StudentId, "Student", "transporte", "consentimento"), default));
        Assert.Equal("Granted", Assert.IsType<ConsentDto>(granted.Value).Status);
    }

    [Fact]
    public async Task AdminOnlyRoutes_WithAnAdminTokenAndUnknownIds_Return404()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), UserRole.Admin);

        using var decision = await client.PostAsJsonAsync(
            $"/api/privacy/requests/{Guid.NewGuid()}/decision", new { decision = "approve" });
        using var completion = await client.PostAsync(
            $"/api/privacy/requests/{Guid.NewGuid()}/complete", content: null);
        using var export = await client.GetAsync($"/api/privacy/students/{Guid.NewGuid()}/export");

        Assert.Equal(HttpStatusCode.NotFound, decision.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, completion.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, export.StatusCode);
    }

    // ------------------------------------------------------------ helpers

    /// <summary>
    /// Controller com banco e serviços reais e a identidade do chamador nas claims que o
    /// controller lê (<c>NameIdentifier</c>, <c>Role</c> e <c>tenant_id</c>), como o token emite.
    /// </summary>
    private static PrivacyController CreateController(PrivacyRequestDbContext db, IAuditTrail audit,
        Guid tenantId, Guid userId, UserRole role)
    {
        var controller = new PrivacyController(
            new ConsentService(db, audit),
            CreateService(db, audit),
            new PrivacyExportService(db));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Role, role.ToString()),
                    new Claim("tenant_id", tenantId.ToString()),
                ], authenticationType: "Test")),
            },
        };

        return controller;
    }

    private HttpClient AuthenticatedClient(Guid tenantId, UserRole role)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(tenantId, role));
        return client;
    }

    private static string Token(Guid tenantId, UserRole role)
    {
        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c["Jwt:Secret"]).Returns(ApiTestHost.JwtSecret);
        configuration.Setup(c => c["Jwt:Issuer"]).Returns("Ciclo");
        configuration.Setup(c => c["Jwt:Audience"]).Returns("Ciclo");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "privacy-test@escola.com",
            Name = "Privacy Test",
            TenantId = tenantId,
            Role = role,
        };

        return new JwtTokenGenerator(configuration.Object).GenerateAccessToken(user);
    }
}
