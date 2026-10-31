using System.Reflection;
using Ciclo.Api.Controllers;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 110 — imutabilidade append-only na camada de aplicação (FR-002).
///
/// A guarda vive no <c>AppDbContext</c>, não em um interceptor: a alteração de qualquer evento é
/// sempre bloqueada; a remoção é bloqueada apenas dentro do piso de retenção, porque o expurgo
/// precisa poder remover o que está fora dele.
/// </summary>
public sealed class AuditImmutabilityTests
{
    private static AuditEvent NewEvent(Guid tenantId, int daysAgo) => new()
    {
        TenantId = tenantId,
        OccurredAt = DateTime.UtcNow.AddDays(-daysAgo),
        Action = AuditActions.EnrollmentCancelled,
        TargetType = AuditTargetTypes.Enrollment,
        TargetId = Guid.NewGuid()
    };

    [Fact]
    public async Task ModifyingAPersistedEvent_Throws()
    {
        var (context, tenantId) = AuditTestData.CreateContext();
        using var db = context;

        db.AuditEvents.Add(NewEvent(tenantId, daysAgo: 1));
        await db.SaveChangesAsync();

        var persisted = await db.AuditEvents.SingleAsync();
        persisted.Action = AuditActions.SessionRevoked;

        var exception = await Assert.ThrowsAsync<AuditImmutabilityException>(
            () => db.SaveChangesAsync());

        Assert.Equal("audit_events_are_append_only", exception.Message);
    }

    [Fact]
    public async Task DeletingAnEventInsideTheFloor_Throws()
    {
        var (context, tenantId) = AuditTestData.CreateContext();
        using var db = context;

        db.AuditEvents.Add(NewEvent(tenantId, daysAgo: 10));
        await db.SaveChangesAsync();

        var persisted = await db.AuditEvents.SingleAsync();
        db.AuditEvents.Remove(persisted);

        var exception = await Assert.ThrowsAsync<AuditImmutabilityException>(
            () => db.SaveChangesAsync());

        Assert.Equal("audit_events_within_the_retention_floor_cannot_be_deleted", exception.Message);
    }

    [Fact]
    public async Task DeletingAnEventOutsideTheFloor_Succeeds()
    {
        var (context, tenantId) = AuditTestData.CreateContext();
        using var db = context;

        db.AuditEvents.Add(NewEvent(tenantId, daysAgo: 100));
        await db.SaveChangesAsync();

        var persisted = await db.AuditEvents.SingleAsync();
        db.AuditEvents.Remove(persisted);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.AuditEvents.CountAsync());
    }

    /// <summary>
    /// A primeira camada da imutabilidade é a ausência de operação de escrita: nenhuma ação do
    /// controlador pode declarar PUT, PATCH ou DELETE. Reflexão em vez de leitura visual, porque a
    /// regressão aqui é justamente alguém "resolver" um problema adicionando o endpoint.
    /// </summary>
    [Fact]
    public void AuditControllerExposesNoMutationEndpoint()
    {
        var mutatingVerbs = new[]
        {
            typeof(HttpPutAttribute),
            typeof(HttpPatchAttribute),
            typeof(HttpDeleteAttribute)
        };

        var mutatingActions = typeof(AuditController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes(inherit: true)
                .Any(attribute => mutatingVerbs.Contains(attribute.GetType())))
            .Select(method => method.Name)
            .ToArray();

        Assert.Empty(mutatingActions);
    }
}
