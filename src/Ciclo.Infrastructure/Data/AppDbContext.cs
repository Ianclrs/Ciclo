using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Ciclo.Core.Entities;
using Ciclo.Core.Exceptions;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Auditing;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{
    private readonly ITenantContext _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<UserInvitation> Invitations { get; set; } = null!;
    public DbSet<Student> Students { get; set; } = null!;
    public DbSet<StudentParent> StudentParents { get; set; } = null!;
    public DbSet<EnrollmentPeriod> EnrollmentPeriods { get; set; } = null!;
    public DbSet<Enrollment> Enrollments { get; set; } = null!;
    public DbSet<DocumentType> DocumentTypes { get; set; } = null!;
    public DbSet<Document> Documents { get; set; } = null!;
    public DbSet<Notification> Notifications { get; set; } = null!;
    public DbSet<UserNotification> UserNotifications { get; set; } = null!;
    public DbSet<AuditEvent> AuditEvents { get; set; } = null!;
    public DbSet<NotificationDelivery> NotificationDeliveries { get; set; } = null!;
    public DbSet<AcademicYear> AcademicYears { get; set; } = null!;
    public DbSet<SchoolClass> SchoolClasses { get; set; } = null!;
    public DbSet<CurriculumComponent> CurriculumComponents { get; set; } = null!;
    public DbSet<Assessment> Assessments { get; set; } = null!;
    public DbSet<Grade> Grades { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Tenant entity configuration
        builder.Entity<Tenant>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => t.Slug).IsUnique();
            entity.Property(t => t.Name).HasMaxLength(200).IsRequired();
            entity.Property(t => t.Slug).HasMaxLength(100).IsRequired();
            entity.Property(t => t.Country).HasMaxLength(2).IsRequired();
            entity.Property(t => t.DocumentoFiscal).HasMaxLength(30);
            entity.Property(t => t.Telefone).HasMaxLength(30);
            entity.Property(t => t.Endereco).HasMaxLength(200);
            entity.Property(t => t.Cidade).HasMaxLength(100);
            entity.Property(t => t.Estado).HasMaxLength(100);
            entity.Property(t => t.Cep).HasMaxLength(20);
            entity.Property(t => t.IsActive).HasDefaultValue(true);
            entity.Property(t => t.CreatedAt).HasDefaultValueSql("now()");
        });

        // User entity configuration (IdentityUser<Guid> customizations)
        builder.Entity<User>(entity =>
        {
            // Rename Identity default table from AspNetUsers to Users
            entity.ToTable("Users");

            // Custom properties
            entity.Property(u => u.Name).HasMaxLength(200).IsRequired();
            entity.Property(u => u.Role).HasConversion<int>().IsRequired();
            entity.Property(u => u.GoogleId).HasMaxLength(256);
            entity.Property(u => u.IsActive).HasDefaultValue(true);
            entity.Property(u => u.CreatedAt).HasDefaultValueSql("now()");

            // Tenant relationship
            entity.HasOne(u => u.Tenant)
                .WithMany()
                .HasForeignKey(u => u.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            // E-mail único GLOBALMENTE, não por tenant: o login resolve o usuário só pelo e-mail,
            // sem tenant (ver EmailUniqueness). O índice composto (TenantId, Email) não sustentava
            // essa consulta e permitia o mesmo e-mail em duas escolas — o login então escolhia
            // uma linha arbitrária.
            //
            // O índice faz parte da migration inicial (ver Migrations/ e MigrationHistoryTests), então
            // o schema criado a partir de agora já nasce com ele.
            //
            // Banco criado ANTES do A1 (pelo antigo EnsureCreated) não o tem — esse schema não tem
            // como ser atualizado, porque EnsureCreated nunca alterava tabela existente. Para esses,
            // ou recria-se o banco, ou roda-se uma vez:
            //   CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");
            // Se houver duplicatas antigas, a criação FALHA — que é o momento certo de resolver a
            // ambiguidade em vez de conviver com ela (era justamente o defeito do C2).
            entity.HasIndex(u => u.Email).IsUnique();

            // Unique filtered index on GoogleId (where not null)
            entity.HasIndex(u => u.GoogleId)
                .IsUnique()
                .HasFilter("\"GoogleId\" IS NOT NULL");
        });

        // RefreshToken entity configuration
        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(rt => rt.Id);
            entity.Property(rt => rt.Token).HasMaxLength(128).IsRequired();
            entity.Property(rt => rt.CreatedAt).HasDefaultValueSql("now()");

            entity.HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserInvitation entity configuration (Spec 120)
        builder.Entity<UserInvitation>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.TokenHash).HasMaxLength(64).IsRequired();

            // Único por hash: um convite é um segredo de uso único, e o hash só pode apontar
            // para uma linha — aceitar o token tem de resolver exatamente um convite.
            entity.HasIndex(i => i.TokenHash).IsUnique();
            entity.HasIndex(i => i.UserId);

            entity.HasOne(i => i.User)
                .WithMany()
                .HasForeignKey(i => i.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Student entity configuration
        builder.Entity<Student>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Nome).HasMaxLength(200).IsRequired();
            entity.Property(s => s.Cpf).HasMaxLength(14);
            entity.Property(s => s.Turma).HasMaxLength(50).IsRequired();
            entity.Property(s => s.Observacoes).HasMaxLength(1000);
            // text: sem limite no banco; o tamanho da foto é validado na aplicação.
            entity.Property(s => s.Foto).HasColumnType("text");
            entity.Property(s => s.Status).HasConversion<int>();
            entity.Property(s => s.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(s => s.UpdatedAt).HasDefaultValueSql("now()");

            entity.HasIndex(s => s.TenantId);
            entity.HasIndex(s => new { s.TenantId, s.Nome });
            entity.HasIndex(s => new { s.TenantId, s.Cpf }).IsUnique().HasFilter("\"Cpf\" IS NOT NULL");

            entity.HasOne(s => s.Tenant)
                .WithMany()
                .HasForeignKey(s => s.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            // Spec 150 — vínculo acadêmico por referência. Restrict nas duas chaves: excluir
            // uma turma ou um ano em uso seria perder a identidade dos alunos pendurados nele.
            entity.HasOne(s => s.AcademicYear)
                .WithMany()
                .HasForeignKey(s => s.AcademicYearId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(s => s.SchoolClass)
                .WithMany(c => c.Students)
                .HasForeignKey(s => s.SchoolClassId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // AcademicYear configuration (Spec 150)
        builder.Entity<AcademicYear>(entity =>
        {
            entity.HasKey(y => y.Id);
            entity.Property(y => y.Year).IsRequired();
            entity.Property(y => y.IsActive).HasDefaultValue(true);

            entity.HasIndex(y => new { y.TenantId, y.Year }).IsUnique();
        });

        // SchoolClass configuration (Spec 150)
        builder.Entity<SchoolClass>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).HasMaxLength(50).IsRequired();

            entity.HasIndex(c => new { c.TenantId, c.AcademicYearId, c.Name }).IsUnique();

            entity.HasOne(c => c.AcademicYear)
                .WithMany(y => y.Classes)
                .HasForeignKey(c => c.AcademicYearId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // CurriculumComponent configuration (Spec 150)
        builder.Entity<CurriculumComponent>(entity =>
        {
            entity.HasKey(cc => cc.Id);
            entity.Property(cc => cc.Name).HasMaxLength(100).IsRequired();
            entity.Property(cc => cc.Code).HasMaxLength(20).IsRequired();
            entity.Property(cc => cc.IsActive).HasDefaultValue(true);

            entity.HasIndex(cc => new { cc.TenantId, cc.Code }).IsUnique();
        });

        // Assessment configuration (Spec 150)
        builder.Entity<Assessment>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Title).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Weight).HasPrecision(5, 2).HasDefaultValue(1m);

            entity.HasIndex(a => new { a.TenantId, a.SchoolClassId, a.CurriculumComponentId, a.Title })
                .IsUnique();

            entity.HasOne(a => a.SchoolClass)
                .WithMany()
                .HasForeignKey(a => a.SchoolClassId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.CurriculumComponent)
                .WithMany()
                .HasForeignKey(a => a.CurriculumComponentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Grade configuration (Spec 150)
        builder.Entity<Grade>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Value).HasPrecision(5, 2);

            // Chave natural do lançamento: é o que torna o upsert idempotente.
            entity.HasIndex(g => new { g.AssessmentId, g.StudentId }).IsUnique();

            entity.HasOne(g => g.Assessment)
                .WithMany()
                .HasForeignKey(g => g.AssessmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(g => g.Student)
                .WithMany()
                .HasForeignKey(g => g.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // StudentParent join entity configuration
        builder.Entity<StudentParent>(entity =>
        {
            entity.HasKey(sp => new { sp.StudentId, sp.ParentId });

            entity.HasOne(sp => sp.Student)
                .WithMany(s => s.StudentParents)
                .HasForeignKey(sp => sp.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(sp => sp.Parent)
                .WithMany()
                .HasForeignKey(sp => sp.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // EnrollmentPeriod configuration
        builder.Entity<EnrollmentPeriod>(entity =>
        {
            entity.HasKey(ep => ep.Id);
            entity.Property(ep => ep.Nome).HasMaxLength(200).IsRequired();
            entity.Property(ep => ep.IsActive).HasDefaultValue(true);
            entity.Property(ep => ep.CreatedAt).HasDefaultValueSql("now()");

            entity.HasIndex(ep => ep.TenantId);
            entity.HasIndex(ep => new { ep.TenantId, ep.AnoLetivo });

            entity.HasOne(ep => ep.Tenant)
                .WithMany()
                .HasForeignKey(ep => ep.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Enrollment configuration
        builder.Entity<Enrollment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MotivoRejeicao).HasMaxLength(500);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.StudentId);
            entity.HasIndex(e => new { e.TenantId, e.Status });

            entity.HasOne(e => e.Student)
                .WithMany()
                .HasForeignKey(e => e.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Period)
                .WithMany(p => p.Enrollments)
                .HasForeignKey(e => e.EnrollmentPeriodId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // DocumentType configuration
        builder.Entity<DocumentType>(entity =>
        {
            entity.HasKey(dt => dt.Id);
            entity.Property(dt => dt.Nome).HasMaxLength(200).IsRequired();
            entity.Property(dt => dt.IsRequired).HasDefaultValue(true);
            entity.Property(dt => dt.IsActive).HasDefaultValue(true);

            entity.HasIndex(dt => dt.TenantId);

            entity.HasOne(dt => dt.Tenant)
                .WithMany()
                .HasForeignKey(dt => dt.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Document configuration
        builder.Entity<Document>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.NomeArquivo).HasMaxLength(500).IsRequired();
            entity.Property(d => d.CaminhoArquivo).HasMaxLength(1000).IsRequired();
            entity.Property(d => d.MotivoRejeicao).HasMaxLength(500);
            entity.Property(d => d.Status).HasConversion<int>();
            entity.Property(d => d.CreatedAt).HasDefaultValueSql("now()");

            entity.HasIndex(d => d.TenantId);
            entity.HasIndex(d => d.StudentId);
            entity.HasIndex(d => new { d.TenantId, d.Status });

            entity.HasOne(d => d.Student)
                .WithMany()
                .HasForeignKey(d => d.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.DocumentType)
                .WithMany()
                .HasForeignKey(d => d.DocumentTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.Tenant)
                .WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Notification configuration (Spec 80)
        builder.Entity<Notification>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Titulo).HasMaxLength(200).IsRequired();
            entity.Property(n => n.Mensagem).HasMaxLength(2000).IsRequired();
            entity.Property(n => n.Tipo).HasConversion<int>();
            entity.Property(n => n.CreatedAt).HasDefaultValueSql("now()");

            entity.HasIndex(n => n.TenantId);

            entity.HasOne(n => n.Tenant)
                .WithMany()
                .HasForeignKey(n => n.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // UserNotification configuration (Spec 80)
        builder.Entity<UserNotification>(entity =>
        {
            entity.HasKey(un => un.Id);

            entity.HasIndex(un => new { un.UserId, un.IsRead });

            entity.HasOne(un => un.Notification)
                .WithMany(n => n.UserNotifications)
                .HasForeignKey(un => un.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(un => un.User)
                .WithMany()
                .HasForeignKey(un => un.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Rename Identity role table
        builder.Entity<IdentityRole<Guid>>(entity =>
        {
            entity.ToTable("Roles");
        });

        // AuditEvent configuration (Spec 110)
        builder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Action).HasMaxLength(64).IsRequired();
            entity.Property(e => e.TargetType).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ActorRole).HasMaxLength(32);
            entity.Property(e => e.IpAddress).HasMaxLength(64);
            entity.Property(e => e.UserAgent).HasMaxLength(512);
            entity.Property(e => e.CorrelationId).HasMaxLength(64);
            entity.Property(e => e.BeforeJson).HasColumnType("jsonb");
            entity.Property(e => e.AfterJson).HasColumnType("jsonb");
            entity.Property(e => e.OccurredAt).HasDefaultValueSql("now()");

            entity.HasIndex(e => new { e.TenantId, e.OccurredAt });
            entity.HasIndex(e => new { e.TenantId, e.Action, e.OccurredAt });
            entity.HasIndex(e => new { e.TenantId, e.TargetType, e.TargetId });

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // NotificationDelivery configuration (Spec 140)
        builder.Entity<NotificationDelivery>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.IdempotencyKey).HasMaxLength(200).IsRequired();
            entity.Property(d => d.Erro).HasMaxLength(500);
            entity.Property(d => d.Kind).HasConversion<int>();
            entity.Property(d => d.Channel).HasConversion<int>();
            entity.Property(d => d.State).HasConversion<int>();

            // Chave natural da entrega: o índice único é o árbitro da idempotência,
            // inclusive entre duas instâncias que processem o mesmo gatilho.
            entity.HasIndex(d => new { d.TenantId, d.IdempotencyKey, d.RecipientUserId, d.Channel })
                .IsUnique()
                .HasDatabaseName("IX_NotificationDeliveries_Idempotency");
            entity.HasIndex(d => new { d.RecipientUserId, d.CreatedAt })
                .HasDatabaseName("IX_NotificationDeliveries_Recipient");

            entity.HasOne(d => d.Tenant)
                .WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Apply global query filter to all ITenantScoped entities
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var tenantIdProperty = Expression.Property(parameter, nameof(ITenantScoped.TenantId));
                var currentTenantId = Expression.Property(
                    Expression.Constant(_tenantContext), nameof(ITenantContext.TenantId));
                var filter = Expression.Lambda(
                    Expression.Equal(tenantIdProperty, currentTenantId), parameter);
                builder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AutoSetTenantId();
        GuardAuditImmutability();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        AutoSetTenantId();
        GuardAuditImmutability();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardAuditImmutability()
    {
        foreach (var entry in ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State == EntityState.Modified)
                throw new AuditImmutabilityException("audit_events_are_append_only");

            if (entry.State == EntityState.Deleted &&
                entry.Entity.OccurredAt > DateTime.UtcNow.AddDays(-AuditSettings.MinimumRetentionDays))
            {
                throw new AuditImmutabilityException(
                    "audit_events_within_the_retention_floor_cannot_be_deleted");
            }
        }
    }

    private void AutoSetTenantId()
    {
        if (!_tenantContext.IsResolved) return;

        foreach (var entry in ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added && e.Entity is ITenantScoped))
        {
            var entity = (ITenantScoped)entry.Entity;
            if (entity.TenantId == Guid.Empty)
            {
                var property = entry.Entity.GetType().GetProperty(nameof(ITenantScoped.TenantId));
                property?.SetValue(entry.Entity, _tenantContext.TenantId);
            }
        }
    }
}
