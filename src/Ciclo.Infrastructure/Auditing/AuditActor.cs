using Ciclo.Core.Interfaces;

namespace Ciclo.Infrastructure.Auditing;

public sealed class AuditActor : IAuditActor
{
    public const int MaxRoleLength = 32;
    public const int MaxIpAddressLength = 64;
    public const int MaxUserAgentLength = 512;

    public Guid? UserId { get; private set; }
    public string? Role { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? CorrelationId { get; private set; }

    public void Capture(Guid? userId, string? role, string? ipAddress, string? userAgent)
    {
        UserId = userId;
        Role = Truncate(role, MaxRoleLength);
        IpAddress = Truncate(ipAddress, MaxIpAddressLength);
        UserAgent = Truncate(userAgent, MaxUserAgentLength);
    }

    public void SetCorrelationId(string correlationId) => CorrelationId = correlationId;

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
