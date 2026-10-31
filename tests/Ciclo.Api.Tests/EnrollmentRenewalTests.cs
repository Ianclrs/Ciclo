using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Exceptions;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 160 — rematrícula em lote. Prova a prévia (elegibilidade, documentos reaproveitáveis e
/// vencidos, ordem por nome), a idempotência por (StudentId, EnrollmentPeriodId), o relatório de
/// falhas parciais, a promoção de <c>Student.AnoLetivo</c> apenas para renovados e o evento de
/// auditoria por aluno efetivado.
///
/// Repete o cuidado já documentado em <c>AuditTestData</c>: o filtro global de tenant é compilado no
/// modelo do EF, e o modelo é cacheado por TIPO de contexto, preso à instância que o criou. Por isso
/// o contexto é um tipo derivado deste arquivo, com tenant fixo, e o isolamento entre testes vem do
/// nome único do banco InMemory.
/// </summary>
public sealed class EnrollmentRenewalTests
{
    /// <summary>
    /// Tenant fixo deste arquivo: o filtro global do modelo é capturado uma vez por tipo de
    /// contexto, então um valor variável faria as consultas de um teste enxergarem o tenant de outro.
    /// </summary>
    private static readonly Guid TestTenantId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>Ano em que os alunos semeidados estão — sempre diferente do ano do período destino.</summary>
    private static readonly int CurrentYear = DateTime.UtcNow.Year;

    /// <summary>Ano do período destino: a promoção esperada de quem é renovado.</summary>
    private static readonly int TargetYear = CurrentYear + 1;

    private static readonly Guid AdminUserId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>Tipo obrigatório semeidado e esperado em <c>MissingDocumentTypes</c>.</summary>
    private static readonly string[] ExpectedMissingDocumentType = { "Cartão de vacinação" };

    /// <summary>Ordem alfabética esperada dos candidatos da prévia.</summary>
    private static readonly string[] ExpectedStudentOrder = { "Ana", "Bruno" };

    private sealed class FixedTenantContext : ITenantContext
    {
        private readonly Guid _tenantId;

        internal FixedTenantContext(Guid tenantId) => _tenantId = tenantId;

        public Guid TenantId => _tenantId;

        public bool IsResolved => true;

        public void SetTenant(Guid tenantId) =>
            throw new NotSupportedException("O tenant é fixo no contexto de teste.");
    }

    private sealed class RenewalTestDbContext : AppDbContext
    {
        internal RenewalTestDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }
    }

    private sealed class Fixture : IDisposable
    {
        internal AppDbContext Db { get; }

        internal EnrollmentPeriod Period { get; }

        internal Fixture()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            Db = new RenewalTestDbContext(options, new FixedTenantContext(TestTenantId));

            Db.Tenants.Add(new Tenant
            {
                Id = TestTenantId,
                Name = "Escola Teste",
                Slug = "escola-teste",
                Country = "BR"
            });

            // Período destino dentro da janela de datas: `CreateEnrollmentAsync` recusa criar fora
            // dela, e o teste não é sobre calendário.
            Period = new EnrollmentPeriod
            {
                Id = Guid.NewGuid(),
                TenantId = TestTenantId,
                Nome = $"Rematrícula {TargetYear}",
                AnoLetivo = TargetYear,
                DataInicio = DateTime.UtcNow.AddMonths(-1),
                DataFim = DateTime.UtcNow.AddMonths(1),
                IsActive = true
            };

            Db.EnrollmentPeriods.Add(Period);
            Db.SaveChanges();
        }

        internal EnrollmentRenewalService CreateRenewal(IAuditTrail audit) =>
            new(Db, new EnrollmentService(Db, audit));

        internal Student AddStudent(
            string nome, int anoLetivo = -1, StudentStatus status = StudentStatus.Ativo)
        {
            var student = new Student
            {
                Id = Guid.NewGuid(),
                TenantId = TestTenantId,
                Nome = nome,
                Turma = "1A",
                AnoLetivo = anoLetivo < 0 ? CurrentYear : anoLetivo,
                Status = status
            };

            Db.Students.Add(student);
            Db.SaveChanges();
            return student;
        }

        internal DocumentType AddDocumentType(string nome, bool isRequired, bool isActive = true)
        {
            var documentType = new DocumentType
            {
                Id = Guid.NewGuid(),
                TenantId = TestTenantId,
                Nome = nome,
                IsRequired = isRequired,
                IsActive = isActive
            };

            Db.DocumentTypes.Add(documentType);
            Db.SaveChanges();
            return documentType;
        }

        internal Document AddDocument(
            Student student, DocumentType documentType, DocumentStatus status, DateTime? dataValidade)
        {
            var document = new Document
            {
                Id = Guid.NewGuid(),
                TenantId = TestTenantId,
                StudentId = student.Id,
                DocumentTypeId = documentType.Id,
                NomeArquivo = "arquivo.pdf",
                CaminhoArquivo = "arquivo.pdf",
                Status = status,
                DataValidade = dataValidade
            };

            Db.Documents.Add(document);
            Db.SaveChanges();
            return document;
        }

        internal Enrollment AddEnrollment(Student student, EnrollmentStatus status)
        {
            var enrollment = new Enrollment
            {
                Id = Guid.NewGuid(),
                TenantId = TestTenantId,
                StudentId = student.Id,
                EnrollmentPeriodId = Period.Id,
                Status = status
            };

            Db.Enrollments.Add(enrollment);
            Db.SaveChanges();
            return enrollment;
        }

        internal Task<RenewalPreviewDto> PreviewAsync(
            IAuditTrail audit, Guid? periodId = null, UserRole role = UserRole.Admin) =>
            CreateRenewal(audit).GetPreviewAsync(periodId ?? Period.Id, TestTenantId, role);

        internal Task<RenewalBatchResultDto> RenewAsync(
            IEnumerable<Guid> studentIds, IAuditTrail audit, UserRole role = UserRole.Admin) =>
            CreateRenewal(audit).RenewAsync(
                new RenewalBatchRequest(Period.Id, studentIds.ToList()),
                TestTenantId,
                AdminUserId,
                role);

        /// <summary>
        /// Leitura fresca do banco: o serviço deixa entidades rastreadas (e limpa o rastreador em
        /// falhas), então reler sem limpar poderia devolver o estado em memória e não o persistido.
        /// </summary>
        internal async Task<List<Enrollment>> StoredEnrollmentsAsync()
        {
            Db.ChangeTracker.Clear();
            return await Db.Enrollments.ToListAsync();
        }

        internal async Task<Student> ReloadStudentAsync(Guid studentId)
        {
            Db.ChangeTracker.Clear();
            return await Db.Students.SingleAsync(student => student.Id == studentId);
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task Preview_ClassifiesApprovedDocumentsAsReusableOrExpired()
    {
        using var fixture = new Fixture();
        var student = fixture.AddStudent("Ana");
        var rg = fixture.AddDocumentType("RG", isRequired: true);
        var comprovante = fixture.AddDocumentType("Comprovante de residência", isRequired: false);

        // Sem validade = não expira; validade no passado = venceu, precisa reenviar.
        fixture.AddDocument(student, rg, DocumentStatus.Aprovado, dataValidade: null);
        fixture.AddDocument(
            student, comprovante, DocumentStatus.Aprovado, dataValidade: DateTime.UtcNow.AddDays(-1));

        var preview = await fixture.PreviewAsync(NoopAuditTrail.Instance);

        Assert.Equal(fixture.Period.Id, preview.PeriodId);
        Assert.Equal(fixture.Period.Nome, preview.PeriodName);
        Assert.Equal(TargetYear, preview.AnoLetivo);

        var item = Assert.Single(preview.Items);
        Assert.True(item.Eligible);
        Assert.Null(item.BlockReason);
        Assert.False(item.EnrollmentAlreadyExists);
        Assert.Empty(item.MissingDocumentTypes);

        Assert.Equal(2, item.CarriedDocuments.Count);
        Assert.False(Assert.Single(item.CarriedDocuments, d => d.DocumentType == "RG").IsExpired);
        Assert.True(Assert.Single(
            item.CarriedDocuments, d => d.DocumentType == "Comprovante de residência").IsExpired);
    }

    [Fact]
    public async Task Preview_ReportsRequiredTypesWithoutValidDocument()
    {
        using var fixture = new Fixture();
        fixture.AddDocumentType("Cartão de vacinação", isRequired: true);
        fixture.AddStudent("Ana");

        var item = Assert.Single((await fixture.PreviewAsync(NoopAuditTrail.Instance)).Items);

        Assert.False(item.Eligible);
        Assert.Equal("pending_required_documents", item.BlockReason);
        Assert.Equal(ExpectedMissingDocumentType, item.MissingDocumentTypes);
    }

    [Fact]
    public async Task Preview_OrdersByNameAndOmitsStudentsAlreadyInTargetYear()
    {
        using var fixture = new Fixture();
        fixture.AddStudent("Bruno");
        fixture.AddStudent("Ana");
        fixture.AddStudent("Zelia", anoLetivo: TargetYear);

        var items = (await fixture.PreviewAsync(NoopAuditTrail.Instance)).Items;

        Assert.Equal(ExpectedStudentOrder, items.Select(item => item.StudentName));
    }

    [Fact]
    public async Task Preview_MarksEnrollmentThatAlreadyExistsInThePeriod()
    {
        using var fixture = new Fixture();
        var student = fixture.AddStudent("Ana");
        fixture.AddEnrollment(student, EnrollmentStatus.Pendente);

        var item = Assert.Single((await fixture.PreviewAsync(NoopAuditTrail.Instance)).Items);

        Assert.False(item.Eligible);
        Assert.True(item.EnrollmentAlreadyExists);
        Assert.Equal("enrollment_already_exists", item.BlockReason);
    }

    [Fact]
    public async Task Preview_AppliesInactiveBeforePendingDocuments()
    {
        using var fixture = new Fixture();
        fixture.AddDocumentType("Cartão de vacinação", isRequired: true);
        fixture.AddStudent("Ana", status: StudentStatus.Inativo);

        var item = Assert.Single((await fixture.PreviewAsync(NoopAuditTrail.Instance)).Items);

        // A ordem de precedência faz parte do contrato: inativo explica mais do que documento.
        Assert.False(item.Eligible);
        Assert.Equal("student_inactive", item.BlockReason);
        Assert.Single(item.MissingDocumentTypes);
    }

    [Fact]
    public async Task Preview_WhenPeriodDoesNotExist_ThrowsNotFound()
    {
        using var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<EnrollmentException>(
            () => fixture.PreviewAsync(NoopAuditTrail.Instance, periodId: Guid.NewGuid()));

        Assert.Equal("period_not_found", exception.Message);
        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task Renew_ApprovesEnrollmentPromotesStudentAndWritesAuditEvent()
    {
        using var fixture = new Fixture();
        var student = fixture.AddStudent("Ana");
        var trail = new RecordingAuditTrail();

        var result = await fixture.RenewAsync([student.Id], trail);

        Assert.Equal(fixture.Period.Id, result.EnrollmentPeriodId);
        Assert.Equal(TargetYear, result.AnoLetivo);

        var item = Assert.Single(result.Items);
        Assert.Equal(RenewalOutcome.Renewed, item.Outcome);
        Assert.Null(item.Message);
        Assert.Equal(student.Id, item.StudentId);
        Assert.Equal("Ana", item.StudentName);

        Assert.Equal(1, result.Renewed);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Blocked);
        Assert.Equal(0, result.Failed);

        var stored = Assert.Single(await fixture.StoredEnrollmentsAsync());
        Assert.Equal(student.Id, stored.StudentId);
        Assert.Equal(fixture.Period.Id, stored.EnrollmentPeriodId);
        Assert.Equal(EnrollmentStatus.Aprovado, stored.Status);
        Assert.NotNull(stored.ApprovedAt);

        var promoted = await fixture.ReloadStudentAsync(student.Id);
        Assert.Equal(TargetYear, promoted.AnoLetivo);

        var entry = Assert.Single(trail.Entries);
        Assert.Equal(AuditActions.EnrollmentApproved, entry.Action);
        Assert.Equal(AuditTargetTypes.Enrollment, entry.TargetType);
        Assert.Equal(stored.Id, entry.TargetId);
    }

    [Fact]
    public async Task Renew_RunTwice_SkipsTheSecondRunWithoutDuplicatingEnrollment()
    {
        using var fixture = new Fixture();
        var student = fixture.AddStudent("Ana");
        var trail = new RecordingAuditTrail();

        var first = await fixture.RenewAsync([student.Id], trail);
        var second = await fixture.RenewAsync([student.Id], trail);

        Assert.Equal(RenewalOutcome.Renewed, Assert.Single(first.Items).Outcome);

        var skipped = Assert.Single(second.Items);
        Assert.Equal(RenewalOutcome.SkippedAlreadyExists, skipped.Outcome);
        Assert.Equal("enrollment_already_exists", skipped.Message);
        Assert.Equal(0, second.Renewed);
        Assert.Equal(1, second.Skipped);

        Assert.Single(await fixture.StoredEnrollmentsAsync());

        // Nenhuma transição na segunda execução: um evento de auditoria, da primeira aprovação.
        Assert.Single(trail.Entries);
    }

    [Fact]
    public async Task Renew_MixedBatch_ReportsBlockedAndRenewedAndPromotesOnlyTheRenewed()
    {
        using var fixture = new Fixture();
        var required = fixture.AddDocumentType("Cartão de vacinação", isRequired: true);
        var blocked = fixture.AddStudent("Ana");
        var renewable = fixture.AddStudent("Bruno");
        fixture.AddDocument(
            renewable, required, DocumentStatus.Aprovado, dataValidade: DateTime.UtcNow.AddMonths(6));

        var result = await fixture.RenewAsync([blocked.Id, renewable.Id], NoopAuditTrail.Instance);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Renewed);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Blocked);
        Assert.Equal(0, result.Failed);
        Assert.Equal(
            result.Items.Count,
            result.Renewed + result.Skipped + result.Blocked + result.Failed);

        var blockedItem = Assert.Single(result.Items, item => item.StudentId == blocked.Id);
        Assert.Equal(RenewalOutcome.BlockedPendingDocuments, blockedItem.Outcome);
        Assert.Equal("pending_required_documents", blockedItem.Message);

        Assert.Equal(
            RenewalOutcome.Renewed,
            Assert.Single(result.Items, item => item.StudentId == renewable.Id).Outcome);

        Assert.Equal(CurrentYear, (await fixture.ReloadStudentAsync(blocked.Id)).AnoLetivo);
        Assert.Equal(TargetYear, (await fixture.ReloadStudentAsync(renewable.Id)).AnoLetivo);
    }

    [Fact]
    public async Task Renew_WithEmptyOrNullStudentIds_ThrowsBadRequestWithoutWriting()
    {
        using var fixture = new Fixture();
        var student = fixture.AddStudent("Ana");
        var renewal = fixture.CreateRenewal(NoopAuditTrail.Instance);

        var empty = await Assert.ThrowsAsync<EnrollmentException>(() =>
            renewal.RenewAsync(
                new RenewalBatchRequest(fixture.Period.Id, []), TestTenantId, AdminUserId, UserRole.Admin));

        Assert.Equal("student_ids_required", empty.Message);
        Assert.Equal(400, empty.StatusCode);

        var missing = await Assert.ThrowsAsync<EnrollmentException>(() =>
            renewal.RenewAsync(
                new RenewalBatchRequest(fixture.Period.Id, null!), TestTenantId, AdminUserId, UserRole.Admin));

        Assert.Equal("student_ids_required", missing.Message);
        Assert.Equal(400, missing.StatusCode);

        Assert.Empty(await fixture.StoredEnrollmentsAsync());
        Assert.Equal(CurrentYear, (await fixture.ReloadStudentAsync(student.Id)).AnoLetivo);
    }

    [Fact]
    public async Task Renew_WithDuplicatedStudentId_ProcessesTheStudentOnce()
    {
        using var fixture = new Fixture();
        var student = fixture.AddStudent("Ana");

        var result = await fixture.RenewAsync(
            [student.Id, student.Id], NoopAuditTrail.Instance);

        Assert.Equal(RenewalOutcome.Renewed, Assert.Single(result.Items).Outcome);
        Assert.Equal(1, result.Renewed);
        Assert.Single(await fixture.StoredEnrollmentsAsync());
    }

    [Fact]
    public async Task Renew_WithUnknownStudent_ReportsFailureWithoutInterruptingTheBatch()
    {
        using var fixture = new Fixture();
        var student = fixture.AddStudent("Ana");
        var unknown = Guid.NewGuid();

        var result = await fixture.RenewAsync([unknown, student.Id], NoopAuditTrail.Instance);

        var failed = Assert.Single(result.Items, item => item.StudentId == unknown);
        Assert.Equal(RenewalOutcome.Failed, failed.Outcome);
        Assert.Equal("student_not_found", failed.Message);
        Assert.Equal(1, result.Failed);

        // O aluno seguinte foi processado normalmente: a falha é por aluno, não do lote.
        Assert.Equal(
            RenewalOutcome.Renewed,
            Assert.Single(result.Items, item => item.StudentId == student.Id).Outcome);
    }

    [Fact]
    public async Task Renew_WhenAuditTrailFails_ReportsFailureAndLeavesEnrollmentUnapproved()
    {
        using var fixture = new Fixture();
        var student = fixture.AddStudent("Ana");

        var result = await fixture.RenewAsync([student.Id], new ThrowingAuditTrail());

        var item = Assert.Single(result.Items);
        Assert.Equal(RenewalOutcome.Failed, item.Outcome);
        Assert.Equal("audit_write_failed", item.Message);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Renewed);

        var stored = Assert.Single(await fixture.StoredEnrollmentsAsync());
        Assert.NotEqual(EnrollmentStatus.Aprovado, stored.Status);
        Assert.Null(stored.ApprovedAt);

        // A promoção acontece dentro da aprovação: sem aprovação, o ano letivo não muda.
        Assert.Equal(CurrentYear, (await fixture.ReloadStudentAsync(student.Id)).AnoLetivo);
    }

    [Fact]
    public async Task BothOperations_WithParentRole_ThrowForbidden()
    {
        using var fixture = new Fixture();
        var student = fixture.AddStudent("Ana");
        var renewal = fixture.CreateRenewal(NoopAuditTrail.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            renewal.GetPreviewAsync(fixture.Period.Id, TestTenantId, UserRole.Parent));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            renewal.RenewAsync(
                new RenewalBatchRequest(fixture.Period.Id, [student.Id]),
                TestTenantId,
                AdminUserId,
                UserRole.Parent));

        Assert.Empty(await fixture.StoredEnrollmentsAsync());
    }
}
