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

using Ciclo.Api.Middleware;

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
            // Cadastro não tem opção de "lembrar": a sessão nasce de sessão de navegador.
            SetRefreshTokenCookie(result.RefreshToken, rememberMe: false);
            return Created(string.Empty, new { result.AccessToken, result.User });
        }
        catch (AuthException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>
    /// A5: cadastro de responsavel pelo slug publico do colegio. Endpoint anonimo por decisao
    /// (como o cadastro de escola); criar a conta nao vincula aluno — o vinculo e ato da secretaria.
    /// </summary>
    [HttpPost("register/parent")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> RegisterParent([FromBody] RegisterParentRequest request)
    {
        try
        {
            var result = await _authService.RegisterParentAsync(request);
            // Cadastro não tem opção de "lembrar": a sessão nasce de sessão de navegador.
            SetRefreshTokenCookie(result.RefreshToken, rememberMe: false);
            return Created(string.Empty, new { result.AccessToken, result.User });
        }
        catch (AuthException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
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
            SetRefreshTokenCookie(result.RefreshToken, request.RememberMe);
            return Ok(new { result.AccessToken, result.User });
        }
        catch (AuthException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
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
            return ProblemResponse.FromCode("invalid_token", 401);

        try
        {
            var result = await _authService.RefreshTokenAsync(refreshToken);
            // A política de "lembrar" vem do token rotacionado: sem isso o primeiro refresh
            // transformaria uma sessão de 30 dias em cookie de sessão.
            SetRefreshTokenCookie(result.RefreshToken, result.RememberMe);
            return Ok(new { result.AccessToken, result.User });
        }
        catch (AuthException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
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
            user.EmailConfirmed,
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
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>
    /// Spec 120: aceite do convite. Endpoint anônimo porque o convidado ainda não tem senha; o
    /// token de uso único é a credencial que prova a posse da caixa de e-mail.
    /// </summary>
    [HttpPost("accept-invite")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request)
    {
        try
        {
            await _authService.AcceptInviteAsync(request);
            return NoContent();
        }
        catch (AuthException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>M11: confirma o e-mail com o token enviado ao usuário.</summary>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest request)
    {
        try
        {
            await _authService.ConfirmEmailAsync(request);
            return Ok();
        }
        catch (AuthException ex)
        {
            return ProblemResponse.FromCode(ex.Message, ex.StatusCode);
        }
    }

    /// <summary>
    /// M11: reenvia o token de confirmação. Em Development o token volta na resposta (mesmo
    /// padrão do forgot-password, que viabiliza o fluxo sem servidor de e-mail).
    /// </summary>
    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ResendConfirmation([FromBody] ForgotPasswordRequest request)
    {
        var token = await _authService.ResendConfirmationAsync(request.Email);

        if (_environment.IsDevelopment() && token is not null)
            return Ok(new { confirmationToken = token });

        return Ok();
    }

    /// <summary>
    /// Emite o cookie do refresh token.
    ///
    /// <paramref name="rememberMe"/> decide a NATUREZA do cookie, não só a duração: com
    /// "lembrar" ele é persistente (30 dias, sobrevive a fechar o navegador); sem, é cookie de
    /// sessão — o navegador o descarta ao fechar. O prazo do lado do servidor acompanha a
    /// mesma escolha em <c>AuthService.GenerateAuthResponse</c>.
    /// </summary>
    private void SetRefreshTokenCookie(string token, bool rememberMe)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
        };

        if (rememberMe)
            cookieOptions.Expires = DateTime.UtcNow.AddDays(30);
        Response.Cookies.Append("refresh_token", token, cookieOptions);
    }
}
