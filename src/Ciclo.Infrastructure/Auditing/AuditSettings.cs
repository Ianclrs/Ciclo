using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Ciclo.Infrastructure.Auditing;

public sealed record AuditSettings(int RetentionDays)
{
    public const string RetentionDaysKey = "Audit:RetentionDays";
    public const int DefaultRetentionDays = 365;
    public const int MinimumRetentionDays = 90;
    public const int MaximumRetentionDays = 3650;

    public static AuditSettings Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var raw = configuration[RetentionDaysKey];
        var retentionDays = DefaultRetentionDays;

        if (!string.IsNullOrWhiteSpace(raw) &&
            !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out retentionDays))
        {
            throw new InvalidOperationException(
                $"{RetentionDaysKey} must be an integer; the configured value is '{raw}'.");
        }

        if (retentionDays < MinimumRetentionDays || retentionDays > MaximumRetentionDays)
        {
            throw new InvalidOperationException(
                $"{RetentionDaysKey} must be between {MinimumRetentionDays} and {MaximumRetentionDays} days; " +
                $"the configured value is {retentionDays}.");
        }

        return new AuditSettings(retentionDays);
    }
}
