using Ciclo.Core.Entities;
using Ciclo.Core.Exceptions;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 150, task 5 — o boletim no portal dos pais. `GetChildGradesAsync` deixou de devolver
/// lista vazia: agora deriva da turma vinculada, e a média passa pelo dono único
/// (`GradeCalculation`). A autorização continua sendo o vínculo responsável-aluno.
/// </summary>
public sealed class ParentReportCardTests
{
    private sealed class ReportCardTestDbContext : AppDbContext
    {
        internal ReportCardTestDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
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

    private static readonly Guid TestTenantId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static ReportCardTestDbContext CreateContext() => new ReportCardTestDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options,
        new FixedTenantContext(TestTenantId));

    private static ParentService CreateService(AppDbContext db) =>
        new(db, new Mock<IDocumentService>().Object);

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

    private static async Task<(Student Student, SchoolClass Class)> AddStudentWithClassAsync(AppDbContext db)
    {
        var year = new AcademicYear
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            Year = 2026,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
        };
        db.AcademicYears.Add(year);

        var schoolClass = new SchoolClass
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            Name = "5A",
            AcademicYearId = year.Id,
        };
        db.SchoolClasses.Add(schoolClass);

        var student = new Student
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            Nome = "Aluno Teste",
            DataNascimento = new DateTime(2015, 5, 20, 0, 0, 0, DateTimeKind.Utc),
            Turma = "5A",
            AnoLetivo = 2026,
            AcademicYearId = year.Id,
            SchoolClassId = schoolClass.Id,
            Status = StudentStatus.Ativo,
            CreatedAt = DateTime.UtcNow,
        };
        db.Students.Add(student);

        await db.SaveChangesAsync();
        return (student, schoolClass);
    }

    private static async Task<CurriculumComponent> AddComponentAsync(AppDbContext db, string name, string code)
    {
        var component = new CurriculumComponent
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            Name = name,
            Code = code,
        };
        db.CurriculumComponents.Add(component);
        await db.SaveChangesAsync();
        return component;
    }

    private static async Task<Assessment> AddAssessmentAsync(
        AppDbContext db, Guid classId, Guid componentId, string title, DateTime date, bool recovery = false)
    {
        var assessment = new Assessment
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            SchoolClassId = classId,
            CurriculumComponentId = componentId,
            Title = title,
            Weight = 1m,
            Date = DateOnly.FromDateTime(date),
            IsRecovery = recovery,
        };
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        return assessment;
    }

    private static async Task AddGradeAsync(AppDbContext db, Guid assessmentId, Guid studentId, decimal value)
    {
        db.Grades.Add(new Grade
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            AssessmentId = assessmentId,
            StudentId = studentId,
            Value = value,
            RecordedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetChildReportCardAsync_ComputesWeightedAverageRecoveryAndSituation()
    {
        await using var db = CreateContext();
        var parent = await AddParentAsync(db);
        var (student, schoolClass) = await AddStudentWithClassAsync(db);
        db.StudentParents.Add(new StudentParent { StudentId = student.Id, ParentId = parent.Id });
        await db.SaveChangesAsync();

        var math = await AddComponentAsync(db, "Matemática", "MAT");
        var history = await AddComponentAsync(db, "História", "HIS");

        var first = await AddAssessmentAsync(db, schoolClass.Id, math.Id, "Prova 1", new DateTime(2026, 3, 10));
        var second = await AddAssessmentAsync(db, schoolClass.Id, math.Id, "Prova 2", new DateTime(2026, 5, 10));
        var recovery = await AddAssessmentAsync(db, schoolClass.Id, math.Id, "Recuperação", new DateTime(2026, 7, 10), recovery: true);
        await AddAssessmentAsync(db, schoolClass.Id, history.Id, "Prova única", new DateTime(2026, 4, 10));

        await AddGradeAsync(db, first.Id, student.Id, 8m);
        await AddGradeAsync(db, second.Id, student.Id, 6m);
        await AddGradeAsync(db, recovery.Id, student.Id, 5m);

        var reportCard = await CreateService(db)
            .GetChildReportCardAsync(parent.Id, student.Id, TestTenantId);

        Assert.Equal("5A", reportCard.Turma);
        Assert.Equal(2026, reportCard.AnoLetivo);
        Assert.Equal(2, reportCard.Componentes.Count);

        var mathEntry = Assert.Single(reportCard.Componentes, entry => entry.Componente == "Matemática");
        Assert.Equal(7m, mathEntry.MediaRegular);
        Assert.Equal(5m, mathEntry.NotaRecuperacao);
        // A recuperação nunca reduz: final = max(7, 5) = 7.
        Assert.Equal(7m, mathEntry.MediaFinal);
        Assert.Equal("aprovado", mathEntry.Situacao);
        Assert.Equal(3, mathEntry.Avaliacoes.Count);

        var historyEntry = Assert.Single(reportCard.Componentes, entry => entry.Componente == "História");
        Assert.Null(historyEntry.MediaRegular);
        Assert.Null(historyEntry.MediaFinal);
        Assert.Equal("sem_nota", historyEntry.Situacao);
        Assert.Null(Assert.Single(historyEntry.Avaliacoes).Nota);
    }

    [Fact]
    public async Task GetChildGradesAsync_ReturnsOneRowPerComponentWithTheFinalAverage()
    {
        await using var db = CreateContext();
        var parent = await AddParentAsync(db);
        var (student, schoolClass) = await AddStudentWithClassAsync(db);
        db.StudentParents.Add(new StudentParent { StudentId = student.Id, ParentId = parent.Id });
        await db.SaveChangesAsync();

        var math = await AddComponentAsync(db, "Matemática", "MAT");
        var assessment = await AddAssessmentAsync(db, schoolClass.Id, math.Id, "Prova 1", new DateTime(2026, 3, 10));
        await AddGradeAsync(db, assessment.Id, student.Id, 9m);

        var grades = await CreateService(db).GetChildGradesAsync(parent.Id, student.Id, TestTenantId);

        var grade = Assert.Single(grades);
        Assert.Equal("Matemática", grade.Disciplina);
        Assert.Equal(9m, grade.Nota);
        Assert.Null(grade.Observacoes);
    }

    [Fact]
    public async Task GetChildReportCardAsync_WithoutClass_ReturnsAnEmptyReportWithoutThrowing()
    {
        await using var db = CreateContext();
        var parent = await AddParentAsync(db);

        var student = new Student
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            Nome = "Aluno Sem Turma",
            DataNascimento = new DateTime(2015, 5, 20, 0, 0, 0, DateTimeKind.Utc),
            Turma = "7C",
            AnoLetivo = 2026,
            Status = StudentStatus.Ativo,
            CreatedAt = DateTime.UtcNow,
        };
        db.Students.Add(student);
        db.StudentParents.Add(new StudentParent { StudentId = student.Id, ParentId = parent.Id });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var reportCard = await service.GetChildReportCardAsync(parent.Id, student.Id, TestTenantId);
        var grades = await service.GetChildGradesAsync(parent.Id, student.Id, TestTenantId);

        // Sem vínculo por referência não há turma de onde ler avaliação — e isso não é erro.
        Assert.Null(reportCard.Turma);
        Assert.Null(reportCard.AnoLetivo);
        Assert.Empty(reportCard.Componentes);
        Assert.Empty(grades);
    }

    [Fact]
    public async Task GetChildReportCardAsync_WithoutParentLink_ThrowsBeforeReadingAnyGrade()
    {
        await using var db = CreateContext();
        var parent = await AddParentAsync(db);
        var (student, _) = await AddStudentWithClassAsync(db);

        var service = CreateService(db);

        await Assert.ThrowsAsync<AccessDeniedException>(
            () => service.GetChildReportCardAsync(parent.Id, student.Id, TestTenantId));
        await Assert.ThrowsAsync<AccessDeniedException>(
            () => service.GetChildGradesAsync(parent.Id, student.Id, TestTenantId));
    }
}
