using System.Text.Json;
using System.Text.Json.Serialization;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Infrastructure.Auditing;

public sealed class AuditTrail : IAuditTrail
{
    internal const int MaxPayloadCharacters = 8192;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditActor _actor;

    public AuditTrail(AppDbContext db, ITenantContext tenantContext, IAuditActor actor)
    {
        _db = db;
        _tenantContext = tenantContext;
        _actor = actor;
    }

    public void Record(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.Action))
            throw new ArgumentException("audit action is required", nameof(entry));

        if (string.IsNullOrWhiteSpace(entry.TargetType))
            throw new ArgumentException("audit target type is required", nameof(entry));

        if (!_tenantContext.IsResolved)
            throw new TenantNotResolvedException("audit_event_requires_resolved_tenant");

        _db.AuditEvents.Add(new AuditEvent
        {
            TenantId = _tenantContext.TenantId,
            OccurredAt = DateTime.UtcNow,
            ActorUserId = _actor.UserId,
            ActorRole = _actor.Role,
            Action = entry.Action,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            BeforeJson = Serialize(entry.Before, nameof(entry.Before)),
            AfterJson = Serialize(entry.After, nameof(entry.After)),
            IpAddress = _actor.IpAddress,
            UserAgent = _actor.UserAgent,
            CorrelationId = _actor.CorrelationId
        });
    }

    private static string? Serialize(object? value, string parameterName)
    {
        if (value is null) return null;

        var json = JsonSerializer.Serialize(value, JsonOptions);
        if (json.Length > MaxPayloadCharacters)
        {
            throw new InvalidOperationException(
                $"audit payload exceeds {MaxPayloadCharacters} characters (observed {json.Length}) for {parameterName}.");
        }

        return json;
    }
}
