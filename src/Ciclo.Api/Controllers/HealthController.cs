using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ciclo.Infrastructure.Data;

namespace Ciclo.Api.Controllers;

/// <summary>
/// Probe de infraestrutura do host: público por decisão (não faz parte da superfície de
/// aplicação e fica fora de /api — ver management/arch.md). É a única exceção à política de
/// fallback de autenticação.
/// </summary>
[ApiController]
[Route("[controller]")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "Healthy" });

    [HttpGet("db")]
    public async Task<IActionResult> GetDb(AppDbContext db)
    {
        // Provider não relacional (InMemory nos testes) não tem conexão a verificar.
        var canConnect = !db.Database.IsRelational() || await db.Database.CanConnectAsync();
        return canConnect
            ? Ok(new { status = "Healthy", database = "Connected" })
            : StatusCode(503, new { status = "Unhealthy", database = "Disconnected" });
    }
}
