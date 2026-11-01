using System.Text.Json;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 210 FR-003 / FR-004 / NFR-001 — anonimização do aluno. Prova a sobrescrita
/// irreversível dos campos identificadores, a integridade referencial preservada, a recusa por
/// bloqueio legal sem qualquer escrita, a idempotência e a entrada de auditoria restrita a um
/// marcador (AD-002).
/// </summary>
public sealed class PrivacyAnonymizationTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GuardianId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherTenantStudentId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static readonly Guid AcademicYearId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SchoolClassId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EnrollmentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid AssessmentId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid GradeId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid DocumentId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private const string OriginalName = "Aluno Original";
    private const string OriginalCpf = "12345678900";
    private const string OriginalPhoto = "data:image/png;base64,foto-original";
    private const string OriginalNotes = "Observação original do prontuário";
    private static readonly DateTime OriginalBirthDate =
        new(2010, 5, 4, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime AnonymousBirthDate =
        new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Contexto derivado com tipo próprio: o EF cacheia o modelo por tipo e o filtro global de
    /// tenant fica preso à instância que construiu o modelo (mesmo motivo de AuditTrailTests).
    /// </summary>
    private sealed class PrivacyAnonymizationDbContext : AppDbContext
    {
        internal PrivacyAnonymizationDbContext(DbContextOptions<AppDbContext> options,
            ITenantContext tenantContext)
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

    private static PrivacyAnonymizationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // O provider in-memory não implementa transações: sem esta supressão o
            // BeginTransactionAsync do serviço derruba o teste por limitação do dublê,
            // não por defeito da operação (a atomicidade real é do provider relacional).
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new PrivacyAnonymizationDbContext(options, new FixedTenantContext(TestTenantId));
    }

    /// <summary>Aluno com todas as linhas acadêmicas ligadas a ele, para provar a preservação dos vínculos.</summary>
    private static async Task SeedStudentWithRelatedRowsAsync(PrivacyAnonymizationDbContext db)
    {
        var now = DateTime.UtcNow;

        db.Students.Add(new Student
        {
            Id = StudentId,
            TenantId = TestTenantId,
            Nome = OriginalName,
            DataNascimento = OriginalBirthDate,
            Cpf = OriginalCpf,
            Turma = "5A",
            AnoLetivo = 2026,
            AcademicYearId = AcademicYearId,
            SchoolClassId = SchoolClassId,
            Status = StudentStatus.Ativo,
            Observacoes = OriginalNotes,
            Foto = OriginalPhoto,
            CreatedAt = now.AddDays(-30),
            UpdatedAt = now.AddDays(-1),
        });

        db.StudentParents.Add(new StudentParent { StudentId = StudentId, ParentId = GuardianId });

        db.Enrollments.Add(new Enrollment
        {
            Id = EnrollmentId,
            TenantId = TestTenantId,
            StudentId = StudentId,
            EnrollmentPeriodId = Guid.NewGuid(),
            Status = EnrollmentStatus.Aprovado,
            CreatedAt = now.AddDays(-20),
            ApprovedAt = now.AddDays(-18),
        });

        db.Assessments.Add(new Assessment
        {
            Id = AssessmentId,
            TenantId = TestTenantId,
            SchoolClassId = SchoolClassId,
            CurriculumComponentId = Guid.NewGuid(),
            Title = "Prova de Matemática",
            Date = DateOnly.FromDateTime(now.AddDays(-10)),
        });

        db.Grades.Add(new Grade
        {
            Id = GradeId,
            TenantId = TestTenantId,
            AssessmentId = AssessmentId,
            StudentId = StudentId,
            Value = 9.5m,
            RecordedAt = now.AddDays(-9),
        });

        db.Documents.Add(new Document
        {
            Id = DocumentId,
            TenantId = TestTenantId,
            StudentId = StudentId,
            DocumentTypeId = Guid.NewGuid(),
            NomeArquivo = "documento.pdf",
            CaminhoArquivo = "storage-key/tenant/student/documento.pdf",
            TamanhoBytes = 2048,
            HashSha256 = "hash-sha256",
            ContentType = "application/pdf",
            EnviadoPorUsuarioId = GuardianId,
            Status = DocumentStatus.Aprovado,
            CreatedAt = now.AddDays(-5),
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Anonymize_OverwritesEveryIdentityFieldAndStampsAnonymizedAt()
    {
        using var db = CreateContext();
        await SeedStudentWithRelatedRowsAsync(db);
        var before = await db.Students.AsNoTracking().SingleAsync();
        var service = new AnonymizationService(db, new CapturingAuditTrail());

        var result = await service.AnonymizeStudentAsync(TestTenantId, StudentId);

        db.ChangeTracker.Clear();
        var stored = await db.Students.SingleAsync();

        Assert.Equal("Titular anonimizado", stored.Nome);
        Assert.Null(stored.Cpf);
        Assert.Null(stored.Foto);
        Assert.Null(stored.Observacoes);
        Assert.Equal(AnonymousBirthDate, stored.DataNascimento);
        Assert.NotNull(stored.AnonymizedAt);

        // Nenhum valor original sobrevive à sobrescrita.
        Assert.NotEqual(OriginalName, stored.Nome);
        Assert.NotEqual(OriginalCpf, stored.Cpf);
        Assert.NotEqual(OriginalPhoto, stored.Foto);
        Assert.NotEqual(OriginalNotes, stored.Observacoes);
        Assert.NotEqual(OriginalBirthDate, stored.DataNascimento);

        // Identidade estrutural e vínculos acadêmicos continuam intactos (AD-001).
        Assert.Equal(before.Id, stored.Id);
        Assert.Equal(before.TenantId, stored.TenantId);
        Assert.Equal(before.Status, stored.Status);
        Assert.Equal(before.AcademicYearId, stored.AcademicYearId);
        Assert.Equal(before.SchoolClassId, stored.SchoolClassId);
        Assert.Equal(before.Turma, stored.Turma);
        Assert.Equal(before.AnoLetivo, stored.AnoLetivo);
        Assert.Equal(before.CreatedAt, stored.CreatedAt);
        Assert.Equal(stored.AnonymizedAt, stored.UpdatedAt);

        Assert.Equal("Titular anonimizado", result.Nome);
        Assert.Equal(stored.AnonymizedAt, result.AnonymizedAt);
    }

    [Fact]
    public async Task Anonymize_KeepsStudentParentsEnrollmentGradeAndDocumentRowsUntouched()
    {
        using var db = CreateContext();
        await SeedStudentWithRelatedRowsAsync(db);
        var before = new
        {
            Links = await db.StudentParents.AsNoTracking().CountAsync(),
            Enrollments = await db.Enrollments.AsNoTracking().CountAsync(),
            Grades = await db.Grades.AsNoTracking().CountAsync(),
            Documents = await db.Documents.AsNoTracking().CountAsync(),
        };
        var service = new AnonymizationService(db, new CapturingAuditTrail());

        await service.AnonymizeStudentAsync(TestTenantId, StudentId);

        db.ChangeTracker.Clear();

        var link = Assert.Single(await db.StudentParents.AsNoTracking().ToListAsync());
        Assert.Equal(StudentId, link.StudentId);
        Assert.Equal(GuardianId, link.ParentId);

        var enrollment = Assert.Single(await db.Enrollments.AsNoTracking().ToListAsync());
        Assert.Equal(EnrollmentId, enrollment.Id);
        Assert.Equal(StudentId, enrollment.StudentId);

        var grade = Assert.Single(await db.Grades.AsNoTracking().ToListAsync());
        Assert.Equal(GradeId, grade.Id);
        Assert.Equal(StudentId, grade.StudentId);
        Assert.Equal(9.5m, grade.Value);

        var document = Assert.Single(await db.Documents.AsNoTracking().ToListAsync());
        Assert.Equal(DocumentId, document.Id);
        Assert.Equal(StudentId, document.StudentId);
        Assert.Equal("storage-key/tenant/student/documento.pdf", document.CaminhoArquivo);

        Assert.Equal(before.Links, await db.StudentParents.AsNoTracking().CountAsync());
        Assert.Equal(before.Enrollments, await db.Enrollments.AsNoTracking().CountAsync());
        Assert.Equal(before.Grades, await db.Grades.AsNoTracking().CountAsync());
        Assert.Equal(before.Documents, await db.Documents.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Anonymize_WithAnOpenLegalHold_ThrowsAndLeavesEveryFieldAndTheAuditUntouched()
    {
        using var db = CreateContext();
        await SeedStudentWithRelatedRowsAsync(db);

        db.PrivacyRequests.Add(new PrivacyRequest
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            Type = PrivacyRequestType.Anonymization,
            Status = PrivacyRequestStatus.Approved,
            SubjectStudentId = StudentId,
            RequestedByUserId = GuardianId,
            LegalHold = true,
            SubmittedAt = DateTime.UtcNow.AddDays(-2),
        });
        await db.SaveChangesAsync();

        var before = await db.Students.AsNoTracking().SingleAsync();
        var audit = new CapturingAuditTrail();
        var service = new AnonymizationService(db, audit);

        var exception = await Assert.ThrowsAsync<PrivacyLegalHoldException>(
            () => service.AnonymizeStudentAsync(TestTenantId, StudentId));

        Assert.Equal("legal_hold_active", exception.Message);
        Assert.Equal(409, exception.StatusCode);

        db.ChangeTracker.Clear();
        var stored = await db.Students.SingleAsync();

        // Recusa atômica: nada foi escrito e nenhuma entrada de auditoria foi produzida.
        Assert.Equal(before.Nome, stored.Nome);
        Assert.Equal(before.Cpf, stored.Cpf);
        Assert.Equal(before.Foto, stored.Foto);
        Assert.Equal(before.Observacoes, stored.Observacoes);
        Assert.Equal(before.DataNascimento, stored.DataNascimento);
        Assert.Equal(before.UpdatedAt, stored.UpdatedAt);
        Assert.Null(stored.AnonymizedAt);
        Assert.Equal(before.Status, stored.Status);
        Assert.Equal(before.AcademicYearId, stored.AcademicYearId);
        Assert.Equal(before.SchoolClassId, stored.SchoolClassId);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Anonymize_LegalHoldOnACompletedRequest_DoesNotBlock()
    {
        using var db = CreateContext();
        await SeedStudentWithRelatedRowsAsync(db);

        // Encerrado (Completed/Rejected) não é bloqueio aberto: o prazo de retenção fica
        // registrado, mas a execução da anonimização não é mais barrada por ele.
        db.PrivacyRequests.Add(new PrivacyRequest
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            Type = PrivacyRequestType.Anonymization,
            Status = PrivacyRequestStatus.Completed,
            SubjectStudentId = StudentId,
            RequestedByUserId = GuardianId,
            LegalHold = true,
            RetainUntil = DateTime.UtcNow.AddYears(1),
            SubmittedAt = DateTime.UtcNow.AddDays(-10),
            CompletedAt = DateTime.UtcNow.AddDays(-8),
        });
        await db.SaveChangesAsync();

        var service = new AnonymizationService(db, new CapturingAuditTrail());

        var result = await service.AnonymizeStudentAsync(TestTenantId, StudentId);

        Assert.Equal("Titular anonimizado", result.Nome);
        Assert.NotNull(result.AnonymizedAt);

        db.ChangeTracker.Clear();
        var stored = await db.Students.SingleAsync();
        Assert.Equal("Titular anonimizado", stored.Nome);
        Assert.Null(stored.Cpf);
    }

    [Fact]
    public async Task Anonymize_AlreadyAnonymized_IsANoOpWithoutSecondAuditEntry()
    {
        using var db = CreateContext();
        await SeedStudentWithRelatedRowsAsync(db);
        var audit = new CapturingAuditTrail();
        var service = new AnonymizationService(db, audit);

        var first = await service.AnonymizeStudentAsync(TestTenantId, StudentId);
        Assert.Single(audit.Entries);

        db.ChangeTracker.Clear();
        var afterFirstCall = await db.Students.AsNoTracking().SingleAsync();

        var second = await service.AnonymizeStudentAsync(TestTenantId, StudentId);

        db.ChangeTracker.Clear();
        var afterSecondCall = await db.Students.AsNoTracking().SingleAsync();

        Assert.Single(audit.Entries);
        Assert.Equal(afterFirstCall.AnonymizedAt, afterSecondCall.AnonymizedAt);
        Assert.Equal(afterFirstCall.UpdatedAt, afterSecondCall.UpdatedAt);
        Assert.Equal(first.AnonymizedAt, second.AnonymizedAt);
        Assert.Equal(afterFirstCall.Nome, afterSecondCall.Nome);
        Assert.Equal(afterFirstCall.DataNascimento, afterSecondCall.DataNascimento);
    }

    [Fact]
    public async Task Anonymize_RecordsOneMarkerOnlyAuditEntryWithoutAnyOriginalIdentityValue()
    {
        using var db = CreateContext();
        await SeedStudentWithRelatedRowsAsync(db);
        var audit = new CapturingAuditTrail();
        var service = new AnonymizationService(db, audit);

        await service.AnonymizeStudentAsync(TestTenantId, StudentId);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.PrivacyAnonymized, entry.Action);
        Assert.Equal(AuditTargetTypes.Student, entry.TargetType);
        Assert.Equal(StudentId, entry.TargetId);

        // AD-002: a trilha é imutável, então nunca guarda o valor que a operação apaga.
        Assert.Null(entry.Before);

        var payload = JsonSerializer.Serialize(entry.After);
        Assert.DoesNotContain(OriginalName, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(OriginalCpf, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(OriginalPhoto, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(OriginalNotes, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("2010-05-04", payload, StringComparison.Ordinal);
        Assert.Contains("anonymized", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymize_UnknownStudent_ThrowsKeyNotFoundAndWritesNothing()
    {
        using var db = CreateContext();
        await SeedStudentWithRelatedRowsAsync(db);
        var audit = new CapturingAuditTrail();
        var service = new AnonymizationService(db, audit);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.AnonymizeStudentAsync(TestTenantId, Guid.NewGuid()));

        Assert.Empty(audit.Entries);
        Assert.Equal(OriginalName, (await db.Students.AsNoTracking().SingleAsync()).Nome);
    }

    [Fact]
    public async Task Anonymize_StudentOfAnotherTenant_ThrowsKeyNotFoundAndLeavesTheRowUntouched()
    {
        using var db = CreateContext();
        await SeedStudentWithRelatedRowsAsync(db);

        db.Students.Add(new Student
        {
            Id = OtherTenantStudentId,
            TenantId = OtherTenantId,
            Nome = "Aluno de Outra Escola",
            DataNascimento = OriginalBirthDate,
            Cpf = "98765432100",
            Turma = "2B",
            AnoLetivo = 2026,
        });
        await db.SaveChangesAsync();

        var audit = new CapturingAuditTrail();
        var service = new AnonymizationService(db, audit);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.AnonymizeStudentAsync(TestTenantId, OtherTenantStudentId));

        db.ChangeTracker.Clear();
        var stored = await db.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == OtherTenantStudentId);

        Assert.Equal("Aluno de Outra Escola", stored.Nome);
        Assert.Equal("98765432100", stored.Cpf);
        Assert.Equal(OriginalBirthDate, stored.DataNascimento);
        Assert.Null(stored.AnonymizedAt);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Anonymize_TwiceThroughTheSameContext_KeepsThePersistedSentinelValues()
    {
        using var db = CreateContext();
        await SeedStudentWithRelatedRowsAsync(db);
        var audit = new CapturingAuditTrail();
        var service = new AnonymizationService(db, audit);

        await service.AnonymizeStudentAsync(TestTenantId, StudentId);
        await service.AnonymizeStudentAsync(TestTenantId, StudentId);

        var stored = await db.Students.AsNoTracking().SingleAsync();

        Assert.Equal("Titular anonimizado", stored.Nome);
        Assert.Equal(AnonymousBirthDate, stored.DataNascimento);
        Assert.Single(audit.Entries);
        Assert.All(audit.Entries, e => Assert.Equal(AuditActions.PrivacyAnonymized, e.Action));
    }
}
