using System.Globalization;
using System.Reflection;
using Ciclo.Infrastructure.Auditing;
using Ciclo.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 110 — política de retenção (FR-010, NFR-006).
///
/// A configuração é fail-closed: valor fora da faixa ou não numérico impede a inicialização, em vez
/// de produzir um prazo arbitrário que apagaria prova. O piso de imutabilidade precisa ser o mesmo
/// na aplicação e na migration, e é isso que o último teste amarra.
/// </summary>
public sealed class AuditSettingsTests
{
    private static IConfiguration Configuration(string? retentionDays)
    {
        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c[AuditSettings.RetentionDaysKey]).Returns(retentionDays);
        return configuration.Object;
    }

    [Fact]
    public void Read_DefaultsTo365Days()
    {
        var settings = AuditSettings.Read(Configuration(null));

        Assert.Equal(365, settings.RetentionDays);
        Assert.Equal(AuditSettings.DefaultRetentionDays, settings.RetentionDays);
    }

    [Fact]
    public void Read_AcceptsTheFloor()
    {
        var settings = AuditSettings.Read(Configuration("90"));

        Assert.Equal(AuditSettings.MinimumRetentionDays, settings.RetentionDays);
    }

    [Fact]
    public void Read_RejectsRetentionBelowTheFloor()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => AuditSettings.Read(Configuration("30")));

        Assert.Contains(AuditSettings.RetentionDaysKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains("90", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsNonIntegerValue()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => AuditSettings.Read(Configuration("trinta")));

        Assert.Contains(AuditSettings.RetentionDaysKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsRetentionAboveTheMaximum()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => AuditSettings.Read(Configuration("4000")));

        Assert.Contains(
            AuditSettings.MaximumRetentionDays.ToString(CultureInfo.InvariantCulture),
            exception.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Duas camadas, um único piso: o gatilho do PostgreSQL repete o valor de
    /// <see cref="AuditSettings.MinimumRetentionDays"/> e precisa continuar igual. Sem este teste,
    /// mudar a constante deixaria a aplicação autorizando um expurgo que o banco bloqueia (ou o
    /// contrário), e nenhum teste perceberia.
    /// </summary>
    [Fact]
    public void TheDatabaseTriggerUsesTheSameImmutabilityFloor()
    {
        var builder = new MigrationBuilder("Npgsql");
        var migration = new AddAuditEvents();

        typeof(AddAuditEvents)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var sql = string.Join(
            Environment.NewLine,
            builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));

        Assert.Contains("audit_events_append_only_guard", sql, StringComparison.Ordinal);
        Assert.Contains("audit_events_no_update", sql, StringComparison.Ordinal);
        Assert.Contains("audit_events_no_delete", sql, StringComparison.Ordinal);
        Assert.Contains("90 days", sql, StringComparison.Ordinal);
        Assert.Equal(90, AuditSettings.MinimumRetentionDays);
    }
}
