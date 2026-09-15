using System.Threading;

namespace Ciclo.Infrastructure.Tenancy;

public interface ITenantContext
{
    Guid TenantId { get; }
    bool IsResolved { get; }

    /// <summary>
    /// Define o tenant do escopo atual. Usado pelo middleware de autenticação
    /// e por fluxos pré-autenticação (register/reset de senha) que precisam
    /// executar consultas do Identity sobre entidades tenant-scoped.
    /// </summary>
    void SetTenant(Guid tenantId);
}

/// <summary>
/// Ambient tenant context. Uses <see cref="AsyncLocal{T}"/> so the tenant flows
/// correctly through async execution contexts per request, and so the EF Core
/// global query filter (which captures the context instance when the model is
/// built) always reads the tenant of the current request — never a stale one.
/// </summary>
public class TenantContext : ITenantContext, IDisposable
{
    private static readonly AsyncLocal<Guid?> _tenantId = new();

    public Guid TenantId => _tenantId.Value
        ?? throw new TenantNotResolvedException();

    public bool IsResolved => _tenantId.Value.HasValue;

#pragma warning disable CA1822 // SetTenant only mutates the ambient (static) AsyncLocal state
    public void SetTenant(Guid tenantId) => _tenantId.Value = tenantId;
#pragma warning restore CA1822

    public void Dispose()
    {
        _tenantId.Value = null;
        GC.SuppressFinalize(this);
    }
}
