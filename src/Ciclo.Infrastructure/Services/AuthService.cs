using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Auth;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Security;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Infrastructure.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> RegisterParentAsync(RegisterParentRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<RefreshedSession> RefreshTokenAsync(string refreshToken);
    Task RevokeTokenAsync(string refreshToken);
    Task<string?> ForgotPasswordAsync(string email);
    Task ResetPasswordAsync(ResetPasswordRequest request);
    Task<AuthResponse> GenerateAuthResponseForUser(User user);

    // M11 — gestão de usuários e verificação de e-mail
    Task<List<UserDto>> ListUsersAsync(Guid tenantId, UserRole actorRole);
    Task<(UserDto User, string InviteToken)> InviteUserAsync(
        InviteUserRequest request, Guid tenantId, UserRole actorRole);
    Task<UserDto> ChangeRoleAsync(
        Guid userId, UserRole role, Guid actorId, Guid tenantId, UserRole actorRole);
    Task<UserDto> SetActiveAsync(
        Guid userId, bool isActive, Guid actorId, Guid tenantId, UserRole actorRole);
    Task<string?> ResendConfirmationAsync(string email);
    Task ConfirmEmailAsync(ConfirmEmailRequest request);
}

public class AuthService : IAuthService
{
    private readonly UserManager<User> _userManager;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AuthService> _logger;
    private readonly IAuditTrail _audit;

    public AuthService(
        UserManager<User> userManager,
        IJwtTokenGenerator tokenGenerator,
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ILogger<AuthService> logger,
        IAuditTrail audit)
    {
        _userManager = userManager;
        _tokenGenerator = tokenGenerator;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
        _audit = audit;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        // Validate tenant exists
        var tenant = await _dbContext.Tenants.FindAsync(request.TenantId);
        if (tenant == null)
            throw new AuthException("invalid_tenant", 400);

        return await CreateParentUserAsync(tenant.Id, request.Email, request.Password, request.Name);
    }

    /// <summary>
    /// A5 - cadastro de responsavel pelo slug publico do colegio. Decisao de produto aplicada
    /// (registrada em plano-correcoes.md): o slug e informacao publica e criar a conta **nao**
    /// concede acesso a aluno nenhum — sem vinculo, o responsavel nao ve nada. O vinculo exige
    /// acao da secretaria, que e o portao de autorizacao real. Convite nominal e o caminho
    /// preferivel a longo prazo e pertence a spec 120 (ciclo de vida de identidade).
    /// </summary>
    public async Task<AuthResponse> RegisterParentAsync(RegisterParentRequest request)
    {
        var slug = request.SchoolSlug?.Trim().ToLowerInvariant() ?? string.Empty;
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null || !tenant.IsActive)
            throw new AuthException("invalid_tenant", 400);

        return await CreateParentUserAsync(tenant.Id, request.Email, request.Password, request.Name);
    }

    /// <summary>
    /// Dono unico da criacao de conta de responsavel: os dois caminhos de cadastro passam por aqui,
    /// para que a regra de e-mail unico e o papel nao possam divergir entre eles (foi o defeito do C2).
    /// </summary>
    private async Task<AuthResponse> CreateParentUserAsync(Guid tenantId, string email, string password, string name)
    {
        // O e-mail é a chave de login: precisa ser único globalmente, não só neste tenant.
        await EmailUniqueness.EnsureAvailableAsync(_dbContext, email);

        var user = new User
        {
            TenantId = tenantId,
            Email = email,
            UserName = email,
            Name = name,
            Role = UserRole.Parent,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Resolve o tenant do usuário para que as consultas internas do Identity
        // (ex.: FindByNameAsync) não falhem por causa do global query filter.
        _tenantContext.SetTenant(tenantId);

        var result = await _userManager.CreateAsync(user, password);
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

        return await GenerateAuthResponse(user, request.RememberMe);
    }

    public async Task<RefreshedSession> RefreshTokenAsync(string refreshToken)
    {
        var storedToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        if (storedToken is null)
            throw new AuthException("invalid_token", 401);

        // B3: o reuso de um token JÁ ROTACIONADO é o sinal de roubo — e é AQUI que ele importa.
        // Antes a detecção ficava no revoke (logout): repetir o logout revogava todas as sessões
        // do usuário, derrubando também os outros dispositivos. No refresh, o token revogado
        // chegando de novo é exatamente o que deve encerrar a família de tokens.
        // B3: o reuso de um token JÁ ROTACIONADO é o sinal de roubo — e é AQUI que ele importa.
        // Antes a detecção ficava no revoke (logout): repetir o logout revogava todas as sessões
        // do usuário, derrubando também os outros dispositivos. No refresh, o token revogado
        // chegando de novo é exatamente o que deve encerrar a família de tokens.
        if (storedToken.IsRevoked)
        {
#pragma warning disable CA1848, CA1873
            _logger.LogWarning("Refresh token reuse detected for user {UserId}", storedToken.UserId);
#pragma warning restore CA1848, CA1873

            await RevokeAllActiveTokensAsync(storedToken.UserId, "reuse_detected");
            throw new AuthException("invalid_token", 401);
        }

        if (!storedToken.IsActive)
            throw new AuthException("invalid_token", 401);

        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == storedToken.UserId);

        if (user == null)
            throw new AuthException("invalid_token", 401);

        // Rotação: o token apresentado deixa de valer e um novo é emitido (GenerateAuthResponse
        // persiste os dois no mesmo SaveChanges).
        var rememberMe = WasRemembered(storedToken);
        storedToken.RevokedAt = DateTime.UtcNow;

        return new RefreshedSession(await GenerateAuthResponse(user, rememberMe), rememberMe);
    }

    public async Task RevokeTokenAsync(string refreshToken)
    {
        var storedToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        // B3: revogar é LOCAL e IDEMPOTENTE. Repetir o logout não pode encerrar as outras
        // sessões do usuário — era o que a detecção de reuso fazia neste ponto.
        // B3: revogar é LOCAL e IDEMPOTENTE. Repetir o logout não pode encerrar as outras
        // sessões do usuário — era o que a detecção de reuso fazia neste ponto.
        if (storedToken is null || storedToken.IsRevoked)
            return;

        // O logout é anônimo: sem JWT o tenant não foi resolvido na borda, e a trilha exige tenant.
        await EnsureTenantResolvedAsync(storedToken.UserId);

        storedToken.RevokedAt = DateTime.UtcNow;

        _audit.Record(new AuditEntry(
            AuditActions.SessionRevoked,
            AuditTargetTypes.Session,
            storedToken.Id,
            new { RevokedAt = (DateTime?)null },
            new { RevokedAt = storedToken.RevokedAt }));

        await _dbContext.SaveChangesAsync();
    }

    private async Task EnsureTenantResolvedAsync(Guid userId)
    {
        if (_tenantContext.IsResolved) return;

        var tenantId = await _dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => (Guid?)u.TenantId)
            .FirstOrDefaultAsync();

        if (tenantId.HasValue)
            _tenantContext.SetTenant(tenantId.Value);
    }

    private async Task RevokeAllActiveTokensAsync(Guid userId, string? reason = null)
    {
        var active = await _dbContext.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync();

        if (active.Count == 0) return;

        await EnsureTenantResolvedAsync(userId);

        var revokedAt = DateTime.UtcNow;
        foreach (var token in active)
        {
            token.RevokedAt = revokedAt;

            if (reason is null)
            {
                _audit.Record(new AuditEntry(
                    AuditActions.SessionRevoked,
                    AuditTargetTypes.Session,
                    token.Id,
                    new { RevokedAt = (DateTime?)null },
                    new { RevokedAt = token.RevokedAt }));
            }
            else
            {
                _audit.Record(new AuditEntry(
                    AuditActions.SessionRevoked,
                    AuditTargetTypes.Session,
                    token.Id,
                    new { RevokedAt = (DateTime?)null },
                    new { RevokedAt = token.RevokedAt, Reason = reason }));
            }
        }

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
        // O token NÃO entra no log: é credencial de redefinição de senha — quem lê o log assume
        // a conta. Registra-se apenas o usuário afetado.
        _logger.LogInformation("Password reset token generated for user {UserId}", user.Id);
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

        // M11: aceitar o convite/reset prova a posse da caixa — o token só chega por e-mail.
        // É este caminho que ativa a conta convidada (que nasce sem senha).
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
        }

#pragma warning disable CA1848, CA1873
        _logger.LogInformation("Password changed for user {UserId} ({Email})", user.Id, user.Email);
#pragma warning restore CA1848, CA1873
    }


    public Task<AuthResponse> GenerateAuthResponseForUser(User user)
    {
        return GenerateAuthResponse(user);
    }

    /// <summary>B4: retenção dos tokens revogados — auditoria de reuso sem crescimento sem fim.</summary>
    private const int RevokedTokenRetentionDays = 30;

    /// <summary>
    /// Duração do refresh token quando a pessoa NÃO pediu para ser lembrada. O cookie que o
    /// carrega é de sessão (morre ao fechar o navegador); este prazo é o teto do lado do
    /// servidor, para que uma sessão sem "lembrar" não sobreviva a um dia inteiro de aba aberta.
    /// </summary>
    private const int SessionLifetimeDays = 1;

    /// <summary>Duração do refresh token quando a pessoa marcou "lembrar o acesso".</summary>
    private const int RememberedLifetimeDays = 30;

    /// <summary>
    /// Onde a política fica gravada: na própria duração do token. Qualquer coisa acima de uma
    /// sessão de um dia é uma sessão lembrada — o valor do meio separa os dois mundos com folga.
    /// </summary>
    private static readonly TimeSpan RememberedThreshold =
        TimeSpan.FromDays((SessionLifetimeDays + RememberedLifetimeDays) / 2.0);

    /// <summary>
    /// A política de "lembrar" vem do TOKEN que chegou, nunca do cliente: quem decide quanto
    /// tempo uma sessão dura é o servidor, e a rotação herda a decisão original (senão o
    /// primeiro refresh rebaixaria uma sessão de 30 dias para sessão de navegador).
    /// </summary>
    private static bool WasRemembered(RefreshToken token) =>
        token.ExpiresAt - token.CreatedAt > RememberedThreshold;

    private async Task<AuthResponse> GenerateAuthResponse(User user, bool rememberMe = false)
    {
        var accessToken = _tokenGenerator.GenerateAccessToken(user);
        var refreshToken = _tokenGenerator.GenerateRefreshToken(user.Id);

        // O gerador produz o segredo; a DURAÇÃO é política de sessão e mora aqui.
        refreshToken.ExpiresAt = DateTime.UtcNow.AddDays(
            rememberMe ? RememberedLifetimeDays : SessionLifetimeDays);

        await PruneStaleTokensAsync(user.Id, DateTime.UtcNow);

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync();

        return new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken.Token,
            User: new UserDto(
                user.Id, user.Email ?? string.Empty, user.Name, user.Role.ToString(), user.TenantId,
                user.EmailConfirmed, user.IsActive));
    }

    // ── M11: gestão de usuários ─────────────────────────────────────

    private static UserDto ToDto(User user) => new(
        user.Id, user.Email ?? string.Empty, user.Name, user.Role.ToString(), user.TenantId,
        user.EmailConfirmed, user.IsActive);

    public async Task<List<UserDto>> ListUsersAsync(Guid tenantId, UserRole actorRole)
    {
        AccessPolicy.Require(AccessPolicy.Roles.UserRead, actorRole);
        _tenantContext.SetTenant(tenantId);

        return await _dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.Name)
            .Select(u => new UserDto(
                u.Id, u.Email ?? string.Empty, u.Name, u.Role.ToString(), u.TenantId, u.EmailConfirmed, u.IsActive))
            .ToListAsync();
    }

    /// <summary>
    /// M11 — convite. A conta nasce SEM SENHA e sem e-mail confirmado; o convite carrega um token
    /// de definição de senha. Aceitar o convite (reset de senha) é o que prova a posse da caixa
    /// de e-mail e ativa a conta — por isso não é preciso bloquear login de quem já tem senha.
    /// </summary>
    public async Task<(UserDto User, string InviteToken)> InviteUserAsync(
        InviteUserRequest request, Guid tenantId, UserRole actorRole)
    {
        AccessPolicy.Require(AccessPolicy.Roles.UserManage, actorRole);

        if (request.Role is not (UserRole.Admin or UserRole.Staff))
            throw new AuthException("invalid_role", 400);

        var email = request.Email.Trim().ToLowerInvariant();
        await EmailUniqueness.EnsureAvailableAsync(_dbContext, email);
        _tenantContext.SetTenant(tenantId);

        var user = new User
        {
            TenantId = tenantId,
            Email = email,
            UserName = email,
            Name = request.Name,
            Role = request.Role,
            IsActive = true,
            EmailConfirmed = false,
            CreatedAt = DateTime.UtcNow
        };

        // Sem senha: até aceitar o convite, o usuário não tem como autenticar.
        var created = await _userManager.CreateAsync(user);
        if (!created.Succeeded)
            throw new AuthException(
                string.Join(", ", created.Errors.Select(e => e.Description)), 400);

        await _userManager.AddToRoleAsync(user, user.Role.ToString());

        var inviteToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        return (ToDto(user), inviteToken);
    }

    public async Task<UserDto> ChangeRoleAsync(
        Guid userId, UserRole role, Guid actorId, Guid tenantId, UserRole actorRole)
    {
        AccessPolicy.Require(AccessPolicy.Roles.UserManage, actorRole);

        if (role is not (UserRole.Admin or UserRole.Staff or UserRole.Parent))
            throw new AuthException("invalid_role", 400);

        // Um Admin rebaixando a si mesmo perderia o acesso à gestão sem ninguém ter decidido isso.
        if (userId == actorId)
            throw new AuthException("cannot_change_own_role", 400);

        _tenantContext.SetTenant(tenantId);
        var user = await _dbContext.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId)
            ?? throw new AuthException("user_not_found", 404);


        if (user.Role == UserRole.Admin && role != UserRole.Admin)
            await EnsureAnotherActiveAdminAsync(tenantId, userId);

        var previousRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, previousRoles);
        await _userManager.AddToRoleAsync(user, role.ToString());

        user.Role = role;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new AuthException(
                string.Join(", ", result.Errors.Select(e => e.Description)), 400);

        return ToDto(user);
    }

    public async Task<UserDto> SetActiveAsync(
        Guid userId, bool isActive, Guid actorId, Guid tenantId, UserRole actorRole)
    {
        AccessPolicy.Require(AccessPolicy.Roles.UserManage, actorRole);

        if (userId == actorId && !isActive)
            throw new AuthException("cannot_deactivate_self", 400);

        _tenantContext.SetTenant(tenantId);
        var user = await _dbContext.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId)
            ?? throw new AuthException("user_not_found", 404);


        if (user.Role == UserRole.Admin && user.IsActive && !isActive)
            await EnsureAnotherActiveAdminAsync(tenantId, userId);

        user.IsActive = isActive;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new AuthException(
                string.Join(", ", result.Errors.Select(e => e.Description)), 400);

        return ToDto(user);
    }

    /// <summary>Não deixa o colégio sem nenhum Admin ativo — seria perder a gestão para sempre.</summary>
    private async Task EnsureAnotherActiveAdminAsync(Guid tenantId, Guid excludingUserId)
    {
        var others = await _dbContext.Users.IgnoreQueryFilters().CountAsync(u =>
            u.TenantId == tenantId && u.Id != excludingUserId
            && u.Role == UserRole.Admin && u.IsActive);

        if (others == 0)
            throw new AuthException("last_admin", 400);
    }

    public async Task<string?> ResendConfirmationAsync(string email)
    {
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user is null || user.EmailConfirmed)
            return null;

        _tenantContext.SetTenant(user.TenantId);
        return await _userManager.GenerateEmailConfirmationTokenAsync(user);
    }

    public async Task ConfirmEmailAsync(ConfirmEmailRequest request)
    {
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user is null)
            throw new AuthException("invalid_token", 400);

        if (user.EmailConfirmed)
            return;

        _tenantContext.SetTenant(user.TenantId);
        var result = await _userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
            throw new AuthException("invalid_token", 400);
    }

    /// <summary>
    /// B4: cada login/refresh insere uma linha e nada nunca saía. A poda acontece na própria
    /// emissão (o único ponto que sempre roda) e remove o que já não serve para nada: tokens
    /// expirados e revogados além da retenção. Um token recém-rotacionado é preservado — é ele
    /// que permite detectar reuso dentro da janela.
    /// </summary>
    private async Task PruneStaleTokensAsync(Guid userId, DateTime now)
    {
        var revokedCutoff = now.AddDays(-RevokedTokenRetentionDays);

        var stale = await _dbContext.RefreshTokens
            .Where(rt => rt.UserId == userId
                && (rt.ExpiresAt < now || (rt.RevokedAt != null && rt.RevokedAt < revokedCutoff)))
            .ToListAsync();

        if (stale.Count == 0)
            return;

        _dbContext.RefreshTokens.RemoveRange(stale);
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
