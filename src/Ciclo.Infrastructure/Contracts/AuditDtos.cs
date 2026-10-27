namespace Ciclo.Infrastructure.Contracts;

public sealed record AuditEventFilter(
    DateTime? From,
    DateTime? To,
    string? Action,
    Guid? ActorUserId,
    string? TargetType,
    Guid? TargetId);

public sealed record AuditEventDto(
    Guid Id,
    DateTime OccurredAt,
    Guid? ActorUserId,
    string? ActorRole,
    string Action,
    string TargetType,
    Guid TargetId,
    string? BeforeJson,
    string? AfterJson,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId);
