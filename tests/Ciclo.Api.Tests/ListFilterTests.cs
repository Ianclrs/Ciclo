using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// M5 — filtros de listagem.
///
/// Dois defeitos com a mesma raiz ("entrada do usuário tratada como texto exato"):
/// 1. `Contains` vira `LIKE` no Npgsql, que é sensível a caixa — buscar "ana" não encontrava
///    "Ana Souza";
/// 2. `status` inválido era descartado por `Enum.TryParse` sem erro, e a resposta era a lista
///    INTEIRA: a tela mostrava "todos" quando o operador pediu um status que não existe.
/// </summary>
public sealed class StudentListFilterTests
{
    private static async Task<(AppDbContext Db, Guid TenantId, StudentService Service)> CreateAsync()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        student.Nome = "Ana Souza";
        student.Turma = "5A";
        await db.SaveChangesAsync();

        return (db, tenantId, new StudentService(db));
    }

    [Theory]
    [InlineData("ana")]
    [InlineData("ANA")]
    [InlineData("  Ana  ")]
    public async Task Search_IsCaseInsensitiveAndTrimmed(string search)
    {
        var (db, tenantId, service) = await CreateAsync();
        await using var _ = db;

        var result = await service.GetAllAsync(
            search, null, null, 1, 20, tenantId, Guid.NewGuid(), UserRole.Admin);

        Assert.Single(result.Items);
        Assert.Equal("Ana Souza", result.Items[0].Nome);
    }

    [Theory]
    [InlineData("5a")]
    [InlineData(" 5A ")]
    public async Task TurmaFilter_IsCaseInsensitive(string turma)
    {
        var (db, tenantId, service) = await CreateAsync();
        await using var _ = db;

        var result = await service.GetAllAsync(
            null, turma, null, 1, 20, tenantId, Guid.NewGuid(), UserRole.Admin);

        Assert.Single(result.Items);
    }

    [Theory]
    [InlineData("inexistente")]
    [InlineData("99")]
    public async Task Status_WithInvalidValue_IsRejectedInsteadOfIgnored(string status)
    {
        var (db, tenantId, service) = await CreateAsync();
        await using var _ = db;

        var ex = await Assert.ThrowsAsync<StudentException>(
            () => service.GetAllAsync(null, null, status, 1, 20, tenantId, Guid.NewGuid(), UserRole.Admin));

        Assert.Equal("status_invalid", ex.Message);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Status_WithValidValue_StillFilters()
    {
        var (db, tenantId, service) = await CreateAsync();
        await using var _ = db;

        var active = await service.GetAllAsync(
            null, null, "ativo", 1, 20, tenantId, Guid.NewGuid(), UserRole.Admin);
        var inactive = await service.GetAllAsync(
            null, null, "Inativo", 1, 20, tenantId, Guid.NewGuid(), UserRole.Admin);

        Assert.Single(active.Items);
        Assert.Empty(inactive.Items);
    }
}

/// <summary>O mesmo defeito de status na listagem de matrículas.</summary>
public sealed class EnrollmentListFilterTests
{
    private static async Task<(AppDbContext Db, Guid TenantId, EnrollmentService Service)> CreateAsync()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, 2026);
        await DocumentTestData.AddEnrollmentAsync(
            db, tenantId, student.Id, period.Id, EnrollmentStatus.Aprovado);

        return (db, tenantId, new EnrollmentService(db));
    }

    [Theory]
    [InlineData("aprovado")]
    [InlineData(" Aprovado ")]
    public async Task StatusFilter_IsCaseInsensitive(string status)
    {
        var (db, tenantId, service) = await CreateAsync();
        await using var _ = db;

        var result = await service.GetEnrollmentsAsync(
            status, null, 1, 20, tenantId, Guid.NewGuid(), UserRole.Admin);

        Assert.Single(result.Items);
        Assert.Equal("Aprovado", result.Items[0].Status);
    }

    [Theory]
    [InlineData("inexistente")]
    [InlineData("99")]
    public async Task Status_WithInvalidValue_IsRejectedInsteadOfIgnored(string status)
    {
        var (db, tenantId, service) = await CreateAsync();
        await using var _ = db;

        var ex = await Assert.ThrowsAsync<EnrollmentException>(
            () => service.GetEnrollmentsAsync(status, null, 1, 20, tenantId, Guid.NewGuid(), UserRole.Admin));

        Assert.Equal("status_invalid", ex.Message);
        Assert.Equal(400, ex.StatusCode);
    }
}
