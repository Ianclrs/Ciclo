namespace Ciclo.Core.Auditing;

public sealed record AuditEntry(
    string Action,
    string TargetType,
    Guid TargetId,
    object? Before = null,
    object? After = null);
