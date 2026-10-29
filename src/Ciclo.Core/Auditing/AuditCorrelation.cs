using System.Text.RegularExpressions;

namespace Ciclo.Core.Auditing;

public static partial class AuditCorrelation
{
    public const string HeaderName = "X-Correlation-Id";
    public const int MaxLength = 64;

    /// <summary>Chave de <see cref="HttpContext.Items"/> onde o valor resolvido na borda é gravado.</summary>
    public const string ItemsKey = "CorrelationId";

    [GeneratedRegex("^[A-Za-z0-9._:-]{8,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedPattern();

    public static string Resolve(string? candidate, string traceIdentifier)
    {
        if (!string.IsNullOrWhiteSpace(candidate) && AllowedPattern().IsMatch(candidate))
            return candidate;

        var fallback = traceIdentifier ?? string.Empty;
        return fallback.Length <= MaxLength ? fallback : fallback[..MaxLength];
    }
}
