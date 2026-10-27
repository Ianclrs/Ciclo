using Ciclo.Core.Auditing;

namespace Ciclo.Core.Interfaces;

public interface IAuditTrail
{
    void Record(AuditEntry entry);
}
