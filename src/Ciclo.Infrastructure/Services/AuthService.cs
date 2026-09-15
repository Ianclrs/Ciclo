using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Auth;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Infrastructure.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RefreshTokenAsync(string refreshToken);
    Task RevokeTokenAsync(string refreshToken);
    Task<string?> ForgotPasswordAsync(string email);
    Task ResetPasswordAsync(ResetPasswordRequest request);
    Task<User?> FindByEmailAsync(string email);
    Task<AuthResponse> GenerateAuthResponseForUser(User user);
}

public class AuthService : IAuthService
{
    private readonly UserManager<User> _userManager;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<User> userManager,
        IJwtTokenGenerator tokenGenerator,
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _tokenGenerator = tokenGenerator;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        // Validate tenant exists
        var tenant = await _dbContext.Tenants.FindAsync(request.TenantId);
        if (tenant == null)
            throw new AuthException("invalid_tenant", 400);

        // Check email uniqueness within tenant
        var existingUser = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == request.TenantId && u.Email == request.Email);
        if (existingUser != null)
            throw new AuthException("email_already_registered", 409);

        var user = new User
        {
            TenantId = request.TenantId,
            Email = request.Email,
            UserName = request.Email,
            Name = request.Name,
            Role = UserRole.Parent,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Resolve o tenant do usuário para que as consultas internas do Identity
        // (ex.: FindByNameAsync) não falhem por causa do global query filter.
        _tenantContext.SetTenant(request.TenantId);

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new AuthException(errors, 400);
        }

        await _userManager.AddToRoleAsync(user, user.Role.ToString());

        return await GenerateAuthResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user == null)
            throw new AuthException("invalid_credentials", 401);

        if (!user.IsActive)
            throw new AuthException("account_inactive", 403);

        // Bloqueio por excesso de tentativas (5 tentativas / 30 minutos)
        if (await _userManager.IsLockedOutAsync(user))
            throw new AuthException("account_locked_30min", 423);

        var passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValid)
        {
            // Identity internamente consulta Users (ex.: FindByNameAsync) durante o
            // UpdateUserAsync; resolve o tenant para o query filter global não falhar.
            _tenantContext.SetTenant(user.TenantId);
            await _userManager.AccessFailedAsync(user);

            // Na 5ª tentativa errada o Identity já aplica o lockout de 30 minutos
            if (await _userManager.IsLockedOutAsync(user))
                throw new AuthException("account_locked_30min", 423);

            throw new AuthException("invalid_credentials", 401);
        }

        _tenantContext.SetTenant(user.TenantId);
        await _userManager.ResetAccessFailedCountAsync(user);

        return await GenerateAuthResponse(user);
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken)
    {
        var storedToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        if (storedToken == null || !storedToken.IsActive)
            throw new AuthException("invalid_token", 401);

        storedToken.RevokedAt = DateTime.UtcNow;

        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == storedToken.UserId);

        if (user == null)
            throw new AuthException("invalid_token", 401);

        return await GenerateAuthResponse(user);
    }

    public async Task RevokeTokenAsync(string refreshToken)
    {
        var storedToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        if (storedToken == null)
            return;

        if (storedToken.IsRevoked)
        {
#pragma warning disable CA1848, CA1873
            _logger.LogWarning("Refresh token reuse detected for user {UserId}", storedToken.UserId);
#pragma warning restore CA1848, CA1873
            var allTokens = await _dbContext.RefreshTokens
                .Where(rt => rt.UserId == storedToken.UserId && rt.RevokedAt == null)
                .ToListAsync();

            foreach (var t in allTokens)
                t.RevokedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            return;
        }

        storedToken.RevokedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    public async Task<string?> ForgotPasswordAsync(string email)
    {
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
            return null;

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
#pragma warning disable CA1848, CA1873
        _logger.LogInformation("Password reset token for {Email}: {Token}", email, token);
#pragma warning restore CA1848, CA1873

        return token;
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user == null)
            throw new AuthException("invalid_token", 400);

        // Check if new password is the same as current
        var isSamePassword = await _userManager.CheckPasswordAsync(user, request.NewPassword);
        if (isSamePassword)
            throw new AuthException("same_password", 400);

        // O fluxo de reset é pré-autenticação, então o tenant ainda não foi resolvido
        // pelo middleware. Resolvemos o tenant do próprio usuário para que as consultas
        // internas do Identity (ex.: FindByNameAsync) funcionem com o query filter global.
        _tenantContext.SetTenant(user.TenantId);

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new AuthException(errors, 400);
        }

#pragma warning disable CA1848, CA1873
        _logger.LogInformation("Password changed for user {UserId} ({Email})", user.Id, user.Email);
#pragma warning restore CA1848, CA1873
    }

    public async Task<User?> FindByEmailAsync(string email)
    {
        return await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);
    }

    public Task<AuthResponse> GenerateAuthResponseForUser(User user)
    {
        return GenerateAuthResponse(user);
    }

    private async Task<AuthResponse> GenerateAuthResponse(User user)
    {
        var accessToken = _tokenGenerator.GenerateAccessToken(user);
        var refreshToken = _tokenGenerator.GenerateRefreshToken(user.Id);

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync();

        return new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken.Token,
            User: new UserDto(user.Id, user.Email ?? string.Empty, user.Name, user.Role.ToString(), user.TenantId));
    }
}

public class AuthException : Exception
{
    public int StatusCode { get; }

    public AuthException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}
