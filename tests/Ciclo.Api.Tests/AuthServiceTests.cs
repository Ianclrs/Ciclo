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
    public async Task RegisterParentAsync_WithPublicSchoolSlug_CreatesAParentAccount()
    {
        var result = await _authService.RegisterParentAsync(
            new RegisterParentRequest("test-school", "responsavel@test.com", "Password1!", "Responsável"));

        Assert.Equal("responsavel@test.com", result.User.Email);
        Assert.Equal("Parent", result.User.Role);
        Assert.Equal(TestConstants.TenantId, result.User.TenantId);
    }

    [Fact]
    public async Task RegisterParentAsync_WithUnknownSlug_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<AuthException>(() => _authService.RegisterParentAsync(
            new RegisterParentRequest("colegio-inexistente", "x@test.com", "Password1!", "X")));

        Assert.Equal("invalid_tenant", ex.Message);
    }

    /// <summary>
    /// O limite do cadastro por slug: criar a conta não concede acesso a aluno nenhum. Se este
    /// teste falhar, o cadastro passou a conceder algo além de uma conta.
    /// </summary>
    [Fact]
    public async Task RegisterParentAsync_DoesNotLinkAnyStudent()
    {
        await _authService.RegisterParentAsync(
            new RegisterParentRequest("test-school", "sem-vinculo@test.com", "Password1!", "Sem Vínculo"));

        Assert.Empty(_dbContext.StudentParents);
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
    public async Task RefreshTokenAsync_ReuseOfRotatedToken_RevokesAllActiveSessions()
    {
        var first = await _authService.RegisterAsync(
            new RegisterRequest("reuse@test.com", "Password1!", "Reuse", TestConstants.TenantId));

        // Rotação normal: o primeiro token morre e nasce o segundo.
        var second = await _authService.RefreshTokenAsync(first.RefreshToken);

        // Reapresentar o token já rotacionado é o sinal de roubo (B3).
        var ex = await Assert.ThrowsAsync<AuthException>(
            () => _authService.RefreshTokenAsync(first.RefreshToken));
        Assert.Equal("invalid_token", ex.Message);

        // A família inteira é encerrada — inclusive o token que estava válido.
        var secondStored = await _dbContext.RefreshTokens.FirstAsync(rt => rt.Token == second.RefreshToken);
        Assert.True(secondStored.IsRevoked);
    }

    [Fact]
    public async Task RevokeTokenAsync_Repeated_DoesNotEndOtherSessions()
    {
        var first = await _authService.RegisterAsync(
            new RegisterRequest("logout@test.com", "Password1!", "Logout", TestConstants.TenantId));

        // Segunda sessão (outro dispositivo), ativa.
        var second = await _authService.LoginAsync(
            new LoginRequest("logout@test.com", "Password1!"));

        // Logout repetido do primeiro token (B3): o antigo revogava TODAS as sessões.
        await _authService.RevokeTokenAsync(first.RefreshToken);
        await _authService.RevokeTokenAsync(first.RefreshToken);

        var secondStored = await _dbContext.RefreshTokens.FirstAsync(rt => rt.Token == second.RefreshToken);
        Assert.False(secondStored.IsRevoked);
    }

    [Fact]
    public async Task RefreshTokenAsync_PrunesExpiredAndOldRevokedTokens()
    {
        var auth = await _authService.RegisterAsync(
            new RegisterRequest("prune@test.com", "Password1!", "Prune", TestConstants.TenantId));
        var userId = auth.User.Id;

        _dbContext.RefreshTokens.AddRange(
            new RefreshToken { Id = Guid.NewGuid(), UserId = userId, Token = "expirado", ExpiresAt = DateTime.UtcNow.AddDays(-1) },
            new RefreshToken { Id = Guid.NewGuid(), UserId = userId, Token = "revogado-antigo", ExpiresAt = DateTime.UtcNow.AddDays(10), RevokedAt = DateTime.UtcNow.AddDays(-31) },
            new RefreshToken { Id = Guid.NewGuid(), UserId = userId, Token = "revogado-recente", ExpiresAt = DateTime.UtcNow.AddDays(10), RevokedAt = DateTime.UtcNow.AddDays(-1) });
        await _dbContext.SaveChangesAsync();

        // Emitir um novo par dispara a poda.
        await _authService.RefreshTokenAsync(auth.RefreshToken);

        var remaining = await _dbContext.RefreshTokens
            .Where(rt => rt.UserId == userId)
            .Select(rt => rt.Token)
            .ToListAsync();

        Assert.DoesNotContain("expirado", remaining);
        Assert.DoesNotContain("revogado-antigo", remaining);
        // O revogado recente fica: é ele que permite detectar reuso dentro da janela.
        Assert.Contains("revogado-recente", remaining);
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

    // ===== M11: gestão de usuários e ciclo de convite =====

    private async Task<User> CreateAdminAsync(string email)
    {
        var admin = new User
        {
            Id = Guid.NewGuid(),
            TenantId = TestConstants.TenantId,
            Email = email,
            UserName = email,
            Name = "Admin",
            Role = UserRole.Admin,
            IsActive = true,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        var created = await _userManager.CreateAsync(admin, "Password1!");
        Assert.True(created.Succeeded);
        await _userManager.AddToRoleAsync(admin, "Admin");
        return admin;
    }

    [Fact]
    public async Task InviteUserAsync_CreatesAccountWithoutPasswordAndUnconfirmed()
    {
        await CreateAdminAsync("inviter@test.com");

        var (user, inviteToken) = await _authService.InviteUserAsync(
            new InviteUserRequest("invited@test.com", "Convidado", UserRole.Staff),
            TestConstants.TenantId,
            UserRole.Admin);

        Assert.False(user.EmailConfirmed);
        Assert.False(string.IsNullOrWhiteSpace(inviteToken));

        // Consulta direta: o store do Identity aplica o filtro global, e neste harness o
        // tenant do contexto é vazio (mock sem estado).
        var stored = await _dbContext.Users.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "invited@test.com");
        // Sem senha: não há como autenticar antes de aceitar o convite.
        Assert.False(await _userManager.HasPasswordAsync(stored));
    }

    [Fact]
    public async Task InviteUserAsync_ByStaff_IsForbidden()
    {
        await Assert.ThrowsAsync<Ciclo.Core.Exceptions.ForbiddenException>(() =>
            _authService.InviteUserAsync(
                new InviteUserRequest("x@test.com", "X", UserRole.Staff),
                TestConstants.TenantId,
                UserRole.Staff));
    }

    [Fact]
    public async Task InvitedUser_AcceptingInvite_ConfirmsEmailAndAllowsLogin()
    {
        await CreateAdminAsync("inviter2@test.com");

        var (_, inviteToken) = await _authService.InviteUserAsync(
            new InviteUserRequest("accept@test.com", "Aceite", UserRole.Staff),
            TestConstants.TenantId,
            UserRole.Admin);

        // Antes de aceitar, o login não é possível (não há senha).
        await Assert.ThrowsAsync<AuthException>(() =>
            _authService.LoginAsync(new LoginRequest("accept@test.com", "NewPassword1!")));

        await _authService.ResetPasswordAsync(
            new ResetPasswordRequest("accept@test.com", inviteToken, "NewPassword1!"));

        var login = await _authService.LoginAsync(
            new LoginRequest("accept@test.com", "NewPassword1!"));

        Assert.True(login.User.EmailConfirmed);
        Assert.Equal("Staff", login.User.Role);
    }

    [Fact]
    public async Task ChangeRoleAsync_OnTheLastAdmin_IsRejected()
    {
        var admin = await CreateAdminAsync("only-admin@test.com");

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            _authService.ChangeRoleAsync(
                admin.Id, UserRole.Staff, Guid.NewGuid(), TestConstants.TenantId, UserRole.Admin));

        Assert.Equal("last_admin", ex.Message);
    }

    [Fact]
    public async Task ChangeRoleAsync_OnSelf_IsRejected()
    {
        var admin = await CreateAdminAsync("self@test.com");
        await CreateAdminAsync("other-admin@test.com");

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            _authService.ChangeRoleAsync(
                admin.Id, UserRole.Staff, admin.Id, TestConstants.TenantId, UserRole.Admin));

        Assert.Equal("cannot_change_own_role", ex.Message);
    }

    [Fact]
    public async Task SetActiveAsync_DeactivatingSelf_IsRejected()
    {
        var admin = await CreateAdminAsync("self-deactivate@test.com");

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            _authService.SetActiveAsync(
                admin.Id, false, admin.Id, TestConstants.TenantId, UserRole.Admin));

        Assert.Equal("cannot_deactivate_self", ex.Message);
    }

    [Fact]
    public async Task SetActiveAsync_DeactivatedUser_CannotLogin()
    {
        await CreateAdminAsync("admin3@test.com");
        var parent = await _authService.RegisterAsync(
            new RegisterRequest("deactivated@test.com", "Password1!", "Inativo", TestConstants.TenantId));

        await _authService.SetActiveAsync(
            parent.User.Id, false, Guid.NewGuid(), TestConstants.TenantId, UserRole.Admin);

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            _authService.LoginAsync(new LoginRequest("deactivated@test.com", "Password1!")));

        Assert.Equal("account_inactive", ex.Message);
    }

    [Fact]
    public async Task ConfirmEmailAsync_WithTokenFromResend_ConfirmsTheAccount()
    {
        await _authService.RegisterAsync(
            new RegisterRequest("confirm@test.com", "Password1!", "Confirmar", TestConstants.TenantId));

        var token = await _authService.ResendConfirmationAsync("confirm@test.com");
        Assert.NotNull(token);

        await _authService.ConfirmEmailAsync(new ConfirmEmailRequest("confirm@test.com", token!));

        var stored = await _dbContext.Users.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "confirm@test.com");
        Assert.True(stored.EmailConfirmed);
    }

    [Fact]
    public async Task ResendConfirmationAsync_ForUnknownEmail_ReturnsNull()
    {
        Assert.Null(await _authService.ResendConfirmationAsync("ninguem@test.com"));
    }
}

public static class TestConstants
{
    public static readonly Guid TenantId = new("11111111-1111-1111-1111-111111111111");
}
