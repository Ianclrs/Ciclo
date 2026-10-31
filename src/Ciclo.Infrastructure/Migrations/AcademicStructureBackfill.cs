namespace Ciclo.Infrastructure.Migrations;

/// <summary>
/// Spec 150 — backfill do vínculo acadêmico.
///
/// As colunas legadas <c>Students.Turma</c> e <c>Students.AnoLetivo</c> continuam sendo o dado de
/// entrada; o backfill materializa um ano por par <c>(TenantId, AnoLetivo)</c> e uma turma por
/// tripla <c>(TenantId, AnoLetivo, Turma)</c> distinta e liga cada aluno à estrutura resultante.
/// O texto vive em um símbolo estável para poder ser inspecionado por teste sem um PostgreSQL em
/// execução; a migração aplica exatamente este conteúdo.
///
/// Idempotência: cada INSERT é condicionado a <c>NOT EXISTS</c> em vez de depender de
/// <c>ON CONFLICT</c> — assim o backfill não depende de o índice único já existir no momento em
/// que roda, e reaplicá-lo não duplica linha.
/// </summary>
public static class AcademicStructureBackfill
{
    public const string Sql = """
        INSERT INTO "AcademicYears" ("Id", "TenantId", "Year", "StartDate", "EndDate", "IsActive")
        SELECT gen_random_uuid(), source."TenantId", source."AnoLetivo",
               make_date(source."AnoLetivo", 1, 1), make_date(source."AnoLetivo", 12, 31), TRUE
        FROM (
            SELECT DISTINCT "TenantId", "AnoLetivo"
            FROM "Students"
            WHERE "AnoLetivo" > 0
        ) AS source
        WHERE NOT EXISTS (
            SELECT 1 FROM "AcademicYears" existing
            WHERE existing."TenantId" = source."TenantId" AND existing."Year" = source."AnoLetivo"
        );

        INSERT INTO "SchoolClasses" ("Id", "TenantId", "Name", "AcademicYearId")
        SELECT gen_random_uuid(), source."TenantId", btrim(source."Turma"), year."Id"
        FROM (
            SELECT DISTINCT "TenantId", "AnoLetivo", btrim("Turma") AS "Turma"
            FROM "Students"
            WHERE "AnoLetivo" > 0 AND btrim("Turma") <> ''
        ) AS source
        JOIN "AcademicYears" AS year
          ON year."TenantId" = source."TenantId" AND year."Year" = source."AnoLetivo"
        WHERE NOT EXISTS (
            SELECT 1 FROM "SchoolClasses" existing
            WHERE existing."TenantId" = source."TenantId"
              AND existing."AcademicYearId" = year."Id"
              AND existing."Name" = source."Turma"
        );

        UPDATE "Students" AS student
        SET "AcademicYearId" = year."Id",
            "SchoolClassId" = school_class."Id"
        FROM "AcademicYears" AS year
        JOIN "SchoolClasses" AS school_class
          ON school_class."TenantId" = year."TenantId"
         AND school_class."AcademicYearId" = year."Id"
        WHERE year."TenantId" = student."TenantId"
          AND year."Year" = student."AnoLetivo"
          AND school_class."Name" = btrim(student."Turma")
          AND btrim(student."Turma") <> '';
        """;
}
