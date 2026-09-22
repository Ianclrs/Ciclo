using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Storage;
using Ciclo.Infrastructure.Tenancy;

#pragma warning disable CA1848, CA1873 // LoggerMessage/expensive-arg — acceptable for seeders

namespace Ciclo.Infrastructure.Data.Seeders;

/// <summary>
/// Seeds a complete demo dataset for local development and testing:
/// one fake school (tenant), users for each role, students linked to parents,
/// an enrollment period, enrollments, document types and sample documents.
/// Credentials are logged so they can be used to log in.
/// </summary>
public static class DemoDataSeeder
{
    public const string DemoSlug = "colegio-modelo";
    public const string DemoPassword = "Admin@123";

    public static async Task SeedAsync(
        IServiceProvider services,
        AppDbContext db,
        IHostEnvironment env,
        ILogger logger)
    {
        if (!env.IsDevelopment())
        {
            logger.LogInformation("DemoDataSeeder: Skipping — not Development environment");
            return;
        }

        if (await db.Tenants.AnyAsync(t => t.Slug == DemoSlug))
        {
            logger.LogInformation("DemoDataSeeder: Demo school '{Slug}' already exists, skipping", DemoSlug);
            return;
        }

        var userManager = services.GetRequiredService<UserManager<User>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        // 1. Roles
        await EnsureRoleAsync(roleManager, "Admin", logger);
        await EnsureRoleAsync(roleManager, "Staff", logger);
        await EnsureRoleAsync(roleManager, "Parent", logger);

        // 2. Tenant (school)
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Colégio Modelo",
            Slug = DemoSlug,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        // Resolve the tenant context so global query filters (on User, Student, etc.)
        // work during seeding — UserManager internally queries the Users table.
        services.GetRequiredService<TenantContext>().SetTenant(tenant.Id);

        // 3. Users (credentials are logged at the end)
        var admin = await CreateUserAsync(userManager, tenant, UserRole.Admin,
            "admin@colegiomodelo.com", DemoPassword, "Diretor Demo");
        var staff = await CreateUserAsync(userManager, tenant, UserRole.Staff,
            "secretaria@colegiomodelo.com", DemoPassword, "Secretária Demo");
        var parentPaulo = await CreateUserAsync(userManager, tenant, UserRole.Parent,
            "paulo.silva@email.com", DemoPassword, "Paulo Silva");
        var parentMaria = await CreateUserAsync(userManager, tenant, UserRole.Parent,
            "maria.souza@email.com", DemoPassword, "Maria Souza");

        // 4. Students
        var students = new List<Student>
        {
            CreateStudent(tenant.Id, "João Pedro Silva", new DateTime(2016, 3, 15), "123.456.789-01", "1º Ano A", 2026),
            CreateStudent(tenant.Id, "Ana Clara Silva", new DateTime(2017, 7, 22), "123.456.789-02", "1º Ano A", 2026),
            CreateStudent(tenant.Id, "Lucas Oliveira Souza", new DateTime(2015, 11, 8), "123.456.789-03", "2º Ano B", 2026),
            CreateStudent(tenant.Id, "Beatriz Souza", new DateTime(2016, 1, 30), "123.456.789-04", "2º Ano B", 2026),
            CreateStudent(tenant.Id, "Miguel Santos", new DateTime(2014, 9, 12), "123.456.789-05", "3º Ano C", 2026),
            CreateStudent(tenant.Id, "Laura Costa", new DateTime(2015, 4, 5), "123.456.789-06", "3º Ano C", 2026),
        };
        db.Students.AddRange(students);
        await db.SaveChangesAsync();

        // 5. Parent <-> Student links
        db.StudentParents.AddRange(
            new StudentParent { StudentId = students[0].Id, ParentId = parentPaulo.Id },
            new StudentParent { StudentId = students[1].Id, ParentId = parentPaulo.Id },
            new StudentParent { StudentId = students[2].Id, ParentId = parentMaria.Id },
            new StudentParent { StudentId = students[3].Id, ParentId = parentMaria.Id });
        await db.SaveChangesAsync();

        // 6. Enrollment period + enrollments
        var period = new EnrollmentPeriod
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Matrículas 2026",
            DataInicio = new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            DataFim = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc),
            AnoLetivo = 2026,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.EnrollmentPeriods.Add(period);
        await db.SaveChangesAsync();

        db.Enrollments.AddRange(
            CreateEnrollment(tenant.Id, students[0].Id, period.Id, EnrollmentStatus.Aprovado, approvedAt: DateTime.UtcNow),
            CreateEnrollment(tenant.Id, students[1].Id, period.Id, EnrollmentStatus.Pendente),
            CreateEnrollment(tenant.Id, students[2].Id, period.Id, EnrollmentStatus.DocumentacaoPendente),
            CreateEnrollment(tenant.Id, students[3].Id, period.Id, EnrollmentStatus.Aprovado, approvedAt: DateTime.UtcNow),
            CreateEnrollment(tenant.Id, students[4].Id, period.Id, EnrollmentStatus.Rejeitado, motivo: "Documentação incompleta"),
            CreateEnrollment(tenant.Id, students[5].Id, period.Id, EnrollmentStatus.Pendente));
        await db.SaveChangesAsync();

        // 7. Document types
        var docTypeRg = new DocumentType { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "RG", Descricao = "Registro Geral (identidade)", IsRequired = true, ValidadeMeses = 0, IsActive = true };
        var docTypeCpf = new DocumentType { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "CPF", Descricao = "Cadastro de Pessoa Física", IsRequired = true, ValidadeMeses = 0, IsActive = true };
        var docTypeResidencia = new DocumentType { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Comprovante de Residência", Descricao = "Conta de luz, água ou telefone recente", IsRequired = true, ValidadeMeses = 12, IsActive = true };
        var docTypeHistorico = new DocumentType { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Histórico Escolar", Descricao = "Histórico da escola anterior", IsRequired = true, ValidadeMeses = 0, IsActive = true };
        db.DocumentTypes.AddRange(docTypeRg, docTypeCpf, docTypeResidencia, docTypeHistorico);
        await db.SaveChangesAsync();

        // 8. Sample documents (grava arquivos reais para o download funcionar)
        var fileStorage = services.GetRequiredService<IFileStorage>();
        var sampleDocuments = new (Student Student, DocumentType Type, string FileName, DocumentStatus Status, DateTime? VerifiedAt, string? Motivo)[]
        {
            (students[0], docTypeRg, "joao-rg.pdf", DocumentStatus.Aprovado, DateTime.UtcNow, null),
            (students[0], docTypeCpf, "joao-cpf.pdf", DocumentStatus.Aprovado, DateTime.UtcNow, null),
            (students[1], docTypeRg, "ana-rg.pdf", DocumentStatus.Pendente, null, null),
            (students[2], docTypeResidencia, "lucas-residencia.pdf", DocumentStatus.Pendente, null, null),
            (students[4], docTypeHistorico, "miguel-historico.pdf", DocumentStatus.Rejeitado, null, "Arquivo ilegível"),
        };

        foreach (var (student, type, fileName, status, verifiedAt, motivo) in sampleDocuments)
        {
            var caminhoArquivo = await fileStorage.SaveAsync(
                tenant.Id, student.Id, fileName, new MemoryStream(PlaceholderPdfBytes(fileName)));

            db.Documents.Add(new Document
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                StudentId = student.Id,
                DocumentTypeId = type.Id,
                NomeArquivo = fileName,
                CaminhoArquivo = caminhoArquivo,
                Status = status,
                MotivoRejeicao = motivo,
                VerifiedAt = verifiedAt,
                CreatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        // 9. Log credentials
        logger.LogInformation("══════════════════════════════════════════════════════════");
        logger.LogInformation(" Demo data seeded — school: {School} (slug: {Slug})", tenant.Name, tenant.Slug);
        logger.LogInformation(" Login credentials (all use password: {Password}):", DemoPassword);
        logger.LogInformation("   Admin   : {Email} (role: Admin)", admin.Email);
        logger.LogInformation("   Staff   : {Email} (role: Staff)", staff.Email);
        logger.LogInformation("   Parent  : {Email} (role: Parent)", parentPaulo.Email);
        logger.LogInformation("   Parent  : {Email} (role: Parent)", parentMaria.Email);
        logger.LogInformation(" Students created: {Count}", students.Count);
        logger.LogInformation("══════════════════════════════════════════════════════════");
    }

    private static async Task EnsureRoleAsync(
        RoleManager<IdentityRole<Guid>> roleManager, string roleName, ILogger logger)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            logger.LogInformation("DemoDataSeeder: Created role '{Role}'", roleName);
        }
    }

    private static async Task<User> CreateUserAsync(
        UserManager<User> userManager,
        Tenant tenant,
        UserRole role,
        string email,
        string password,
        string name)
    {
        var user = new User
        {
            TenantId = tenant.Id,
            Email = email,
            UserName = email,
            Name = name,
            Role = role,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to create user {email}: {errors}");
        }

        await userManager.AddToRoleAsync(user, role.ToString());
        return user;
    }

    private static Student CreateStudent(
        Guid tenantId, string nome, DateTime dataNascimento, string cpf, string turma, int anoLetivo)
    {
        return new Student
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = nome,
            DataNascimento = DateTime.SpecifyKind(dataNascimento, DateTimeKind.Utc),
            Cpf = cpf,
            Turma = turma,
            AnoLetivo = anoLetivo,
            Status = StudentStatus.Ativo,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static Enrollment CreateEnrollment(
        Guid tenantId, Guid studentId, Guid periodId, EnrollmentStatus status,
        DateTime? approvedAt = null, string? motivo = null)
    {
        return new Enrollment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StudentId = studentId,
            EnrollmentPeriodId = periodId,
            Status = status,
            MotivoRejeicao = motivo,
            ApprovedAt = approvedAt,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static byte[] PlaceholderPdfBytes(string fileName)
    {
        return Encoding.UTF8.GetBytes(
            $"%PDF-1.4\n% Documento de exemplo gerado pelo seeder: {fileName}\n% Conteudo ficticio para fins de demonstracao.\n%%EOF\n");
    }
}

#pragma warning restore CA1848, CA1873
