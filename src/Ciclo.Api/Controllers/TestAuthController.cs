using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ciclo.Api.Controllers;

[ApiController]
[Route("api/test-auth")]
public class TestAuthController : ControllerBase
{
    /// <summary>Only Admin role can access.</summary>
    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public IActionResult AdminOnly()
    {
        return Ok(new { message = "You are an Admin", user = User.Identity?.Name });
    }

    /// <summary>Admin and Staff roles can access.</summary>
    [HttpGet("staff")]
    [Authorize(Roles = "Admin,Staff")]
    public IActionResult StaffOrAbove()
    {
        return Ok(new { message = "You are Admin or Staff", user = User.Identity?.Name });
    }

    /// <summary>Any authenticated user can access.</summary>
    [HttpGet("any")]
    [Authorize]
    public IActionResult AnyAuthenticated()
    {
        return Ok(new { message = "You are authenticated", user = User.Identity?.Name });
    }
}
