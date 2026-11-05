using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ciclo.Core.Entities;

namespace Ciclo.Infrastructure.Data.Seeders;

public static class TenantSeeder
{
#pragma warning disable CA1848 // LoggerMessage delegates — acceptable for seeders
#pragma warning disable CA1873 // Potential expensive argument evaluation — acceptable for seeders
    public static async Task SeedAsync(AppDbContext dbContext,
        IHostEnvironment env, ILogger logger)
    {
        if (!env.IsDevelopment())
        {
            logger.LogInformation("TenantSeeder: Skipping — not Development environment");
            return;
        }

        if (await dbContext.Tenants.AnyAsync())
        {
            logger.LogInformation("TenantSeeder: Tenants already exist, skipping");
            return;
        }

        var defaultTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Default School",
            Slug = "default",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Plan = TenantPlan.Basic,
            FeatureFlags = TenantFeature.DefaultCsv(TenantPlan.Basic)
        };

        dbContext.Tenants.Add(defaultTenant);
        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "TenantSeeder: Created default tenant '{Name}' (Id: {Id})",
            defaultTenant.Name, defaultTenant.Id);
    }
#pragma warning restore CA1848
#pragma warning restore CA1873
}
