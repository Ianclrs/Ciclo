using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Auth;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 120 — ciclo de identidade ponta a ponta: o convite guarda só o hash com expiração, o
/// aceite define a primeira senha e confirma o e-mail, o primeiro login fica bloqueado enquanto
/// houver convite pendente e a desativação encerra as sessões já emitidas.
/// </summary>
public sealed class InvitationLifecycleTests : IDisposable
{
    private const string InviteeEmail = "convidado@test.com";
    private const string InviteePassword = "Password1!";

    private readonly ServiceProvider _serviceProvider;
    private readonly AppDbContext _dbContext;
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly IAuthService _authService;

    public InvitationLifecycleTests()
    {
        var services = new ServiceCollection();

        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));

        services.AddDataProtection();

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

        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(tc => tc.IsResolved).Returns(false);
        services.AddScoped<ITenantContext>(_ => tenantContextMock.Object);

        var tokenMock = new Mock<IJwtTokenGenerator>();
        tokenMock.Setup(t => t.GenerateAccessToken(It.IsAny<User>()))
            .Returns("mock-access-token");
        tokenMock.Setup(t => t.GenerateRefreshToken(It.IsAny<Guid>()))
            .Returns((Guid userId) => new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            });
        services.AddScoped<IJwtTokenGenerator>(_ => tokenMock.Object);

        services.AddScoped<IAuditTrail>(_ => NoopAuditTrail.Instance);
        services.AddScoped<IAuthService, AuthService>();
        services.AddSingleton<ILogger<AuthService>>(new CapturingLogger<AuthService>());

        _serviceProvider = services.BuildServiceProvider();

        _dbContext = _serviceProvider.GetRequiredService<AppDbContext>();
        _userManager = _serviceProvider.GetRequiredService<UserManager<User>>();
        _roleManager = _serviceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        _authService = _serviceProvider.GetRequiredService<IAuthService>();

        SeedAsync().GetAwaiter().GetResult();
    }

    private async Task SeedAsync()
    {
        _dbContext.Tenants.Add(new Tenant
        {
            Id = TestConstants.TenantId,
            Name = "Test School",
            Slug = "test-school",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        await _roleManager.CreateAsync(new IdentityRole<Guid>("Admin"));
        await _roleManager.CreateAsync(new IdentityRole<Guid>("Staff"));
        await _roleManager.CreateAsync(new IdentityRole<Guid>("Parent"));

        await _dbContext.SaveChangesAsync();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    private Task<(UserDto User, string InviteToken)> InviteAsync(string email)
        => _authService.InviteUserAsync(
            new InviteUserRequest(email, "Convidado", UserRole.Staff),
            TestConstants.TenantId,
            UserRole.Admin);

    private Task<User> LoadUserAsync(string email)
        => _dbContext.Users.IgnoreQueryFilters().FirstAsync(u => u.Email == email);

    private async Task<UserInvitation> LoadInvitationAsync(Guid userId)
        => await _dbContext.Invitations.IgnoreQueryFilters().SingleAsync(i => i.UserId == userId);

    private Task AcceptAsync(string email, string token, string password)
        => _authService.AcceptInviteAsync(new AcceptInviteRequest(email, token, password));

    // ===== Emissão do convite =====

    [Fact]
    public async Task InviteUserAsync_PersistsOnlyTheHashWithSevenDayExpiry()
    {
        var (user, inviteToken) = await InviteAsync(InviteeEmail);

        var invitation = await LoadInvitationAsync(user.Id);

        // O valor em claro existe apenas na resposta ao Admin; o banco guarda o SHA-256.
        Assert.NotEqual(inviteToken, invitation.TokenHash);
        Assert.Matches("^[0-9a-f]{64}$", invitation.TokenHash);
        Assert.Equal(43, inviteToken.Length);

        Assert.Equal(TestConstants.TenantId, invitation.TenantId);
        Assert.Null(invitation.UsedAt);
        Assert.Equal(TimeSpan.FromDays(7), invitation.ExpiresAt - invitation.CreatedAt);
    }

    [Fact]
    public async Task ReInvitingTheSameEmail_RotatesThePendingInvite()
    {
        var (user, firstToken) = await InviteAsync(InviteeEmail);
        var (_, secondToken) = await InviteAsync(InviteeEmail);

        var invitations = await _dbContext.Invitations
            .IgnoreQueryFilters()
            .Where(i => i.UserId == user.Id)
            .ToListAsync();

        Assert.Equal(2, invitations.Count);
        Assert.Single(invitations, i => i.UsedAt == null);

        // O token antigo deixa de ser aceito no instante da rotação.
        var rejected = await Assert.ThrowsAsync<AuthException>(
            () => AcceptAsync(InviteeEmail, firstToken, InviteePassword));
        Assert.Equal("invite_used", rejected.Message);
        Assert.Equal(400, rejected.StatusCode);

        // O token novo é o único caminho que abre a conta.
        await AcceptAsync(InviteeEmail, secondToken, InviteePassword);

        var login = await _authService.LoginAsync(new LoginRequest(InviteeEmail, InviteePassword));
        Assert.NotNull(login.AccessToken);
    }

    // ===== Aceite do convite =====

    [Fact]
    public async Task AcceptInviteAsync_SetsThePasswordConfirmsEmailAndConsumesTheInvite()
    {
        var (user, inviteToken) = await InviteAsync(InviteeEmail);

        var beforeAccept = await LoadUserAsync(InviteeEmail);
        Assert.False(await _userManager.HasPasswordAsync(beforeAccept));

        await AcceptAsync(InviteeEmail, inviteToken, InviteePassword);

        var invitation = await LoadInvitationAsync(user.Id);
        Assert.NotNull(invitation.UsedAt);

        var accepted = await LoadUserAsync(InviteeEmail);
        Assert.True(accepted.EmailConfirmed);
        Assert.True(await _userManager.HasPasswordAsync(accepted));

        var login = await _authService.LoginAsync(new LoginRequest(InviteeEmail, InviteePassword));
        Assert.Equal("mock-access-token", login.AccessToken);
    }

    [Fact]
    public async Task AcceptInviteAsync_WithAnUnknownToken_IsRejectedAsInvalid()
    {
        await InviteAsync(InviteeEmail);

        var ex = await Assert.ThrowsAsync<AuthException>(
            () => AcceptAsync(InviteeEmail, "token-que-nao-existe", InviteePassword));

        Assert.Equal("invite_invalid", ex.Message);
        Assert.Equal(400, ex.StatusCode);
    }

    /// <summary>
    /// E-mail inexistente e token desconhecido respondem o mesmo código: o endpoint não serve
    /// para descobrir quais caixas existem no colégio.
    /// </summary>
    [Fact]
    public async Task AcceptInviteAsync_ForAnUnknownEmail_IsRejectedAsInvalid()
    {
        var ex = await Assert.ThrowsAsync<AuthException>(
            () => AcceptAsync("ninguem@test.com", "token", InviteePassword));

        Assert.Equal("invite_invalid", ex.Message);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task AcceptInviteAsync_WithAnExpiredInvite_IsRejectedAsExpired()
    {
        var (user, inviteToken) = await InviteAsync(InviteeEmail);

        var invitation = await LoadInvitationAsync(user.Id);
        invitation.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await _dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<AuthException>(
            () => AcceptAsync(InviteeEmail, inviteToken, InviteePassword));

        Assert.Equal("invite_expired", ex.Message);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task AcceptInviteAsync_WithAnAlreadyUsedInvite_IsRejectedAsUsed()
    {
        var (_, inviteToken) = await InviteAsync(InviteeEmail);
        await AcceptAsync(InviteeEmail, inviteToken, InviteePassword);

        // Segunda apresentação do mesmo token — inclusive o caso de duas requisições concorrentes.
        var ex = await Assert.ThrowsAsync<AuthException>(
            () => AcceptAsync(InviteeEmail, inviteToken, "OutraSenha1!"));

        Assert.Equal("invite_used", ex.Message);
        Assert.Equal(400, ex.StatusCode);

        // A senha do primeiro aceite nunca é sobrescrita por credencial divergente.
        var login = await _authService.LoginAsync(new LoginRequest(InviteeEmail, InviteePassword));
        Assert.NotNull(login.AccessToken);
    }

    [Fact]
    public async Task AcceptInviteAsync_ForAnInactiveUser_IsRejectedAndKeepsTheInvitePending()
    {
        var (user, inviteToken) = await InviteAsync(InviteeEmail);

        var stored = await LoadUserAsync(InviteeEmail);
        stored.IsActive = false;
        await _dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<AuthException>(
            () => AcceptAsync(InviteeEmail, inviteToken, InviteePassword));

        Assert.Equal("account_inactive", ex.Message);
        Assert.Equal(403, ex.StatusCode);

        var invitation = await LoadInvitationAsync(user.Id);
        Assert.Null(invitation.UsedAt);
    }

    [Fact]
    public async Task AcceptInviteAsync_WithAWeakPassword_IsRejectedWithoutChangingState()
    {
        var (user, inviteToken) = await InviteAsync(InviteeEmail);

        var ex = await Assert.ThrowsAsync<AuthException>(
            () => AcceptAsync(InviteeEmail, inviteToken, "fraca"));

        Assert.Equal("invalid_password", ex.Message);
        Assert.Equal(400, ex.StatusCode);

        var invitation = await LoadInvitationAsync(user.Id);
        Assert.Null(invitation.UsedAt);

        var stored = await LoadUserAsync(InviteeEmail);
        Assert.False(stored.EmailConfirmed);
        Assert.False(await _userManager.HasPasswordAsync(stored));
    }

    // ===== Primeiro login =====

    [Fact]
    public async Task LoginBeforeAcceptingTheInvite_IsBlockedWithoutCountingAFailure()
    {
        await InviteAsync(InviteeEmail);

        var ex = await Assert.ThrowsAsync<AuthException>(
            () => _authService.LoginAsync(new LoginRequest(InviteeEmail, InviteePassword)));

        Assert.Equal("invitation_pending", ex.Message);
        Assert.Equal(403, ex.StatusCode);

        // O bloqueio acontece antes da verificação de senha: não aproxima a conta do lockout.
        var stored = await LoadUserAsync(InviteeEmail);
        Assert.Equal(0, stored.AccessFailedCount);
    }

    [Fact]
    public async Task LoginAfterAcceptingTheInvite_Succeeds()
    {
        var (_, inviteToken) = await InviteAsync(InviteeEmail);
        await AcceptAsync(InviteeEmail, inviteToken, InviteePassword);

        var login = await _authService.LoginAsync(new LoginRequest(InviteeEmail, InviteePassword));

        Assert.Equal("mock-access-token", login.AccessToken);
        Assert.NotNull(login.RefreshToken);
        Assert.True(login.User.EmailConfirmed);
    }

    // ===== Desativação =====

    [Fact]
    public async Task DeactivatingAUser_RevokesTheSessionsIssuedBefore()
    {
        var (user, inviteToken) = await InviteAsync(InviteeEmail);
        await AcceptAsync(InviteeEmail, inviteToken, InviteePassword);

        var session = await _authService.LoginAsync(new LoginRequest(InviteeEmail, InviteePassword));

        await _authService.SetActiveAsync(
            user.Id, false, Guid.NewGuid(), TestConstants.TenantId, UserRole.Admin);

        var storedToken = await _dbContext.RefreshTokens
            .FirstAsync(rt => rt.Token == session.RefreshToken);
        Assert.True(storedToken.IsRevoked, "a desativação não revogou o refresh token ativo");

        var renewal = await Assert.ThrowsAsync<AuthException>(
            () => _authService.RefreshTokenAsync(session.RefreshToken));
        Assert.Equal("invalid_token", renewal.Message);
        Assert.Equal(401, renewal.StatusCode);
    }

    [Fact]
    public async Task ReactivatingAUser_DoesNotRestoreTheRevokedSession()
    {
        var (user, inviteToken) = await InviteAsync(InviteeEmail);
        await AcceptAsync(InviteeEmail, inviteToken, InviteePassword);

        var session = await _authService.LoginAsync(new LoginRequest(InviteeEmail, InviteePassword));

        await _authService.SetActiveAsync(
            user.Id, false, Guid.NewGuid(), TestConstants.TenantId, UserRole.Admin);
        await _authService.SetActiveAsync(
            user.Id, true, Guid.NewGuid(), TestConstants.TenantId, UserRole.Admin);

        var renewal = await Assert.ThrowsAsync<AuthException>(
            () => _authService.RefreshTokenAsync(session.RefreshToken));
        Assert.Equal("invalid_token", renewal.Message);

        // O caminho de volta é um novo login, que emite uma sessão nova.
        var fresh = await _authService.LoginAsync(new LoginRequest(InviteeEmail, InviteePassword));
        Assert.NotNull(fresh.RefreshToken);
        Assert.NotEqual(session.RefreshToken, fresh.RefreshToken);
    }

    [Fact]
    public async Task DeactivatingAUserWithoutActiveSessions_StillSucceeds()
    {
        var (user, inviteToken) = await InviteAsync(InviteeEmail);
        await AcceptAsync(InviteeEmail, inviteToken, InviteePassword);

        // Nenhuma sessão emitida: a revogação é no-op e a resposta não muda.
        var result = await _authService.SetActiveAsync(
            user.Id, false, Guid.NewGuid(), TestConstants.TenantId, UserRole.Admin);

        Assert.False(result.IsActive);
    }
}
