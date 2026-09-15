using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;

namespace Ciclo.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public AuthController(IAuthService authService, AppDbContext dbContext, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _authService = authService;
        _dbContext = dbContext;
        _configuration = configuration;
        _environment = environment;
    }

    /// <summary>FR-001: Register a new user with email/password.</summary>
    [HttpPost("register")]
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

    /// <summary>FR-005: Redirect to Google OAuth consent screen.</summary>
    [HttpGet("google")]
    public IActionResult GoogleLogin()
    {
        var redirectUrl = Url.Action(nameof(GoogleCallback), "Auth")!;
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, "Google");
    }

    /// <summary>FR-006: Handle Google OAuth callback.</summary>
    [HttpGet("google/callback")]
    public async Task<IActionResult> GoogleCallback()
    {
        // The OAuth middleware has already authenticated the user with Google.
        // We now process the Google claims and create/find our internal user.
        var authResult = await HttpContext.AuthenticateAsync("Google");
        if (!authResult.Succeeded)
            return Redirect($"{GetFrontendUrl()}/login?error=cancelled");

        var googleId = authResult.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var email = authResult.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        var name = authResult.Principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(googleId))
            return Redirect($"{GetFrontendUrl()}/login?error=invalid_google_response");

        try
        {
            // Find user by GoogleId
            var user = await _dbContext.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.GoogleId == googleId);

            if (user == null)
            {
                // Try to find by email for account linking
                user = await _authService.FindByEmailAsync(email);
                if (user != null)
                {
                    // Link Google account to existing user
                    user.GoogleId = googleId;
                    await _dbContext.SaveChangesAsync();
                }
                else
                {
                    // New user — need tenant. Redirect to frontend with Google data.
                    var tempToken = Guid.NewGuid().ToString("N");
                    var state = new { googleId, email, name, tempToken };
                    return Redirect($"{GetFrontendUrl()}/register/google?data={Uri.EscapeDataString(
                        JsonSerializer.Serialize(state))}");
                }
            }

            if (user != null)
            {
                var result = await _authService.GenerateAuthResponseForUser(user);
                SetRefreshTokenCookie(result.RefreshToken);
                return Redirect($"{GetFrontendUrl()}/auth/callback#" +
                    $"access_token={Uri.EscapeDataString(result.AccessToken)}&" +
                    $"user={Uri.EscapeDataString(JsonSerializer.Serialize(result.User))}");
            }
        }
        catch
        {
            return Redirect($"{GetFrontendUrl()}/login?error=auth_failed");
        }

        return Redirect($"{GetFrontendUrl()}/login?error=unknown");
    }

    /// <summary>FR-007: Forgot password — sends reset token.</summary>
    [HttpPost("forgot-password")]
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

    private string GetFrontendUrl()
    {
        return _configuration["Frontend:Url"] ?? "http://localhost:5173";
    }
}
