using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ciclo.Api.RateLimiting;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;

namespace Ciclo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly AppDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public AuthController(IAuthService authService, AppDbContext dbContext, IWebHostEnvironment environment)
    {
        _authService = authService;
        _dbContext = dbContext;
        _environment = environment;
    }

    /// <summary>FR-001: Register a new user with email/password.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var result = await _authService.RegisterAsync(request);
            SetRefreshTokenCookie(result.RefreshToken);
            return Created(string.Empty, new { result.AccessToken, result.User });
        }
        catch (AuthException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>FR-002: Login with email/password.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var result = await _authService.LoginAsync(request);
            SetRefreshTokenCookie(result.RefreshToken);
            return Ok(new { result.AccessToken, result.User });
        }
        catch (AuthException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>FR-003: Refresh access token using refresh token cookie.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Refresh()
    {
        var refreshToken = Request.Cookies["refresh_token"];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(new { error = "invalid_token" });

        try
        {
            var result = await _authService.RefreshTokenAsync(refreshToken);
            SetRefreshTokenCookie(result.RefreshToken);
            return Ok(new { result.AccessToken, result.User });
        }
        catch (AuthException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>FR-004: Revoke refresh token (requires auth).</summary>
    [HttpPost("revoke")]
    [Authorize]
    public async Task<IActionResult> Revoke()
    {
        var refreshToken = Request.Cookies["refresh_token"];
        if (string.IsNullOrEmpty(refreshToken))
            return Ok();

        await _authService.RevokeTokenAsync(refreshToken);
        Response.Cookies.Delete("refresh_token");
        return Ok();
    }

    /// <summary>Get current authenticated user info.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
            return Unauthorized();

        return Ok(new
        {
            user.Id,
            user.Name,
            user.Email,
            role = user.Role.ToString(),
            user.TenantId,
        });
    }

    /// <summary>FR-007: Forgot password — sends reset token.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var resetToken = await _authService.ForgotPasswordAsync(request.Email);

        // Em Development o token é retornado na resposta para viabilizar o fluxo
        // sem um servidor de email real. Em produção, retorne apenas Ok() e
        // entregue o token por email.
        if (_environment.IsDevelopment() && resetToken is not null)
            return Ok(new { resetToken });

        return Ok();
    }

    /// <summary>FR-008: Reset password using token.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        try
        {
            await _authService.ResetPasswordAsync(request);
            return Ok();
        }
        catch (AuthException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    private void SetRefreshTokenCookie(string token)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddDays(7)
        };
        Response.Cookies.Append("refresh_token", token, cookieOptions);
    }
}
