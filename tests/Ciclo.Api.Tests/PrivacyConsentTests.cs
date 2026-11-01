using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 210 FR-001 — consentimento por titular e finalidade. Prova a linha única por
/// (TenantId, SubjectType, SubjectId, Purpose), a revogação que preserva a concessão e o
/// caminho no-op que não grava nem audita uma segunda vez.
/// </summary>
public sealed class PrivacyConsentTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GuardianId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ActorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// Contexto derivado com tipo próprio: o EF cacheia o modelo por tipo e o filtro global de
    /// tenant fica preso à instância que construiu o modelo (mesmo motivo de AuditTrailTests).
    /// </summary>
    private sealed class PrivacyConsentDbContext : AppDbContext
    {
        internal PrivacyConsentDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
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

    private static PrivacyConsentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PrivacyConsentDbContext(options, new FixedTenantContext(TestTenantId));
    }

    private static void SeedSubjects(PrivacyConsentDbContext db)
    {
        db.Students.Add(new Student
        {
            Id = StudentId,
            TenantId = TestTenantId,
            Nome = "Aluno Teste",
            DataNascimento = new DateTime(2012, 3, 4, 0, 0, 0, DateTimeKind.Utc),
            Turma = "5A",
            AnoLetivo = 2026,
        });

        db.Users.Add(new User
        {
            Id = GuardianId,
            TenantId = TestTenantId,
            Name = "Responsável Teste",
            Email = "responsavel@example.com",
            Role = UserRole.Parent,
            IsActive = true,
        });

        db.SaveChanges();
    }

    [Fact]
    public async Task Grant_SameSubjectAndPurpose_ReusesTheSingleExistingRow()
    {
        using var db = CreateContext();
        SeedSubjects(db);
        var audit = new CapturingAuditTrail();
        var service = new ConsentService(db, audit);

        var first = await service.GrantAsync(
            TestTenantId, ConsentSubjectType.Student, StudentId, "saude", "consentimento", ActorId);
        var second = await service.GrantAsync(
            TestTenantId, ConsentSubjectType.Student, StudentId, "  saude  ", "obrigacao_legal", null);

        var row = Assert.Single(await db.ConsentRecords.ToListAsync());
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Id, row.Id);
        Assert.Equal(ConsentStatus.Granted, row.Status);
        Assert.Null(row.RevokedAt);
        Assert.Equal("obrigacao_legal", row.LegalBasis);
        Assert.Equal(first.GrantedAt, row.GrantedAt);
        Assert.Equal(first.GrantedAt, second.GrantedAt);
        Assert.Equal(2, audit.Entries.Count);
    }

    [Fact]
    public async Task Grant_AfterRevocation_ReopensTheSameRowWithAClearedRevokedAt()
    {
        using var db = CreateContext();
        SeedSubjects(db);
        var service = new ConsentService(db, new CapturingAuditTrail());

        var granted = await service.GrantAsync(
            TestTenantId, ConsentSubjectType.Student, StudentId, "imagem", "consentimento", ActorId);
        var revoked = await service.RevokeAsync(TestTenantId, granted.Id);
        Assert.NotNull(revoked);

        var regranted = await service.GrantAsync(
            TestTenantId, ConsentSubjectType.Student, StudentId, "imagem", "obrigacao_legal", ActorId);

        var row = Assert.Single(await db.ConsentRecords.ToListAsync());
        Assert.Equal(granted.Id, row.Id);
        Assert.Equal(ConsentStatus.Granted, row.Status);
        Assert.Null(row.RevokedAt);
        Assert.Equal("obrigacao_legal", row.LegalBasis);
        Assert.Equal(granted.GrantedAt, row.GrantedAt);
        Assert.Equal(granted.GrantedAt, regranted.GrantedAt);
    }

    [Fact]
    public async Task Revoke_StampsStatusAndRevokedAtAndPreservesTheOriginalGrant()
    {
        using var db = CreateContext();
        SeedSubjects(db);
        var service = new ConsentService(db, new CapturingAuditTrail());

        var granted = await service.GrantAsync(
            TestTenantId, ConsentSubjectType.Student, StudentId, "saude", "consentimento", ActorId);

        var revoked = await service.RevokeAsync(TestTenantId, granted.Id);

        var row = await db.ConsentRecords.SingleAsync();
        Assert.NotNull(revoked);
        Assert.Equal(ConsentStatus.Revoked, row.Status);
        Assert.NotNull(row.RevokedAt);
        Assert.Equal(granted.GrantedAt, row.GrantedAt);
        Assert.Equal("consentimento", row.LegalBasis);
        Assert.Equal(row.RevokedAt, revoked!.RevokedAt);
        Assert.Equal("Revoked", revoked.Status);
    }

    [Fact]
    public async Task Revoke_AlreadyRevoked_ChangesNothingAndWritesNoSecondAuditEntry()
    {
        using var db = CreateContext();
        SeedSubjects(db);
        var audit = new CapturingAuditTrail();
        var service = new ConsentService(db, audit);

        var granted = await service.GrantAsync(
            TestTenantId, ConsentSubjectType.Student, StudentId, "saude", "consentimento", ActorId);
        var firstRevocation = await service.RevokeAsync(TestTenantId, granted.Id);

        var entriesAfterFirstRevocation = audit.Entries.Count;
        var revokedAt = (await db.ConsentRecords.SingleAsync()).RevokedAt;

        var secondRevocation = await service.RevokeAsync(TestTenantId, granted.Id);

        var row = await db.ConsentRecords.SingleAsync();
        Assert.NotNull(secondRevocation);
        Assert.Equal(entriesAfterFirstRevocation, audit.Entries.Count);
        Assert.Equal(revokedAt, row.RevokedAt);
        Assert.Equal(firstRevocation!.RevokedAt, secondRevocation.RevokedAt);
        Assert.Equal(ConsentStatus.Revoked, row.Status);
    }

    [Fact]
    public async Task Revoke_UnknownConsent_ReturnsNullAndWritesNothing()
    {
        using var db = CreateContext();
        SeedSubjects(db);
        var audit = new CapturingAuditTrail();
        var service = new ConsentService(db, audit);

        var result = await service.RevokeAsync(TestTenantId, Guid.NewGuid());

        Assert.Null(result);
        Assert.Empty(audit.Entries);
        Assert.Empty(await db.ConsentRecords.ToListAsync());
    }

    [Fact]
    public async Task Grant_EmptyPurpose_ThrowsInvalidRequestAndPersistsNothing()
    {
        using var db = CreateContext();
        SeedSubjects(db);
        var audit = new CapturingAuditTrail();
        var service = new ConsentService(db, audit);

        var exception = await Assert.ThrowsAsync<PrivacyException>(() => service.GrantAsync(
            TestTenantId, ConsentSubjectType.Student, StudentId, "   ", "consentimento", ActorId));

        Assert.Equal("invalid_request", exception.Message);
        Assert.Equal(400, exception.StatusCode);
        Assert.Empty(await db.ConsentRecords.ToListAsync());
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Grant_UnknownSubject_ThrowsKeyNotFoundAndPersistsNothing()
    {
        using var db = CreateContext();
        SeedSubjects(db);
        var audit = new CapturingAuditTrail();
        var service = new ConsentService(db, audit);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GrantAsync(
            TestTenantId, ConsentSubjectType.Student, Guid.NewGuid(), "saude", "consentimento", ActorId));

        Assert.Empty(await db.ConsentRecords.ToListAsync());
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task GrantsAndRevocations_RecordOneAuditEntryEachWithTheSubjectTarget()
    {
        using var db = CreateContext();
        SeedSubjects(db);
        var audit = new CapturingAuditTrail();
        var service = new ConsentService(db, audit);

        var granted = await service.GrantAsync(
            TestTenantId, ConsentSubjectType.Student, StudentId, "saude", "consentimento", ActorId);
        await service.RevokeAsync(TestTenantId, granted.Id);
        await service.GrantAsync(
            TestTenantId, ConsentSubjectType.Guardian, GuardianId, "comunicacoes", "consentimento", ActorId);

        Assert.Equal(3, audit.Entries.Count);

        Assert.Equal(AuditActions.ConsentGranted, audit.Entries[0].Action);
        Assert.Equal(AuditTargetTypes.Student, audit.Entries[0].TargetType);
        Assert.Equal(StudentId, audit.Entries[0].TargetId);

        Assert.Equal(AuditActions.ConsentRevoked, audit.Entries[1].Action);
        Assert.Equal(AuditTargetTypes.Student, audit.Entries[1].TargetType);
        Assert.Equal(StudentId, audit.Entries[1].TargetId);

        Assert.Equal(AuditActions.ConsentGranted, audit.Entries[2].Action);
        Assert.Equal("Guardian", audit.Entries[2].TargetType);
        Assert.Equal(GuardianId, audit.Entries[2].TargetId);
    }

    [Fact]
    public async Task ListBySubject_ReturnsOnlyTheSubjectRowsOrderedByPurpose()
    {
        using var db = CreateContext();
        SeedSubjects(db);
        var service = new ConsentService(db, new CapturingAuditTrail());

        await service.GrantAsync(
            TestTenantId, ConsentSubjectType.Student, StudentId, "saude", "consentimento", ActorId);
        await service.GrantAsync(
            TestTenantId, ConsentSubjectType.Student, StudentId, "boletim", "consentimento", ActorId);
        await service.GrantAsync(
            TestTenantId, ConsentSubjectType.Guardian, GuardianId, "comunicacoes", "consentimento", ActorId);

        var consents = await service.ListBySubjectAsync(
            TestTenantId, ConsentSubjectType.Student, StudentId);

        Assert.Equal(2, consents.Count);
        Assert.Equal("boletim", consents[0].Purpose);
        Assert.Equal("saude", consents[1].Purpose);
        Assert.All(consents, c =>
        {
            Assert.Equal(StudentId, c.SubjectId);
            Assert.Equal("Student", c.SubjectType);
            Assert.Equal("Granted", c.Status);
        });
    }
}
