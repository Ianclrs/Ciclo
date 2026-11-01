using System.Text.Json;
using System.Text.Json.Nodes;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 210 FR-002 / NFR-003 — documento de exportação do titular. Prova a completude das sete
/// seções invariantes, o array vazio no lugar de nulo, a redação de credenciais e de chaves de
/// armazenamento, a projeção de um aluno já anonimizado e o determinismo do documento.
/// </summary>
public sealed class PrivacyExportTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GuardianId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid EmptyStudentId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid AnonymizedStudentId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid OtherTenantStudentId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static readonly string[] ExpectedSectionOrder =
    [
        "generatedAt",
        "student",
        "guardians",
        "enrollments",
        "grades",
        "documents",
        "consents",
        "requests",
    ];

    private static readonly string[] ExpectedCollectionSections =
    [
        "guardians",
        "enrollments",
        "grades",
        "documents",
        "consents",
        "requests",
    ];

    /// <summary>
    /// Contexto derivado com tipo próprio: o EF cacheia o modelo por tipo e o filtro global de
    /// tenant fica preso à instância que construiu o modelo (mesmo motivo de AuditTrailTests).
    /// </summary>
    private sealed class PrivacyExportDbContext : AppDbContext
    {
        internal PrivacyExportDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
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

    private static PrivacyExportDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PrivacyExportDbContext(options, new FixedTenantContext(TestTenantId));
    }

    private static string Serialize(PrivacyExportService service, PrivacyExportDto document)
    {
        using var stream = service.Serialize(document);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Aluno com todas as seções preenchidas, incluindo credenciais que nunca devem sair.</summary>
    private static async Task SeedFullyPopulatedStudentAsync(PrivacyExportDbContext db)
    {
        var now = DateTime.UtcNow;

        db.Students.Add(new Student
        {
            Id = StudentId,
            TenantId = TestTenantId,
            Nome = "Aluno Exportado",
            DataNascimento = new DateTime(2010, 5, 4, 0, 0, 0, DateTimeKind.Utc),
            Cpf = "12345678900",
            Turma = "5A",
            AnoLetivo = 2026,
            Status = StudentStatus.Ativo,
            Observacoes = "Observação sensível",
            Foto = "data:image/png;base64,foto",
            CreatedAt = now.AddDays(-30),
            UpdatedAt = now.AddDays(-1),
        });

        db.Users.Add(new User
        {
            Id = GuardianId,
            TenantId = TestTenantId,
            Name = "Responsável do Aluno",
            Email = "responsavel@example.com",
            Role = UserRole.Parent,
            IsActive = true,
            CreatedAt = now.AddDays(-60),
            PasswordHash = "hash-secret-value",
            SecurityStamp = "stamp-secret-value",
            ConcurrencyStamp = "concurrency-secret-value",
        });

        db.StudentParents.Add(new StudentParent { StudentId = StudentId, ParentId = GuardianId });

        db.Enrollments.Add(new Enrollment
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            StudentId = StudentId,
            EnrollmentPeriodId = Guid.NewGuid(),
            Status = EnrollmentStatus.Aprovado,
            CreatedAt = now.AddDays(-20),
            ApprovedAt = now.AddDays(-18),
        });

        var assessmentId = Guid.NewGuid();
        db.Assessments.Add(new Assessment
        {
            Id = assessmentId,
            TenantId = TestTenantId,
            SchoolClassId = Guid.NewGuid(),
            CurriculumComponentId = Guid.NewGuid(),
            Title = "Prova de Matemática",
            Date = DateOnly.FromDateTime(now.AddDays(-10)),
        });

        db.Grades.Add(new Grade
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            AssessmentId = assessmentId,
            StudentId = StudentId,
            Value = 9.5m,
            RecordedAt = now.AddDays(-9),
        });

        db.Documents.Add(new Document
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            StudentId = StudentId,
            DocumentTypeId = Guid.NewGuid(),
            NomeArquivo = "documento.pdf",
            CaminhoArquivo = "storage-key-value/tenant/student/documento.pdf",
            TamanhoBytes = 2048,
            HashSha256 = "hashsha-secret",
            ContentType = "application/pdf",
            EnviadoPorUsuarioId = GuardianId,
            Status = DocumentStatus.Aprovado,
            CreatedAt = now.AddDays(-5),
        });

        db.ConsentRecords.Add(new ConsentRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            SubjectType = ConsentSubjectType.Student,
            SubjectId = StudentId,
            Purpose = "saude",
            LegalBasis = "consentimento",
            Status = ConsentStatus.Granted,
            GrantedAt = now.AddDays(-7),
            GrantedByUserId = GuardianId,
            CreatedAt = now.AddDays(-7),
        });

        db.PrivacyRequests.Add(new PrivacyRequest
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            Type = PrivacyRequestType.Export,
            Status = PrivacyRequestStatus.Completed,
            SubjectStudentId = StudentId,
            RequestedByUserId = GuardianId,
            Reason = "Pedido do responsável",
            Resolution = "Atendido",
            LegalHold = false,
            RetainUntil = now.AddYears(1),
            SubmittedAt = now.AddDays(-4),
            DecidedAt = now.AddDays(-3),
            CompletedAt = now.AddDays(-2),
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Export_FullyPopulatedStudent_ReturnsAllSectionsWithData()
    {
        using var db = CreateContext();
        await SeedFullyPopulatedStudentAsync(db);
        var service = new PrivacyExportService(db);

        var document = await service.ExportStudentAsync(TestTenantId, StudentId);

        Assert.NotEqual(default, document.GeneratedAt);
        Assert.Equal(StudentId, document.Student.Id);
        Assert.Equal("Aluno Exportado", document.Student.Nome);
        Assert.Equal("12345678900", document.Student.Cpf);
        Assert.Null(document.Student.AnonymizedAt);

        var guardian = Assert.Single(document.Guardians);
        Assert.Equal(GuardianId, guardian.Id);
        Assert.Equal("Responsável do Aluno", guardian.Name);
        Assert.Equal("responsavel@example.com", guardian.Email);
        Assert.Equal("Parent", guardian.Role);

        Assert.Single(document.Enrollments);
        var grade = Assert.Single(document.Grades);
        Assert.Equal("Prova de Matemática", grade.AssessmentTitle);
        Assert.Equal(9.5m, grade.Value);

        var documentRow = Assert.Single(document.Documents);
        Assert.Equal("documento.pdf", documentRow.NomeArquivo);
        Assert.Equal(2048, documentRow.TamanhoBytes);

        Assert.Single(document.Consents);
        Assert.Equal("saude", document.Consents[0].Purpose);

        Assert.Single(document.Requests);
        Assert.NotNull(document.Requests[0].RetainUntil);
    }

    [Fact]
    public async Task Export_StudentWithoutRelatedRows_ReturnsTheSevenSectionsWithEmptyCollections()
    {
        using var db = CreateContext();
        db.Students.Add(new Student
        {
            Id = EmptyStudentId,
            TenantId = TestTenantId,
            Nome = "Aluno Sem Dados",
            DataNascimento = new DateTime(2011, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Turma = "1A",
            AnoLetivo = 2026,
        });
        await db.SaveChangesAsync();

        var service = new PrivacyExportService(db);
        var document = await service.ExportStudentAsync(TestTenantId, EmptyStudentId);

        Assert.NotNull(document.Student);
        Assert.NotNull(document.Guardians);
        Assert.NotNull(document.Enrollments);
        Assert.NotNull(document.Grades);
        Assert.NotNull(document.Documents);
        Assert.NotNull(document.Consents);
        Assert.NotNull(document.Requests);
        Assert.Empty(document.Guardians);
        Assert.Empty(document.Enrollments);
        Assert.Empty(document.Grades);
        Assert.Empty(document.Documents);
        Assert.Empty(document.Consents);
        Assert.Empty(document.Requests);
    }

    [Fact]
    public async Task Export_SerializedDocument_KeepsTheFixedSectionOrder()
    {
        using var db = CreateContext();
        await SeedFullyPopulatedStudentAsync(db);
        var service = new PrivacyExportService(db);

        var document = await service.ExportStudentAsync(TestTenantId, StudentId);
        using var json = JsonDocument.Parse(Serialize(service, document));

        var sections = json.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(ExpectedSectionOrder, sections);
        Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("student").ValueKind);

        foreach (var section in ExpectedCollectionSections)
            Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty(section).ValueKind);
    }

    [Fact]
    public async Task Export_EmptySections_SerializeAsEmptyArraysNeverNull()
    {
        using var db = CreateContext();
        db.Students.Add(new Student
        {
            Id = EmptyStudentId,
            TenantId = TestTenantId,
            Nome = "Aluno Sem Dados",
            DataNascimento = new DateTime(2011, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Turma = "1A",
            AnoLetivo = 2026,
        });
        await db.SaveChangesAsync();

        var service = new PrivacyExportService(db);
        var document = await service.ExportStudentAsync(TestTenantId, EmptyStudentId);

        using var json = JsonDocument.Parse(Serialize(service, document));
        foreach (var section in ExpectedCollectionSections)
        {
            var element = json.RootElement.GetProperty(section);
            Assert.Equal(JsonValueKind.Array, element.ValueKind);
            Assert.Equal(0, element.GetArrayLength());
        }
    }

    [Fact]
    public async Task Export_SerializedDocument_CarriesNoCredentialAndNoStorageKey()
    {
        using var db = CreateContext();
        await SeedFullyPopulatedStudentAsync(db);
        var service = new PrivacyExportService(db);

        var document = await service.ExportStudentAsync(TestTenantId, StudentId);
        var text = Serialize(service, document);

        Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("concurrencyStamp", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash-secret-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("stamp-secret-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("concurrency-secret-value", text, StringComparison.Ordinal);

        Assert.DoesNotContain("caminhoArquivo", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hashSha256", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storage-key-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hashsha-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_AnonymizedStudent_ReturnsThePersistedAnonymizedValues()
    {
        using var db = CreateContext();
        var anonymizedAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        db.Students.Add(new Student
        {
            Id = AnonymizedStudentId,
            TenantId = TestTenantId,
            Nome = "Titular anonimizado",
            DataNascimento = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Cpf = null,
            Turma = "5A",
            AnoLetivo = 2026,
            Observacoes = null,
            Foto = null,
            AnonymizedAt = anonymizedAt,
        });
        await db.SaveChangesAsync();

        var service = new PrivacyExportService(db);
        var document = await service.ExportStudentAsync(TestTenantId, AnonymizedStudentId);

        Assert.Equal("Titular anonimizado", document.Student.Nome);
        Assert.Null(document.Student.Cpf);
        Assert.Null(document.Student.Foto);
        Assert.Null(document.Student.Observacoes);
        Assert.Equal(new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc), document.Student.DataNascimento);
        Assert.Equal(anonymizedAt, document.Student.AnonymizedAt);
    }

    [Fact]
    public async Task Export_SameUnchangedData_ProducesIdenticalDocumentsApartFromGeneratedAt()
    {
        using var db = CreateContext();
        await SeedFullyPopulatedStudentAsync(db);
        var service = new PrivacyExportService(db);

        var first = JsonNode.Parse(Serialize(service, await service.ExportStudentAsync(TestTenantId, StudentId)))!;
        var second = JsonNode.Parse(Serialize(service, await service.ExportStudentAsync(TestTenantId, StudentId)))!;

        Assert.True(first.AsObject().Remove("generatedAt"));
        Assert.True(second.AsObject().Remove("generatedAt"));

        Assert.Equal(first.ToJsonString(), second.ToJsonString());
    }

    [Fact]
    public async Task Export_UnknownStudent_ThrowsKeyNotFound()
    {
        using var db = CreateContext();
        await SeedFullyPopulatedStudentAsync(db);
        var service = new PrivacyExportService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.ExportStudentAsync(TestTenantId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Export_StudentOfAnotherTenant_ThrowsKeyNotFoundAndLeaksNothing()
    {
        using var db = CreateContext();
        db.Students.Add(new Student
        {
            Id = OtherTenantStudentId,
            TenantId = OtherTenantId,
            Nome = "Aluno de Outra Escola",
            DataNascimento = new DateTime(2011, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            Turma = "2B",
            AnoLetivo = 2026,
        });
        await db.SaveChangesAsync();

        var service = new PrivacyExportService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.ExportStudentAsync(TestTenantId, OtherTenantStudentId));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.ExportStudentAsync(OtherTenantId, OtherTenantStudentId));
    }
}
