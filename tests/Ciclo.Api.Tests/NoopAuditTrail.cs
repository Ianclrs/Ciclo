using Ciclo.Core.Auditing;
using Ciclo.Core.Interfaces;

namespace Ciclo.Api.Tests;

internal sealed class NoopAuditTrail : IAuditTrail
{
    public static readonly NoopAuditTrail Instance = new();

    public void Record(AuditEntry entry)
    {
    }
}
