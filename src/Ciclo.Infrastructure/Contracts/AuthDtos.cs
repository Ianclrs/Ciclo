using Ciclo.Core.Entities;

namespace Ciclo.Infrastructure.Contracts;

public record RegisterRequest(string Email, string Password, string Name, Guid TenantId);

/// <summary>
/// A5 - cadastro de responsavel. O responsavel nao conhece o id interno do colegio, entao informa
/// o slug publico (o mesmo que o colegio recebe ao se cadastrar). Criar a conta **nao** vincula
/// aluno nenhum: o vinculo continua sendo ato da secretaria.
/// </summary>
public record RegisterParentRequest(string SchoolSlug, string Email, string Password, string Name);
/// <summary>
/// "Lembrar o acesso": quando verdadeiro, o refresh token e o cookie que o carrega duram
/// 30 dias; quando falso, o cookie e de sessao (morre ao fechar o navegador) e o token vale
/// um dia. O padrao falso garante que um cliente antigo, que nao envia o campo, nao ganhe
/// sessao longa sem ter pedido.
/// </summary>
public record LoginRequest(string Email, string Password, bool RememberMe = false);
public record AuthResponse(string AccessToken, string RefreshToken, UserDto User);

/// <summary>
/// Resultado de uma renovacao. O <see cref="RememberMe"/> acompanha a resposta porque o
/// controller precisa reemitir o cookie com a MESMA politica da sessao original: um cookie
/// persistente reescrito sem validade viraria cookie de sessao, e a pessoa perderia o
/// "lembrar" no primeiro refresh.
/// </summary>
public record RefreshedSession(AuthResponse Response, bool RememberMe)
{
    public string AccessToken => Response.AccessToken;
    public string RefreshToken => Response.RefreshToken;
    public UserDto User => Response.User;
}

/// <summary>
/// M11: <c>EmailConfirmed</c> passa a integrar o contrato — a tela pode cobrar a confirmação
/// sem precisar de outro endpoint só para isso.
/// </summary>
public record UserDto(
    Guid Id, string Email, string Name, string Role, Guid TenantId, bool EmailConfirmed, bool IsActive);

public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Token, string NewPassword);
public record RefreshRequest();

// ── M11: gestão de usuários e verificação de e-mail ─────────────────

public record InviteUserRequest(string Email, string Name, UserRole Role);
public record ChangeUserRoleRequest(UserRole Role);
public record SetUserActiveRequest(bool IsActive);
public record ConfirmEmailRequest(string Email, string Token);
