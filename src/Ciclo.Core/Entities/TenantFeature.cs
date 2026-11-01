namespace Ciclo.Core.Entities;

/// <summary>
/// Fonte única das flags de funcionalidade suportadas e do conjunto padrão por plano (ADR-200-2).
/// </summary>
public static class TenantFeature
{
    public const string Documents = "Documents";
    public const string Notifications = "Notifications";
    public const string ParentPortal = "ParentPortal";
    public const string AcademicPerformance = "AcademicPerformance";
    public const string BatchReenrollment = "BatchReenrollment";
    public const string AuditExport = "AuditExport";

    private static readonly Dictionary<TenantPlan, string[]> Defaults = new()
    {
        [TenantPlan.Basic] = new[] { Documents, ParentPortal },
        [TenantPlan.Standard] = new[] { Documents, ParentPortal, Notifications, AcademicPerformance },
        [TenantPlan.Premium] = new[]
        {
            AcademicPerformance, AuditExport, BatchReenrollment, Documents, Notifications, ParentPortal
        }
    };

    /// <summary>Todas as flags conhecidas.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        new[] { Documents, Notifications, ParentPortal, AcademicPerformance, BatchReenrollment, AuditExport },
        StringComparer.Ordinal);

    /// <summary>Conjunto padrão de flags do plano, ordenado.</summary>
    public static IReadOnlyList<string> DefaultFor(TenantPlan plan)
    {
        var flags = Defaults.TryGetValue(plan, out var known) ? known : Defaults[TenantPlan.Basic];
        return Order(flags);
    }

    /// <summary>Lista ordenada, sem duplicatas e validada contra as flags conhecidas.</summary>
    public static IReadOnlyList<string> Normalize(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Array.Empty<string>();
        }

        var names = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Validate(names);
    }

    /// <summary>Grava a lista normalizada como texto separado por vírgula.</summary>
    public static string ToCsv(IEnumerable<string> flags) => string.Join(',', Validate(flags));

    private static string[] Validate(IEnumerable<string> flags)
    {
        var ordered = Order(flags);
        var invalid = ordered.Where(flag => !All.Contains(flag)).ToArray();

        if (invalid.Length > 0)
        {
            throw new ArgumentException(
                $"Feature flags desconhecidas: {string.Join(", ", invalid)}.", nameof(flags));
        }

        return ordered;
    }

    private static string[] Order(IEnumerable<string> flags) =>
        flags.Distinct(StringComparer.Ordinal).OrderBy(flag => flag, StringComparer.Ordinal).ToArray();
}
