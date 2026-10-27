namespace Ciclo.Core.Exceptions;

public class AuditImmutabilityException : InvalidOperationException
{
    public AuditImmutabilityException(string message) : base(message) { }
}
