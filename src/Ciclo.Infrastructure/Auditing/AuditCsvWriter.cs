using System.Globalization;
using System.Text;
using Ciclo.Infrastructure.Contracts;

namespace Ciclo.Infrastructure.Auditing;

public static class AuditCsvWriter
{
    private const string Header =
        "OccurredAt,ActorUserId,ActorRole,Action,TargetType,TargetId,CorrelationId,IpAddress,UserAgent,BeforeJson,AfterJson";

    public static string Write(IReadOnlyList<AuditEventDto> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var builder = new StringBuilder();
        builder.Append(Header);
        builder.Append("\r\n");

        foreach (var item in events)
        {
            builder.Append(item.OccurredAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(Escape(item.ActorUserId?.ToString()));
            builder.Append(',');
            builder.Append(Escape(item.ActorRole));
            builder.Append(',');
            builder.Append(Escape(item.Action));
            builder.Append(',');
            builder.Append(Escape(item.TargetType));
            builder.Append(',');
            builder.Append(Escape(item.TargetId.ToString()));
            builder.Append(',');
            builder.Append(Escape(item.CorrelationId));
            builder.Append(',');
            builder.Append(Escape(item.IpAddress));
            builder.Append(',');
            builder.Append(Escape(item.UserAgent));
            builder.Append(',');
            builder.Append(Escape(item.BeforeJson));
            builder.Append(',');
            builder.Append(Escape(item.AfterJson));
            builder.Append("\r\n");
        }

        return builder.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        if (value.Contains(',', StringComparison.Ordinal) ||
            value.Contains('"', StringComparison.Ordinal) ||
            value.Contains('\n', StringComparison.Ordinal) ||
            value.Contains('\r', StringComparison.Ordinal))
        {
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return value;
    }
}
