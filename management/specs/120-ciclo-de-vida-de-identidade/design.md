# Spec 120: Ciclo de Vida de Identidade — Design

## Design Approach

O ciclo de identidade é entregue estendendo o dono existente — `AuthService` em `src/Ciclo.Infrastructure/Services/AuthService.cs` — em vez de criar um serviço paralelo. O convite deixa de ser um token de redefinição de senha do Identity (que não é persistido, não é hasheável e não expira de forma explícita) e passa a ser um agregado próprio, `UserInvitation`, que guarda apenas o hash SHA-256 do token, a expiração e o instante de consumo. A expiração é avaliada na leitura, sem job de limpeza: o projeto não possui host de jobs, e a V13/V14 é quem introduz esse host. O aceite reutiliza `UserManager.AddPasswordAsync`, porque a conta de convite nasce sem `PasswordHash`. A desativação reutiliza `RevokeAllActiveTokensAsync`, que já existe para a rotação de refresh. No frontend, a página de definição de senha já existente ganha um modo convite acionado por `invite=1` na URL, sem rota nova.

Decisões de princípio aplicadas: um único dono por comportamento (hash, aceite, revogação e bloqueio vivem no serviço de identidade), interfaces estreitas (um método novo em `IAuthService`), zero abstração especulativa (nada de repositório, fábrica ou serviço de token separado).

## Architecture Decisions

- **AD-1 — Convite como entidade persistida, não como token do Identity.** `GeneratePasswordResetTokenAsync` devolve um token protegido por Data Protection sem linha no banco: não há como impor uso único com carimbo, rotação nem expiração explícita. `UserInvitation` resolve os três com uma coluna indexada.
- **AD-2 — Somente hash em repouso.** O valor em claro é gerado com `RandomNumberGenerator`, devolvido na resposta de criação e descartado; o banco guarda SHA-256 em hexadecimal minúsculo. Vazamento do banco não entrega convites utilizáveis.
- **AD-3 — Expiração avaliada na leitura.** Sem `IHostedService`, sem job agendado. `ExpiresAt > DateTime.UtcNow` é o único critério, o que mantém o comportamento determinístico e testável sem relógio simulado.
- **AD-4 — Aceite em endpoint próprio.** `POST api/auth/accept-invite` em vez de sobrecarregar `reset-password` com dois tipos de token. Explícito vence implícito; a forma do corpo é idêntica à do reset para reaproveitar a tela.
- **AD-5 — Rotação em vez de recusa no reconvite.** Convidar um e-mail que já pertence ao mesmo tenant com convite pendente invalida os pendentes e emite um novo. Evita `email_in_use` como beco sem saída e elimina a intervenção manual no banco, que é justamente o que a V12 elimina.
- **AD-6 — Revogação apenas na desativação.** A V12 nomeia revogação de sessões na desativação; a troca de papel fica fora (ver Out of Scope) para não ampliar o contrato sem exigência.

## Component / Module Breakdown

### Component: UserInvitation (entidade de domínio)

**Purpose:** Representa um convite nominal pendente ou consumido, com hash do token, expiração e carimbo de uso.
**Interface:**
```csharp
namespace Ciclo.Core.Entities;

public class UserInvitation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public User User { get; set; } = null!;
}
```
**Internal Logic:** Entidade de dados pura, no mesmo padrão de `RefreshToken`: propriedades de estado derivado (`IsUsed`, `IsExpired`, `IsPending`) não são persistidas e existem apenas para leitura clara nas consultas do serviço.
**Dependencies:** `Ciclo.Core.Entities.User`.
**Error Handling:** Não lança. Todo critério de validade é decidido pelo serviço que a lê; a entidade nunca valida sozinha para não duplicar a regra.

### Component: AppDbContext (mapeamento)

**Purpose:** Registra e configura o novo agregado no contexto único do projeto.
**Interface:** `public DbSet<UserInvitation> Invitations { get; set; } = null!;` mais um bloco `builder.Entity<UserInvitation>(entity => { })` em `OnModelCreating`.
**Internal Logic:** Configura `entity.HasKey(i => i.Id)`, `entity.Property(i => i.TokenHash).IsRequired().HasMaxLength(64)`, `entity.HasIndex(i => i.TokenHash).IsUnique()`, `entity.HasIndex(i => i.UserId)`, `entity.HasOne(i => i.User).WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade)`. Nenhum query filter global novo é criado para `UserInvitation`: o aceite ocorre antes de existir tenant resolvido, então o serviço lê essa tabela com `IgnoreQueryFilters()`, exatamente como já faz com `Users`.
**Dependencies:** `Ciclo.Core.Entities.UserInvitation`.
**Error Handling:** Violação do índice único em `TokenHash` propaga `DbUpdateException` como qualquer outra escrita; o hash de 256 bits torna a colisão desprezível e não recebe tratamento especial.

### Component: AuthService.InviteUserAsync (alterado)

**Purpose:** Cria a conta sem senha e emite o convite com hash, expiração e rotação dos pendentes.
**Interface:** assinatura preservada — `Task<(UserDto User, string InviteToken)> InviteUserAsync(InviteUserRequest request, Guid tenantId, UserRole actorRole)`.
**Internal Logic:**
1. `AccessPolicy.Require(AccessPolicy.Roles.UserManage, actorRole)`.
2. Rejeita papel fora de `Admin`/`Staff` com `invalid_role` 400.
3. Normaliza `email = request.Email.Trim().ToLowerInvariant()`.
4. Consulta `_dbContext.Users.IgnoreQueryFilters()` por esse e-mail. Se existir usuário no mesmo tenant com convite pendente (`UsedAt == null && ExpiresAt > now`), entra no ramo de rotação: carimba `UsedAt = now` nos pendentes e segue com esse usuário. Se existir usuário em outro tenant, ou usuário já ativo com senha, delega a `EmailUniqueness.EnsureAvailableAsync`, que lança `email_in_use`.
5. Sem usuário: cria `User` com `IsActive = true`, `EmailConfirmed = false`, `Role = request.Role` e sem senha; executa `_userManager.CreateAsync(user)` e `_userManager.AddToRoleAsync(user, role.ToString())`.
6. Gera `token = GenerateInvitationToken()`, adiciona `UserInvitation { UserId, TenantId, TokenHash = HashInvitationToken(token), ExpiresAt = now.AddDays(InvitationLifetimeDays) }` e persiste com `SaveChangesAsync`.
7. Retorna `(ToDto(user), token)`.
**Dependencies:** `AccessPolicy`, `EmailUniqueness`, `UserManager<User>`, `AppDbContext`.
**Error Handling:** Falha em `CreateAsync` ou `AddToRoleAsync` lança `AuthException` 400 com as descrições agregadas, como hoje, e aborta antes de qualquer convite; nenhum convite órfão é gravado porque a linha só é adicionada depois de o usuário existir.

### Component: AuthService.AcceptInviteAsync (novo)

**Purpose:** Consome o convite, define a primeira senha e confirma o e-mail na mesma operação.
**Interface:** `Task AcceptInviteAsync(AcceptInviteRequest request)` — declarado também em `IAuthService`.
**Internal Logic:**
1. Se `request.Email` ou `request.Token` estiverem vazios, lança `AuthException("invite_invalid", 400)`.
2. `user = _dbContext.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == emailNormalizado)`; nulo lança `invite_invalid` 400.
3. `!user.IsActive` lança `account_inactive` 403.
4. `_tenantContext.SetTenant(user.TenantId)`.
5. `hash = HashInvitationToken(request.Token)`. Existe linha com esse hash e `UsedAt != null` para o usuário: lança `invite_used` 400. Existe linha com esse hash e `ExpiresAt <= now`: lança `invite_expired` 400. Nenhuma linha com esse hash: lança `invite_invalid` 400.
6. `_userManager.AddPasswordAsync(user, request.NewPassword)`; falha lança `invalid_password` 400 com `string.Join(", ", result.Errors.Select(e => e.Description))`, antes de qualquer escrita de estado.
7. `invitation.UsedAt = now`; `user.EmailConfirmed = true`; carimba `UsedAt = now` nos demais convites pendentes do usuário; `_userManager.UpdateAsync(user)` e `SaveChangesAsync()`.
**Dependencies:** `AppDbContext`, `ITenantContext`, `UserManager<User>`.
**Error Handling:** Cada rejeição é `AuthException` com código e status da tabela de Edge Cases, traduzida a ProblemDetails por `ProblemResponse.FromCode` no controller. Nenhum caminho escreve estado parcial: a senha é aplicada por `AddPasswordAsync` (que persiste o `PasswordHash`) e as três marcas de convite e confirmação são gravadas na mesma chamada de `SaveChangesAsync`.

### Component: AuthService.LoginAsync (alterado)

**Purpose:** Bloqueia o primeiro login enquanto o convite estiver pendente.
**Interface:** assinatura preservada — `Task<AuthResponse> LoginAsync(LoginRequest request)`.
**Internal Logic:** após localizar o usuário e checar `IsActive`, e antes de `CheckPasswordSignInAsync`, consulta se existe `UserInvitation` com `UsedAt == null && ExpiresAt > now` para aquele `UserId`; existindo, lança `AuthException("invitation_pending", 403)`.
**Dependencies:** `AppDbContext`.
**Error Handling:** O bloqueio acontece antes de qualquer registro de tentativa falha, de modo que `AccessFailedCount` e `LockoutEnd` não são tocados. `account_inactive` continua com precedência.

### Component: AuthService.SetActiveAsync (alterado)

**Purpose:** Revoga todas as sessões ativas quando o usuário é desativado.
**Interface:** assinatura preservada — `Task<UserDto> SetActiveAsync(Guid userId, bool isActive, Guid actorId, Guid tenantId, UserRole actorRole)`.
**Internal Logic:** mantém as validações atuais (`cannot_deactivate_self`, `user_not_found`, `last_admin`), persiste `user.IsActive = isActive` via `UpdateAsync` e, somente quando `!isActive` e a atualização teve sucesso, chama `RevokeAllActiveTokensAsync(user.Id, "account_deactivated")` e `SaveChangesAsync()`.
**Dependencies:** `RevokeAllActiveTokensAsync` (privado, já existente).
**Error Handling:** Se `UpdateAsync` falhar, lança `AuthException` 400 e nenhum token é revogado. Reativação não executa a revogação.

### Component: GenerateInvitationToken / HashInvitationToken (privados)

**Purpose:** Único ponto que produz e transforma o segredo do convite.
**Interface:**
```csharp
private const int InvitationLifetimeDays = 7;
private static string GenerateInvitationToken();
private static string HashInvitationToken(string token);
```
**Internal Logic:** `GenerateInvitationToken` usa `RandomNumberGenerator.GetBytes(32)` e converte para Base64Url (`Convert.ToBase64String(bytes)` com `+` trocado por `-`, `/` trocado por `_` e `=` removido do fim), produzindo 43 caracteres. `HashInvitationToken` usa `SHA256.HashData(Encoding.UTF8.GetBytes(token))` e `Convert.ToHexString(bytes).ToLowerInvariant()`, produzindo 64 caracteres.
**Dependencies:** `System.Security.Cryptography`, `System.Text`.
**Error Handling:** `HashInvitationToken(null)` lança `ArgumentNullException`; o serviço valida token vazio antes de chamar.

### Component: AcceptInviteRequest (DTO)

**Purpose:** Corpo do aceite de convite.
**Interface:** `public sealed record AcceptInviteRequest(string Email, string Token, string NewPassword);` em `src/Ciclo.Infrastructure/Contracts/AuthDtos.cs`.
**Internal Logic:** Record posicional, sem validação própria; a validação é do serviço.
**Dependencies:** Nenhuma.
**Error Handling:** Corpo ausente é rejeitado pelo binding do ASP.NET Core com 400 antes do serviço.

### Component: AuthController.AcceptInvite (novo endpoint)

**Purpose:** Expor o aceite de convite publicamente com rate limit.
**Interface:**
```csharp
[HttpPost("accept-invite")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request)
```
**Internal Logic:** `await _authService.AcceptInviteAsync(request); return NoContent();` dentro de `try/catch (AuthException ex)` com `return ProblemResponse.FromCode(ex.Message, ex.StatusCode);`, idêntico aos endpoints vizinhos.
**Dependencies:** `IAuthService`, `ProblemResponse`, `RateLimitPolicies.Auth`.
**Error Handling:** Toda `AuthException` vira ProblemDetails com o status do código; nenhum erro é mascarado como 200.

### Component: Frontend — api/auth.ts e api/client.ts

**Purpose:** Cliente do novo endpoint e liberação do caminho anônimo.
**Interface:**
```ts
export async function acceptInvite(email: string, token: string, newPassword: string): Promise<void> {
  await api.post('/api/auth/accept-invite', { email, token, newPassword });
}
```
e, em `frontend/src/api/client.ts`, `anonymousAuthEndpoints` passa a incluir `'/api/auth/accept-invite'`.
**Internal Logic:** Mesmo padrão de `resetPassword`; a lista de endpoints anônimos evita disparar o refresh automático em um 400 que pertence à tela.
**Dependencies:** `frontend/src/api/client.ts`.
**Error Handling:** Erros são propagados ao chamador; a interceptação de sessão expirada não se aplica a esses caminhos.

### Component: Frontend — ForgotPasswordPage no modo convite

**Purpose:** Dar ao convidado o caminho de resgate do token sem rota nova.
**Interface:** componente padrão existente, sem mudança de props.
**Internal Logic:** na montagem, `const params = new URLSearchParams(window.location.search); const inviteMode = params.get('invite') === '1';`. Com `inviteMode` verdadeiro: `email` inicia com `params.get('email') ?? ''`, `resetToken` inicia com `params.get('token') ?? ''` e `step` inicia em `'reset'`, pulando a etapa de e-mail. `handleResetSubmit` chama `acceptInvite(email, resetToken, newPassword)` em vez de `resetPassword` e, no sucesso, emite o aviso de convite aceito e navega para `/login`.
**Dependencies:** `frontend/src/api/auth.ts`.
**Error Handling:** Falha exibe `err.response.data.title` como hoje e mantém o formulário preenchido; sem `invite=1` o comportamento atual permanece intacto.

## Data Flow

**Convite (Admin convida Staff):** `POST api/users/invite` (inalterado) → `UsersController` → `AuthService.InviteUserAsync` → usuário sem senha criado → `UserInvitation` com `TokenHash` e `ExpiresAt` gravada → resposta `{ user, inviteToken }` → a tela administrativa mostra o token uma única vez.

**Aceite (convidado define a senha):** link com `?invite=1&email=...&token=...` → `ForgotPasswordPage` abre no passo de senha → `acceptInvite` → `POST api/auth/accept-invite` → `AuthService.AcceptInviteAsync` → valida hash, uso e expiração → `AddPasswordAsync` → `UsedAt` e `EmailConfirmed = true` → 204 → a tela navega para o login.

**Primeiro login:** `POST api/auth/login` → `AuthService.LoginAsync` → usuário ativo → checagem de convite pendente: havendo convite, 403 `invitation_pending`; após o aceite o convite não é mais pendente, a senha confere e a sessão é emitida com refresh token no cookie.

**Desativação:** `PATCH api/users/{id}/active` com `isActive` falso → `AuthService.SetActiveAsync` → `UpdateAsync` → `RevokeAllActiveTokensAsync` → todos os refresh tokens ativos recebem `RevokedAt` → `POST api/auth/refresh` com o token antigo falha na renovação → o usuário só volta por novo login depois da reativação.

## Data Structures

**Tabela `Invitations` (PostgreSQL, criada pela migration EF):**

| Coluna | Tipo | Restrição |
|---|---|---|
| Id | uuid | chave primária |
| UserId | uuid | FK para `Users`, cascade delete, índice |
| TenantId | uuid | obrigatório, metadado de rastreio |
| TokenHash | varchar(64) | obrigatório, índice único |
| CreatedAt | timestamptz | obrigatório, UTC |
| ExpiresAt | timestamptz | obrigatório |
| UsedAt | timestamptz | nulo enquanto pendente |

**DTO do aceite:** `AcceptInviteRequest(string Email, string Token, string NewPassword)`.

**Códigos de erro do ciclo:** `invite_invalid` 400, `invite_expired` 400, `invite_used` 400, `invalid_password` 400, `account_inactive` 403, `invitation_pending` 403.

## File / Module Layout

- `src/Ciclo.Core/Entities/UserInvitation.cs` **novo** — entidade descrita acima.
- `src/Ciclo.Infrastructure/Data/AppDbContext.cs` **alterado** — `DbSet<UserInvitation> Invitations` e configuração em `OnModelCreating`.
- `src/Ciclo.Infrastructure/Services/AuthService.cs` **alterado** — `IAuthService.AcceptInviteAsync`, `InviteUserAsync`, `LoginAsync`, `SetActiveAsync`, `GenerateInvitationToken`, `HashInvitationToken`, `InvitationLifetimeDays`.
- `src/Ciclo.Infrastructure/Contracts/AuthDtos.cs` **alterado** — `AcceptInviteRequest`.
- `src/Ciclo.Api/Controllers/AuthController.cs` **alterado** — ação `AcceptInvite`.
- `src/Ciclo.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` **alterado** — atualizado pela migration `AddUserInvitations`, gerada com `dotnet ef migrations add AddUserInvitations --project src/Ciclo.Infrastructure --startup-project src/Ciclo.Api` e gravada no diretório de migrations já existente do projeto de infraestrutura.
- `frontend/src/api/auth.ts` **alterado** — `acceptInvite`.
- `frontend/src/api/client.ts` **alterado** — caminho anônimo.
- `frontend/src/pages/ForgotPasswordPage.tsx` **alterado** — modo convite.
- `tests/Ciclo.Api.Tests/InvitationLifecycleTests.cs` **novo** — suíte do ciclo de convite e da revogação.
- `tests/Ciclo.Api.Tests/AuthServiceTests.cs` **alterado** — ajuste dos testes de convite existentes.

## Testing Strategy

**`tests/Ciclo.Api.Tests/InvitationLifecycleTests.cs` (novo, xUnit com InMemory, mesmo molde de `AuthServiceTests`):** constrói o `ServiceProvider` com `AddIdentityCore<User>` mais roles e `AddDefaultTokenProviders`, `ITenantContext` e `IJwtTokenGenerator` dublados, `NoopAuditTrail` e `CapturingLogger<AuthService>`, semeando o tenant de `TestConstants` e as roles Admin/Staff/Parent. Cenários provados:
- convite grava exatamente uma linha em `Invitations` com `TokenHash` de 64 caracteres diferente do token devolvido e `ExpiresAt` a sete dias;
- reconvite do mesmo e-mail carimba `UsedAt` no convite anterior e emite token novo aceito, enquanto o token antigo retorna `invite_used`;
- aceite feliz define a senha, marca `EmailConfirmed` e `UsedAt`, e o login subsequente devolve `AuthResponse`;
- login antes do aceite retorna `invitation_pending` 403 e não incrementa `AccessFailedCount`;
- token desconhecido, convite expirado (linha com `ExpiresAt` no passado inserida diretamente), convite já usado e usuário inativo produzem `invite_invalid`, `invite_expired`, `invite_used` e `account_inactive`;
- senha fora da política produz `invalid_password` sem alterar `UsedAt` nem `EmailConfirmed`;
- desativação revoga os refresh tokens ativos e `RefreshTokenAsync` do token antigo passa a falhar; a reativação não devolve o token revogado.

**`tests/Ciclo.Api.Tests/AuthServiceTests.cs` (existente):** os testes de convite passam a assumir o convite persistido; as asserções dos fluxos não tocados (registro, login, refresh, revogação, recuperação de senha, poda) permanecem sem mudança de expectativa.

**Frontend:** `npm run build` e `npm run lint` no diretório `frontend` provam a compilação dos três arquivos alterados; a verificação visual da tela é manual e não entra na régua desta spec.

**Test-scope declaration:** `tests/Ciclo.Api.Tests/InvitationLifecycleTests.cs` e `tests/Ciclo.Api.Tests/AuthServiceTests.cs`.

## Migration / Rollback

**Migration:** `dotnet ef migrations add AddUserInvitations` cria a tabela `Invitations` com os índices descritos. A tabela é nova e vazia em produção, portanto não há backfill: tokens de convite emitidos no formato antigo (token do Identity) deixam de ser aceitos e o caminho de recuperação para eles é um novo convite emitido pelo Admin, que agora invalida pendentes e emite token hasheado.

**Rollback:** reverter a migration remove a tabela `Invitations` sem tocar em `Users`, de modo que nenhum dado de usuário é perdido. Como efeito colateral, convites pendentes deixam de ser aceitos e contas criadas por convite e ainda sem senha ficam sem caminho de ativação, recuperável por um novo convite após a reaplicação.
