using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Ciclo.Api.Tests;

/// <summary>
/// M1 e M2 — o mesmo shape em todos os caminhos de `StudentDto` e uma consulta
/// por dashboard, em vez de duas por filho.
///
/// O contexto é derivado e o tenant é fixo pelo mesmo motivo medido em
/// `DocumentComplianceTests`: o filtro global de tenant é compilado no modelo do
/// EF, que é cacheado por tipo de contexto.
/// </summary>
public sealed class StudentMappingAndParentDashboardTests
{
    private sealed class MappingTestDbContext : AppDbContext
    {
        internal MappingTestDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        private readonly Guid _tenantId;

        internal FixedTenantContext(Guid tenantId) => _tenantId = tenantId;

        public Guid TenantId => _tenantId;

        public bool IsResolved => true;

        public void SetTenant(Guid tenantId) =>
            throw new NotSupportedException("O tenant é fixo no contexto de teste.");
    }

    private static readonly Guid TestTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static MappingTestDbContext CreateContext() => new MappingTestDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options,
        new FixedTenantContext(TestTenantId));

    private static async Task<Student> AddStudentAsync(AppDbContext db, string nome, string turma)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            Nome = nome,
            DataNascimento = new DateTime(2015, 5, 20, 0, 0, 0, DateTimeKind.Utc),
            Turma = turma,
            AnoLetivo = 2025,
            Status = StudentStatus.Ativo,
            CreatedAt = DateTime.UtcNow,
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return student;
    }

    private static async Task<User> AddParentAsync(AppDbContext db)
    {
        var parent = new User
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            UserName = "responsavel@colegio.test",
            Email = "responsavel@colegio.test",
            Name = "Responsável Teste",
            Role = UserRole.Parent,
        };
        db.Users.Add(parent);
        await db.SaveChangesAsync();
        return parent;
    }

    private static async Task LinkAsync(AppDbContext db, Guid studentId, Guid parentId)
    {
        db.StudentParents.Add(new StudentParent { StudentId = studentId, ParentId = parentId });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateAsync_ReturnsTheSameShapeAsGetById()
    {
        await using var db = CreateContext();
        var parent = await AddParentAsync(db);
        var student = await AddStudentAsync(db, "Aluno Um", "5A");
        await LinkAsync(db, student.Id, parent.Id);

        var service = new StudentService(db);
        var updated = await service.UpdateAsync(
            student.Id,
            new UpdateStudentRequest("Aluno Um Atualizado", student.DataNascimento, null, "6B", 2026, "obs", null),
            TestTenantId,
            UserRole.Admin);

        var fetched = await service.GetByIdAsync(student.Id, TestTenantId, parent.Id, UserRole.Parent);

        // O defeito do M1: o Update mapeava a entidade sem o vínculo carregado e devolvia `parents: []`.
        Assert.Single(updated.Parents);
        Assert.Equal(parent.Id, updated.Parents[0].ParentId);
        Assert.Equal(fetched.Parents, updated.Parents);
        Assert.Equal("Aluno Um Atualizado", updated.Nome);
        Assert.Equal("6B", updated.Turma);
        Assert.Equal(2026, updated.AnoLetivo);
    }

    [Fact]
    public async Task GetDashboardAsync_ReportsEachChildStatusAndPendingDocuments()
    {
        await using var db = CreateContext();
        var parent = await AddParentAsync(db);
        var approvedChild = await AddStudentAsync(db, "Aluno Aprovado", "5A");
        var idleChild = await AddStudentAsync(db, "Aluno Sem Matrícula", "6B");
        await LinkAsync(db, approvedChild.Id, parent.Id);
        await LinkAsync(db, idleChild.Id, parent.Id);

        var period = new EnrollmentPeriod
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            Nome = "Matrícula 2025",
            DataInicio = DateTime.UtcNow.AddDays(-10),
            DataFim = DateTime.UtcNow.AddDays(20),
            AnoLetivo = 2025,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.EnrollmentPeriods.Add(period);
        db.Enrollments.Add(new Enrollment
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            StudentId = approvedChild.Id,
            EnrollmentPeriodId = period.Id,
            Status = EnrollmentStatus.Aprovado,
            CreatedAt = DateTime.UtcNow,
        });
        var docType = new DocumentType
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            Nome = "Comprovante",
            IsRequired = true,
            IsActive = true,
        };
        db.DocumentTypes.Add(docType);
        db.Documents.Add(new Document
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            StudentId = approvedChild.Id,
            DocumentTypeId = docType.Id,
            NomeArquivo = "comprovante.pdf",
            CaminhoArquivo = "/tmp/comprovante.pdf",
            Status = DocumentStatus.Pendente,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        // O dashboard não usa o serviço de documentos; o mock só satisfaz o construtor.
        var service = new ParentService(db, new Mock<IDocumentService>().Object);

        var dashboard = await service.GetDashboardAsync(parent.Id, TestTenantId);

        Assert.Equal(2, dashboard.TotalChildren);
        Assert.Equal(1, dashboard.PendingDocuments);
        Assert.Equal(1, dashboard.ActiveEnrollments);

        var approved = Assert.Single(dashboard.Children, c => c.StudentId == approvedChild.Id);
        Assert.Equal("Aprovado", approved.EnrollmentStatus);
        Assert.Equal(1, approved.PendingDocuments);

        var idle = Assert.Single(dashboard.Children, c => c.StudentId == idleChild.Id);
        Assert.Null(idle.EnrollmentStatus);
        Assert.Equal(0, idle.PendingDocuments);
    }
}
