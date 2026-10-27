using Ciclo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Infrastructure.Auditing;

public sealed class AuditRetentionPurger
{
    internal const int PurgeBatchSize = 5000;

    private readonly AppDbContext _db;
    private readonly AuditSettings _settings;

    public AuditRetentionPurger(AppDbContext db, AuditSettings settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<int> PurgeAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var cutoff = utcNow.AddDays(-_settings.RetentionDays);
        var removed = 0;

        while (true)
        {
            var batch = await _db.AuditEvents
                .IgnoreQueryFilters()
                .Where(e => e.OccurredAt < cutoff)
                .OrderBy(e => e.OccurredAt)
                .Take(PurgeBatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0) break;

            _db.AuditEvents.RemoveRange(batch);
            await _db.SaveChangesAsync(cancellationToken);
            removed += batch.Count;

            if (batch.Count < PurgeBatchSize) break;
        }

        return removed;
    }
}
