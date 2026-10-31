using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 150, task 1 — o caminho de escrita de aluno resolve turma e ano em entidades e mantém
/// as colunas espelho (`Turma`, `AnoLetivo`) preenchidas. O contrato de `StudentDto` não muda.
/// </summary>
public sealed class AcademicStructureTests
{
    private sealed class AcademicTestDbContext : AppDbContext
    {
        internal AcademicTestDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
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

    private static readonly Guid TestTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static AcademicTestDbContext CreateContext() => new AcademicTestDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options,
        new FixedTenantContext(TestTenantId));

    private static CreateStudentRequest Request(string turma, int anoLetivo) =>
        new("Aluno Teste", new DateTime(2015, 5, 20, 0, 0, 0, DateTimeKind.Utc), null, turma, anoLetivo, null, null);

    [Fact]
    public async Task CreateAsync_ResolvesYearAndClassAndKeepsTheMirrorColumns()
    {
        await using var db = CreateContext();
        var service = new StudentService(db);

        var created = await service.CreateAsync(Request(" 5A ", 2026), TestTenantId, UserRole.Admin);

        var year = await db.AcademicYears.SingleAsync();
        var schoolClass = await db.SchoolClasses.SingleAsync();
        var student = await db.Students.SingleAsync(s => s.Id == created.Id);

        Assert.Equal(2026, year.Year);
        Assert.Equal(new DateOnly(2026, 1, 1), year.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 31), year.EndDate);

        // A turma é normalizada: " 5A " não pode virar uma segunda turma chamada " 5A ".
        Assert.Equal("5A", schoolClass.Name);
        Assert.Equal(year.Id, schoolClass.AcademicYearId);

        Assert.Equal(year.Id, student.AcademicYearId);
        Assert.Equal(schoolClass.Id, student.SchoolClassId);

        // O espelho continua servindo o contrato atual da tela.
        Assert.Equal("5A", created.Turma);
        Assert.Equal(2026, created.AnoLetivo);
    }

    [Fact]
    public async Task CreateAsync_ReusesTheYearAndClassForASecondStudent()
    {
        await using var db = CreateContext();
        var service = new StudentService(db);

        await service.CreateAsync(Request("5A", 2026), TestTenantId, UserRole.Admin);
        await service.CreateAsync(Request("5A", 2026), TestTenantId, UserRole.Admin);

        Assert.Equal(1, await db.AcademicYears.CountAsync());
        Assert.Equal(1, await db.SchoolClasses.CountAsync());
        Assert.Equal(2, await db.Students.CountAsync());
        var classIds = await db.Students.Select(s => s.SchoolClassId).ToListAsync();
        Assert.Single(classIds.Distinct());
    }

    [Fact]
    public async Task UpdateAsync_MovesTheStudentToTheNewClassAndYear()
    {
        await using var db = CreateContext();
        var service = new StudentService(db);

        var created = await service.CreateAsync(Request("5A", 2026), TestTenantId, UserRole.Admin);
        var originalClassId = (await db.Students.SingleAsync(s => s.Id == created.Id)).SchoolClassId;

        var updated = await service.UpdateAsync(
            created.Id,
            new UpdateStudentRequest("Aluno Teste", new DateTime(2015, 5, 20, 0, 0, 0, DateTimeKind.Utc), null, "6B", 2027, null, null),
            TestTenantId,
            UserRole.Admin);

        var student = await db.Students.SingleAsync(s => s.Id == created.Id);
        var movedClass = await db.SchoolClasses.SingleAsync(c => c.Name == "6B");

        Assert.Equal("6B", updated.Turma);
        Assert.Equal(movedClass.Id, student.SchoolClassId);
        Assert.NotEqual(originalClassId, student.SchoolClassId);

        // A turma antiga permanece: mudar de turma não apaga o histórico da estrutura.
        Assert.Equal(2, await db.SchoolClasses.CountAsync());
        Assert.Equal(2, await db.AcademicYears.CountAsync());
    }
}
