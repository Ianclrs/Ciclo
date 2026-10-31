using System.Reflection;
using Ciclo.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 150, FR-003 — o backfill do vínculo acadêmico. O SQL vive em um símbolo estável
/// (ADR-150-3) justamente para poder ser provado sem um PostgreSQL em execução: aqui se afirma
/// que ele materializa anos e turmas distintos, liga os alunos por junção e não remove nada; e,
/// sobre a migração gerada, que o backfill é aplicado uma única vez depois de o esquema existir.
/// </summary>
public sealed class AcademicMigrationTests
{
    private static readonly string[] AcademicTables =
    [
        "AcademicYears",
        "Assessments",
        "CurriculumComponents",
        "Grades",
        "SchoolClasses"
    ];

    private static Migration AcademicStructureMigration()
    {
        var type = typeof(AcademicStructureBackfill).Assembly
            .GetTypes()
            .Single(candidate =>
                candidate.Name == "AddAcademicStructure" && typeof(Migration).IsAssignableFrom(candidate));

        return (Migration)Activator.CreateInstance(type)!;
    }

    private static MigrationBuilder Build(Migration migration, string method)
    {
        var builder = new MigrationBuilder("Npgsql");

        typeof(Migration)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        return builder;
    }

    [Fact]
    public void Sql_MaterialisesOneYearPerTenantAndLegacyYear()
    {
        var sql = AcademicStructureBackfill.Sql;

        Assert.Contains("INSERT INTO \"AcademicYears\"", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT DISTINCT \"TenantId\", \"AnoLetivo\"", sql, StringComparison.Ordinal);
        // Ano zero ou negativo é lixo legado: não gera linha e o aluno fica sem vínculo.
        Assert.Contains("WHERE \"AnoLetivo\" > 0", sql, StringComparison.Ordinal);
        Assert.Contains("make_date(source.\"AnoLetivo\", 1, 1)", sql, StringComparison.Ordinal);
        Assert.Contains("make_date(source.\"AnoLetivo\", 12, 31)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_MaterialisesOneClassPerTenantYearAndTrimmedName()
    {
        var sql = AcademicStructureBackfill.Sql;

        Assert.Contains("INSERT INTO \"SchoolClasses\"", sql, StringComparison.Ordinal);
        // A chave da turma é (tenant, ano, nome normalizado): "5A" e " 5A " são a mesma turma.
        Assert.Contains(
            "SELECT DISTINCT \"TenantId\", \"AnoLetivo\", btrim(\"Turma\") AS \"Turma\"",
            sql,
            StringComparison.Ordinal);
        // Turma vazia ou só com espaços não cria turma sem nome.
        Assert.Contains("WHERE \"AnoLetivo\" > 0 AND btrim(\"Turma\") <> ''", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN \"AcademicYears\" AS year", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_JoinsStudentsByTenantYearAndTrimmedClassName()
    {
        var sql = AcademicStructureBackfill.Sql;

        Assert.Contains("UPDATE \"Students\"", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN \"SchoolClasses\" AS school_class", sql, StringComparison.Ordinal);
        Assert.Contains("school_class.\"Name\" = btrim(student.\"Turma\")", sql, StringComparison.Ordinal);
        Assert.Contains("AND btrim(student.\"Turma\") <> ''", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_RemovesNoColumnTableOrRow()
    {
        var sql = AcademicStructureBackfill.Sql;

        // Spec 150 é aditiva: as colunas espelho Turma/AnoLetivo ficam para a spec 160.
        Assert.DoesNotContain("DROP", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Up_CreatesTheAcademicSchemaAndAppliesTheBackfillLast()
    {
        var builder = Build(AcademicStructureMigration(), "Up");

        var createdTables = builder.Operations.OfType<CreateTableOperation>().Select(o => o.Name).OrderBy(n => n);
        Assert.Equal(AcademicTables.OrderBy(n => n), createdTables);

        // O backfill lê as tabelas e as colunas criadas acima, então precisa ser a última operação.
        var backfill = Assert.Single(builder.Operations.OfType<SqlOperation>());
        Assert.Equal(AcademicStructureBackfill.Sql, backfill.Sql);
        Assert.Same(backfill, builder.Operations[^1]);

        // Nenhuma remoção na subida: a migração é aditiva.
        Assert.Empty(builder.Operations.OfType<DropColumnOperation>());
        Assert.Empty(builder.Operations.OfType<DropTableOperation>());
    }

    [Fact]
    public void Down_RemovesOnlyTheNewColumnsAndTables()
    {
        var builder = Build(AcademicStructureMigration(), "Down");

        var droppedColumns = builder.Operations.OfType<DropColumnOperation>().Select(o => o.Name).OrderBy(n => n);
        Assert.Equal(["AcademicYearId", "SchoolClassId"], droppedColumns);

        // As colunas legadas são o dado original do aluno; o rollback não pode destruí-las.
        Assert.DoesNotContain("Turma", droppedColumns);
        Assert.DoesNotContain("AnoLetivo", droppedColumns);

        var droppedTables = builder.Operations.OfType<DropTableOperation>().Select(o => o.Name).OrderBy(n => n);
        Assert.Equal(AcademicTables.OrderBy(n => n), droppedTables);
    }

    [Fact]
    public void TheModelSnapshotContainsTheFiveAcademicTables()
    {
        var snapshotPath = Path.Combine(
            RepositoryRoot.Find(), "src", "Ciclo.Infrastructure", "Migrations", "AppDbContextModelSnapshot.cs");

        var snapshot = File.ReadAllText(snapshotPath);

        foreach (var table in AcademicTables)
            Assert.Contains($"b.ToTable(\"{table}\");", snapshot, StringComparison.Ordinal);

        // O espelho do aluno continua no modelo: removê-lo antes da spec 160 quebraria o contrato.
        Assert.Contains("b.Property<string>(\"Turma\")", snapshot, StringComparison.Ordinal);
        Assert.Contains("b.Property<int>(\"AnoLetivo\")", snapshot, StringComparison.Ordinal);
    }
}
