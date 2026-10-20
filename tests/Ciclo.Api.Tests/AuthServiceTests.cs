using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Auth;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Api.Tests;

public sealed class AuthServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AppDbContext _dbContext;
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly IJwtTokenGenerator _tokenGeneratorMock;
    private readonly IAuthService _authService;
    private readonly CapturingLogger<AuthService> _logger;

    public AuthServiceTests()
    {
        var services = new ServiceCollection();

        // InMemory database with a unique name per test instance
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        // Data Protection for token providers
        services.AddDataProtection();

        // Identity setup
        services.AddIdentityCore<User>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequireUppercase = true;
            options.Password.RequireDigit = true;
            options.Password.RequireNonAlphanumeric = true;
            options.User.RequireUniqueEmail = false;
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        // Tenant context mock (unresolved by default for auth tests)
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(tc => tc.IsResolved).Returns(false);
        services.AddScoped<ITenantContext>(_ => tenantContextMock.Object);

        // JWT token generator mock
        var tokenMock = new Mock<IJwtTokenGenerator>();
        tokenMock.Setup(t => t.GenerateAccessToken(It.IsAny<User>()))
            .Returns("mock-access-token");
        tokenMock.Setup(t => t.GenerateRefreshToken(It.IsAny<Guid>()))
            .Returns((Guid userId) => new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            });
        _tokenGeneratorMock = tokenMock.Object;

        // Logger que captura as mensagens registradas (verificação de vazamento de segredo)
        _logger = new CapturingLogger<AuthService>();

        // IConfiguration mock for JWT
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["Jwt:Secret"]).Returns("test-secret-that-is-at-least-32-characters-long!!");
        configMock.Setup(c => c["Jwt:Issuer"]).Returns("Test");
        configMock.Setup(c => c["Jwt:Audience"]).Returns("Test");

        services.AddScoped<IJwtTokenGenerator>(_ => _tokenGeneratorMock);
        services.AddScoped<IAuthService, AuthService>();
        services.AddSingleton<ILogger<AuthService>>(_logger);
        services.AddSingleton(configMock.Object);

        _serviceProvider = services.BuildServiceProvider();

        _dbContext = _serviceProvider.GetRequiredService<AppDbContext>();
        _userManager = _serviceProvider.GetRequiredService<UserManager<User>>();
        _roleManager = _serviceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        _authService = _serviceProvider.GetRequiredService<IAuthService>();

        // Seed test data
        SeedAsync().GetAwaiter().GetResult();
    }

    private async Task SeedAsync()
    {
        // Create test tenant
        _dbContext.Tenants.Add(new Tenant
        {
            Id = TestConstants.TenantId,
            Name = "Test School",
            Slug = "test-school",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        // Create roles
        await _roleManager.CreateAsync(new IdentityRole<Guid>("Admin"));
        await _roleManager.CreateAsync(new IdentityRole<Guid>("Staff"));
        await _roleManager.CreateAsync(new IdentityRole<Guid>("Parent"));

        await _dbContext.SaveChangesAsync();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    // ===== Register Tests =====

    [Fact]
    public async Task RegisterAsync_ValidRequest_CreatesUserAndReturnsTokens()
    {
        var request = new RegisterRequest("newuser@test.com", "Password1!", "New User", TestConstants.TenantId);

        var result = await _authService.RegisterAsync(request);

        Assert.NotNull(result);
        Assert.Equal("mock-access-token", result.AccessToken);
        Assert.NotNull(result.RefreshToken);
        Assert.Equal("newuser@test.com", result.User.Email);
        Assert.Equal("New User", result.User.Name);

        // Verify user was persisted
        var user = await _dbContext.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == "newuser@test.com");
        Assert.NotNull(user);
        Assert.Equal(TestConstants.TenantId, user!.TenantId);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmailInSameTenant_ThrowsAuthException()
    {
        var request = new RegisterRequest("duplicate@test.com", "Password1!", "First User", TestConstants.TenantId);
        await _authService.RegisterAsync(request);

        var duplicate = new RegisterRequest("duplicate@test.com", "Password1!", "Second User", TestConstants.TenantId);
        var ex = await Assert.ThrowsAsync<AuthException>(() => _authService.RegisterAsync(duplicate));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("email_already_registered", ex.Message);
    }

    /// <summary>
    /// C2 — o e-mail é a chave de login (resolvido sem tenant), então um cadastro com e-mail já
    /// usado em <b>outra</b> escola precisa falhar. Antes desta correção a checagem era por
    /// tenant: o mesmo e-mail entrava em duas escolas e o login — que consulta só pelo e-mail,
    /// sem ORDER BY — escolhia uma linha arbitrária. O sintoma era a pessoa entrar na escola
    /// errada, ou receber o token de reset da conta de outra escola.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_DuplicateEmailInAnotherTenant_ThrowsAuthException()
    {
        var otherTenantId = await SeedSecondTenantAsync();

        await _authService.RegisterAsync(
            new RegisterRequest("cross-tenant@test.com", "Password1!", "First User", TestConstants.TenantId));

        var duplicate = new RegisterRequest("cross-tenant@test.com", "Password1!", "Second User", otherTenantId);
        var ex = await Assert.ThrowsAsync<AuthException>(() => _authService.RegisterAsync(duplicate));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("email_already_registered", ex.Message);
    }

    /// <summary>
    /// Controle positivo do teste acima: um e-mail <b>diferente</b> na outra escola é cadastrado
    /// normalmente. Sem isto, o teste anterior passaria por um motivo errado (por exemplo, se a
    /// segunda escola estivesse inválida e o erro fosse outro).
    /// </summary>
    [Fact]
    public async Task RegisterAsync_DistinctEmailInAnotherTenant_Succeeds()
    {
        var otherTenantId = await SeedSecondTenantAsync();

        await _authService.RegisterAsync(
            new RegisterRequest("aluno-a@test.com", "Password1!", "First User", TestConstants.TenantId));

        var result = await _authService.RegisterAsync(
            new RegisterRequest("aluno-b@test.com", "Password1!", "Second User", otherTenantId));

        Assert.NotNull(result.AccessToken);
        Assert.Equal(otherTenantId, result.User.TenantId);
    }

    private async Task<Guid> SeedSecondTenantAsync()
    {
        var otherTenantId = Guid.NewGuid();
        _dbContext.Tenants.Add(new Tenant
        {
            Id = otherTenantId,
            Name = "Outra Escola",
            Slug = $"outra-escola-{otherTenantId:N}",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        return otherTenantId;
    }

    [Fact]
    public async Task RegisterAsync_InvalidTenant_ThrowsAuthException()
    {
        var request = new RegisterRequest("user@test.com", "Password1!", "User", Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<AuthException>(() => _authService.RegisterAsync(request));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("invalid_tenant", ex.Message);
    }

    // ===== Login Tests =====

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokens()
    {
        // Register first
        await _authService.RegisterAsync(
            new RegisterRequest("login@test.com", "Password1!", "Login User", TestConstants.TenantId));

        var result = await _authService.LoginAsync(new LoginRequest("login@test.com", "Password1!"));

        Assert.NotNull(result);
        Assert.Equal("mock-access-token", result.AccessToken);
        Assert.Equal("login@test.com", result.User.Email);
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ThrowsAuthException()
    {
        await _authService.RegisterAsync(
            new RegisterRequest("login2@test.com", "Password1!", "Login User", TestConstants.TenantId));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            _authService.LoginAsync(new LoginRequest("login2@test.com", "WrongPassword1!")));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("invalid_credentials", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_NonExistentUser_ThrowsAuthException()
    {
        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            _authService.LoginAsync(new LoginRequest("nonexistent@test.com", "Password1!")));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("invalid_credentials", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_InactiveUser_ThrowsAuthException()
    {
        await _authService.RegisterAsync(
            new RegisterRequest("inactive@test.com", "Password1!", "Inactive User", TestConstants.TenantId));

        // Deactivate user
        var user = await _dbContext.Users.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "inactive@test.com");
        user.IsActive = false;
        await _dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            _authService.LoginAsync(new LoginRequest("inactive@test.com", "Password1!")));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("account_inactive", ex.Message);
    }

    // ===== Refresh Token Tests =====

    [Fact]
    public async Task RefreshTokenAsync_ValidToken_ReturnsNewTokens()
    {
        var authResult = await _authService.RegisterAsync(
            new RegisterRequest("refresh@test.com", "Password1!", "Refresh User", TestConstants.TenantId));

        var result = await _authService.RefreshTokenAsync(authResult.RefreshToken);

        Assert.NotNull(result);
        Assert.Equal("mock-access-token", result.AccessToken);
        Assert.NotNull(result.RefreshToken);
        Assert.NotEqual(authResult.RefreshToken, result.RefreshToken); // Rotation
    }

    [Fact]
    public async Task RefreshTokenAsync_InvalidToken_ThrowsAuthException()
    {
        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            _authService.RefreshTokenAsync("invalid-refresh-token"));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("invalid_token", ex.Message);
    }

    [Fact]
    public async Task RefreshTokenAsync_ExpiredToken_ThrowsAuthException()
    {
        var authResult = await _authService.RegisterAsync(
            new RegisterRequest("expired-rf@test.com", "Password1!", "Expired User", TestConstants.TenantId));

        // Manually expire the token
        var storedToken = await _dbContext.RefreshTokens
            .FirstAsync(rt => rt.Token == authResult.RefreshToken);
        storedToken.ExpiresAt = DateTime.UtcNow.AddHours(-1);
        await _dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            _authService.RefreshTokenAsync(authResult.RefreshToken));

        Assert.Equal(401, ex.StatusCode);
    }

    // ===== Revoke Token Tests =====

    [Fact]
    public async Task RevokeTokenAsync_ValidToken_RevokesSuccessfully()
    {
        var authResult = await _authService.RegisterAsync(
            new RegisterRequest("revoke@test.com", "Password1!", "Revoke User", TestConstants.TenantId));

        await _authService.RevokeTokenAsync(authResult.RefreshToken);

        // Token should be revoked
        var storedToken = await _dbContext.RefreshTokens
            .FirstAsync(rt => rt.Token == authResult.RefreshToken);
        Assert.True(storedToken.IsRevoked);
    }

    [Fact]
    public async Task RevokeTokenAsync_AlreadyRevokedToken_RevokesAllUserTokens()
    {
        var authResult = await _authService.RegisterAsync(
            new RegisterRequest("revoke-all@test.com", "Password1!", "Revoke All", TestConstants.TenantId));

        // First revoke
        await _authService.RevokeTokenAsync(authResult.RefreshToken);

        // Second revoke of same token (reuse detection)
        await _authService.RevokeTokenAsync(authResult.RefreshToken);

        // All user tokens should be revoked
        var userTokens = await _dbContext.RefreshTokens
            .Where(rt => rt.UserId == authResult.User.Id)
            .ToListAsync();
        Assert.All(userTokens, t => Assert.True(t.IsRevoked));
    }

    [Fact]
    public async Task RevokeTokenAsync_NonExistentToken_DoesNotThrow()
    {
        await _authService.RevokeTokenAsync("nonexistent-token");
        // Should not throw — idempotent
    }

    // ===== Forgot Password Tests =====

    [Fact]
    public async Task ForgotPasswordAsync_ExistingUser_DoesNotThrow()
    {
        await _authService.RegisterAsync(
            new RegisterRequest("forgot@test.com", "Password1!", "Forgot User", TestConstants.TenantId));

        await _authService.ForgotPasswordAsync("forgot@test.com");
        // Should not throw — always returns 200
    }

    /// <summary>
    /// C7 — o token de reset é uma credencial: quem lê o log assume a conta. Ele era registrado
    /// em texto claro, com o e-mail junto (`LogInformation("... {Email}: {Token}", ...)`).
    /// </summary>
    [Fact]
    public async Task ForgotPasswordAsync_DoesNotWriteTheResetTokenToTheLog()
    {
        await _authService.RegisterAsync(
            new RegisterRequest("leak@test.com", "Password1!", "Leak User", TestConstants.TenantId));
        _logger.Messages.Clear();

        var token = await _authService.ForgotPasswordAsync("leak@test.com");

        Assert.NotNull(token);

        // Controle: sem nenhuma mensagem capturada, a asserção seguinte passaria sem provar nada.
        Assert.True(_logger.Messages.Count > 0,
            "O evento de reset não foi registrado — a captura de log não está funcionando.");

        Assert.DoesNotContain(_logger.Messages,
            message => message.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForgotPasswordAsync_NonExistentUser_DoesNotThrow()
    {
        await _authService.ForgotPasswordAsync("nonexistent@test.com");
        // Should not throw — prevents email enumeration
    }

    // ===== Reset Password Tests =====

    [Fact]
    public async Task ResetPasswordAsync_ValidToken_ResetsPassword()
    {
        await _authService.RegisterAsync(
            new RegisterRequest("reset@test.com", "Password1!", "Reset User", TestConstants.TenantId));

        // Generate reset token (bypass tenant filter since tenant context is unresolved)
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "reset@test.com");
        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);

        await _authService.ResetPasswordAsync(new ResetPasswordRequest("reset@test.com", resetToken, "NewPassword1!"));

        // Verify new password works
        var result = await _authService.LoginAsync(new LoginRequest("reset@test.com", "NewPassword1!"));
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ResetPasswordAsync_InvalidToken_ThrowsAuthException()
    {
        await _authService.RegisterAsync(
            new RegisterRequest("reset2@test.com", "Password1!", "Reset User 2", TestConstants.TenantId));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            _authService.ResetPasswordAsync(new ResetPasswordRequest("reset2@test.com", "invalid-token", "NewPassword1!")));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task ResetPasswordAsync_NonExistentUser_ThrowsAuthException()
    {
        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            _authService.ResetPasswordAsync(new ResetPasswordRequest("nonexistent@test.com", "token", "NewPassword1!")));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("invalid_token", ex.Message);
    }
}

public static class TestConstants
{
    public static readonly Guid TenantId = new("11111111-1111-1111-1111-111111111111");
}
