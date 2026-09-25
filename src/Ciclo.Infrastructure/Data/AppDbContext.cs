using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
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
    public DbSet<Student> Students { get; set; } = null!;
    public DbSet<StudentParent> StudentParents { get; set; } = null!;
    public DbSet<EnrollmentPeriod> EnrollmentPeriods { get; set; } = null!;
    public DbSet<Enrollment> Enrollments { get; set; } = null!;
    public DbSet<DocumentType> DocumentTypes { get; set; } = null!;
    public DbSet<Document> Documents { get; set; } = null!;
    public DbSet<Notification> Notifications { get; set; } = null!;
    public DbSet<UserNotification> UserNotifications { get; set; } = null!;

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

            // Unique composite index (TenantId, Email)
            entity.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();

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
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        AutoSetTenantId();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
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
