using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ciclo.Api.Health;

/// <summary>
/// Corpo comum dos endpoints de saúde: status agregado mais o status de cada check.
///
/// O JSON é explícito (em vez do writer padrão, que devolve texto) porque o operador precisa
/// saber QUAL dependência falhou, não só que o serviço está doente.
/// </summary>
public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Status.ToString())
        };

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, Options));
    }
}
