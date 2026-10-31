using Ciclo.Core.Auditing;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Api.Tests;

/// <summary>
/// Trilha de auditoria que registra as entradas em memória, para os testes afirmarem exatamente
/// qual evento cada operação sensível produziu.
/// </summary>
internal sealed class RecordingAuditTrail : IAuditTrail
{
    public List<AuditEntry> Entries { get; } = [];

    public void Record(AuditEntry entry) => Entries.Add(entry);
}

/// <summary>
/// Trilha que falha na escrita, para provar o comportamento fail-closed: a operação de negócio não
/// é confirmada quando a auditoria não pode ser gravada.
/// </summary>
internal sealed class ThrowingAuditTrail : IAuditTrail
{
    public void Record(AuditEntry entry) =>
        throw new InvalidOperationException("audit_write_failed");
}

/// <summary>
/// Dados de teste compartilhados pelos testes de auditoria (Spec 110).
///
/// Repete o cuidado já documentado em <c>DocumentComplianceTests</c>: o filtro global de tenant é
/// compilado no modelo do EF, e o modelo é cacheado por TIPO de contexto, preso à instância que o
/// criou. Por isso o contexto é um tipo derivado e o isolamento entre testes vem do nome único do
/// banco InMemory, não do tenant.
/// </summary>
internal static class AuditTestData
{
    private sealed class AuditTestDbContext : AppDbContext
    {
        internal AuditTestDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        private readonly Guid _tenantId;

        internal FixedTenantContext(Guid tenantId) => _tenantId = tenantId;

        public Guid TenantId => _tenantId;

        public bool IsResolved => true;

        public void SetTenant(Guid tenantId) =>
            throw new NotSupportedException("O tenant é fixo no contexto de teste.");
    }

    /// <summary>
    /// Tenant único deste arquivo de teste. O isolamento vem do banco InMemory de nome único; um
    /// valor fixo evita depender de estado ambiente (AsyncLocal), que se mostrou frágil aqui.
    /// </summary>
    internal static readonly Guid TestTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    internal static (AppDbContext Db, Guid TenantId) CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return (new AuditTestDbContext(options, new FixedTenantContext(TestTenantId)), TestTenantId);
    }
}
