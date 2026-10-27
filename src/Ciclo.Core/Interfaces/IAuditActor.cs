namespace Ciclo.Core.Interfaces;

public interface IAuditActor
{
    Guid? UserId { get; }
    string? Role { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
}
