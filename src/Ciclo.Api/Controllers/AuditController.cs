using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ciclo.Api.Controllers;

[ApiController]
[Route("api/audit-events")]
[Authorize(Roles = "Admin")]
public class AuditController : ControllerBase
{
    private readonly IAuditQueryService _service;

    public AuditController(IAuditQueryService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? action,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? targetType,
        [FromQuery] Guid? targetId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var filter = new AuditEventFilter(from, to, action, actorUserId, targetType, targetId);
            return Ok(await _service.QueryAsync(filter, page, pageSize));
        }
        catch (AuditException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? action,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? targetType,
        [FromQuery] Guid? targetId)
    {
        try
        {
            var filter = new AuditEventFilter(from, to, action, actorUserId, targetType, targetId);
            var bytes = await _service.ExportCsvAsync(filter);
            return File(bytes, "text/csv; charset=utf-8", "audit-events.csv");
        }
        catch (AuditException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }
}
