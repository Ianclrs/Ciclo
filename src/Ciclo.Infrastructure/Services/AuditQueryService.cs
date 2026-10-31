using System.Linq.Expressions;
using System.Text;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Auditing;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Infrastructure.Services;

public interface IAuditQueryService
{
    Task<PagedResponse<AuditEventDto>> QueryAsync(
        AuditEventFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<byte[]> ExportCsvAsync(
        AuditEventFilter filter, CancellationToken cancellationToken = default);
}

public sealed class AuditException : Exception
{
    public int StatusCode { get; }

    public AuditException(string message, int statusCode) : base(message) => StatusCode = statusCode;
}

public sealed class AuditQueryService : IAuditQueryService
{
    private const int DefaultRangeDays = 30;
    private const int MaxRangeDays = 400;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    public const int MaxExportRows = 10000;

    private static readonly Expression<Func<AuditEvent, AuditEventDto>> Projection = e =>
        new AuditEventDto(
            e.Id,
            e.OccurredAt,
            e.ActorUserId,
            e.ActorRole,
            e.Action,
            e.TargetType,
            e.TargetId,
            e.BeforeJson,
            e.AfterJson,
            e.IpAddress,
            e.UserAgent,
            e.CorrelationId);

    private readonly AppDbContext _db;

    public AuditQueryService(AppDbContext db) => _db = db;

    public async Task<PagedResponse<AuditEventDto>> QueryAsync(
        AuditEventFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var (from, to) = NormalizeRange(filter);
        var query = BuildQuery(filter, from, to);

        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(Projection)
            .ToListAsync(cancellationToken);

        return new PagedResponse<AuditEventDto>(items, total, normalizedPage, normalizedPageSize);
    }

    public async Task<byte[]> ExportCsvAsync(
        AuditEventFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var (from, to) = NormalizeRange(filter);
        var query = BuildQuery(filter, from, to);

        if (await query.CountAsync(cancellationToken) > MaxExportRows)
            throw new AuditException("export_exceeds_limit", 400);

        var items = await query
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Select(Projection)
            .ToListAsync(cancellationToken);

        var body = Encoding.UTF8.GetBytes(AuditCsvWriter.Write(items));
        var preamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    private static (DateTime From, DateTime To) NormalizeRange(AuditEventFilter filter)
    {
        var to = filter.To.HasValue ? ToUtc(filter.To.Value) : DateTime.UtcNow;
        var from = filter.From.HasValue ? ToUtc(filter.From.Value) : to.AddDays(-DefaultRangeDays);

        if (to < from)
            throw new AuditException("invalid_date_range", 400);

        if ((to - from).TotalDays > MaxRangeDays)
            throw new AuditException("date_range_too_wide", 400);

        return (from, to);
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value
    };

    private IQueryable<AuditEvent> BuildQuery(AuditEventFilter filter, DateTime from, DateTime to)
    {
        var query = _db.AuditEvents
            .AsNoTracking()
            .Where(e => e.OccurredAt >= from && e.OccurredAt <= to);

        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(e => e.Action == filter.Action);

        if (filter.ActorUserId.HasValue)
            query = query.Where(e => e.ActorUserId == filter.ActorUserId.Value);

        if (!string.IsNullOrWhiteSpace(filter.TargetType))
            query = query.Where(e => e.TargetType == filter.TargetType);

        if (filter.TargetId.HasValue)
            query = query.Where(e => e.TargetId == filter.TargetId.Value);

        return query;
    }
}
