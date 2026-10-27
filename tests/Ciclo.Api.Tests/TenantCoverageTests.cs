using System.Reflection;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// B10 — cobertura de entidades tenant-scoped pelo MODELO, não por quem já tem `TenantId`.
///
/// O teste anterior (<see cref="TenantScopedEntitiesTests"/>) só olha entidades que JÁ possuem
/// a coluna: uma entidade nova, sem `TenantId` e sem a interface, passava despercebida — o
/// teste "passava sem detectar a lacuna".
///
/// Aqui a varredura parte dos `DbSet`: cada entidade mapeada precisa implementar
/// <see cref="ITenantScoped"/> ou estar na lista explícita de exceções registrada no ADR-002.
/// Adicionar entidade nova passa a exigir uma decisão, não um esquecimento.
/// </summary>
public sealed class TenantCoverageTests
{
    /// <summary>
    /// Exceções ao filtro global de tenant, com o motivo. São vínculos cujo tenant é derivado
    /// do agregado pai (Student, Notification) e uma credencial consultada antes de o tenant
    /// existir (RefreshToken) — nenhuma delas guarda dado de negócio próprio.
    /// </summary>
    private static readonly Dictionary<string, string> ExemptFromTenantFilter = new()
    {
        ["Tenant"] = "É a raiz do tenant; não pertence a outro tenant.",
        ["RefreshToken"] = "Credencial pré-autenticação: consultada pelo token, antes de o tenant ser resolvido.",
        ["StudentParent"] = "Vínculo cujo tenant é derivado de Student (escopado); consultado por StudentId/ParentId.",
        ["UserNotification"] = "Vínculo cujo tenant é derivado de Notification (escopada); consultado por UserId."
    };

    [Fact]
    public void EveryMappedEntity_IsEitherTenantScopedOrExplicitlyExempt()
    {
        var mappedEntities = typeof(AppDbContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            // Apenas entidades de DOMÍNIO: o Identity mapeia as próprias tabelas (roles, claims,
            // logins) e elas não seguem o modelo de tenant do produto.
            .Where(t => t.Assembly == typeof(Ciclo.Core.Entities.Tenant).Assembly)
            .ToArray();

        Assert.NotEmpty(mappedEntities);

        // Nome isento que não existe é typo: desativaria a exigência em silêncio.
        foreach (var exempt in ExemptFromTenantFilter.Keys)
        {
            Assert.True(mappedEntities.Any(t => t.Name == exempt),
                $"A entidade isenta '{exempt}' não existe no modelo — revise a lista de exceções.");
        }

        var offenders = mappedEntities
            .Where(t => !typeof(ITenantScoped).IsAssignableFrom(t))
            .Where(t => !ExemptFromTenantFilter.ContainsKey(t.Name))
            .Select(t => t.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Entidade(s) sem isolamento de tenant e sem exceção registrada no ADR-002: " +
            string.Join(", ", offenders));
    }
}
