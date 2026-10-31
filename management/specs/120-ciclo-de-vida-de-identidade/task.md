<!-- PIPELINE-CONTRACT: Task completion is determined by "**Status:** [x] done" at the end of each "### Task N" section.
     Checkboxes "- [ ]" under "**Validation:**" are human verification guides — the pipeline ignores them completely.
     To mark a task as done, add "**Status:** [x] done". To mark it as pending, use "**Status:** [ ] pending". -->
# Spec 120: Ciclo de Vida de Identidade — Implementation Tasks

## Planned Scaffolds

- `src/Ciclo.Core/Entities/UserInvitation.cs`
- `tests/Ciclo.Api.Tests/InvitationLifecycleTests.cs`

## Task Order

Tasks MUST be executed sequentially in numerical order. Each task depends on the completion of all preceding tasks.

## Tasks

### Task 1: Modelar o convite e emitir token hasheado com expiração e rotação

**Objective:** Criar a entidade `UserInvitation`, mapeá-la no contexto único, gerar a migration e alterar `InviteUserAsync` para persistir apenas o hash do token, com expiração de sete dias e rotação dos convites pendentes.
**Requirements Covered:** FR-001, NFR-001
**Design References:** Component `UserInvitation`, Component `AppDbContext`, Component `AuthService.InviteUserAsync`, Component `GenerateInvitationToken / HashInvitationToken`, seção Data Structures.
**Actions:**
1. Criar `src/Ciclo.Core/Entities/UserInvitation.cs` com `Id`, `UserId`, `TenantId`, `TokenHash`, `CreatedAt`, `ExpiresAt`, `UsedAt` e a navegação `User`.
2. Registrar `public DbSet<UserInvitation> Invitations { get; set; } = null!;` em `src/Ciclo.Infrastructure/Data/AppDbContext.cs` e configurar em `OnModelCreating` a chave, `TokenHash` obrigatório com `MaxLength(64)`, índice único em `TokenHash`, índice em `UserId` e a FK com `DeleteBehavior.Cascade`.
3. Gerar a migration com `dotnet ef migrations add AddUserInvitations --project src/Ciclo.Infrastructure --startup-project src/Ciclo.Api` (a migration e o snapshot ficam no diretório de migrations do projeto de infraestrutura já existente).
4. Em `src/Ciclo.Infrastructure/Services/AuthService.cs`, adicionar `private const int InvitationLifetimeDays = 7;`, `private static string GenerateInvitationToken()` (32 bytes de `RandomNumberGenerator` em Base64Url) e `private static string HashInvitationToken(string token)` (SHA-256 em hexadecimal minúsculo).
5. Reescrever `InviteUserAsync` conforme os passos do componente: autorização, rejeição de papel inválido, normalização do e-mail, ramo de rotação (mesmo tenant com convite pendente carimba `UsedAt` e reemite), criação da conta sem senha e gravação de `UserInvitation` com `TokenHash` e `ExpiresAt`, devolvendo `(UserDto, token em claro)`.
6. Manter a assinatura de `InviteUserAsync` e o formato da resposta para não quebrar `UsersController` nem a tela administrativa.
**Expected Files:**
- `src/Ciclo.Core/Entities/UserInvitation.cs`
- `src/Ciclo.Infrastructure/Data/AppDbContext.cs`
- `src/Ciclo.Infrastructure/Services/AuthService.cs`
- `src/Ciclo.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
**Validation:** `dotnet build Ciclo.sln` sem erro; a migration criada contém a tabela `Invitations` com o índice único em `TokenHash`; um teste manual de serviço mostra uma linha em `Invitations` cujo `TokenHash` difere do token devolvido.
**Status:** [x] done

### Task 2: Aceitar o convite, definir a senha e confirmar o e-mail

**Objective:** Adicionar `AcceptInviteRequest`, `IAuthService.AcceptInviteAsync` e o endpoint anônimo `POST api/auth/accept-invite`, consumindo o convite pelo hash, definindo a primeira senha e marcando `EmailConfirmed`.
**Requirements Covered:** FR-002
**Design References:** Component `AuthService.AcceptInviteAsync`, Component `AcceptInviteRequest`, Component `AuthController.AcceptInvite`, seção Data Flow (Aceite).
**Actions:**
1. Adicionar `public sealed record AcceptInviteRequest(string Email, string Token, string NewPassword);` em `src/Ciclo.Infrastructure/Contracts/AuthDtos.cs`.
2. Declarar `Task AcceptInviteAsync(AcceptInviteRequest request);` em `IAuthService` e implementar em `AuthService` na ordem exata descrita no design: validação de entrada, resolução do usuário por e-mail com `IgnoreQueryFilters`, checagem de `IsActive`, `_tenantContext.SetTenant`, resolução do convite por `TokenHash` distinguindo `invite_used`, `invite_expired` e `invite_invalid`, `AddPasswordAsync` mapeando falha para `invalid_password`, e gravação de `UsedAt`, `EmailConfirmed = true` e carimbo dos demais convites pendentes em uma única persistência.
3. Adicionar em `src/Ciclo.Api/Controllers/AuthController.cs` a ação `AcceptInvite` com `[HttpPost("accept-invite")]`, `[AllowAnonymous]` e `[EnableRateLimiting(RateLimitPolicies.Auth)]`, retornando `NoContent()` no sucesso e `ProblemResponse.FromCode(ex.Message, ex.StatusCode)` em `AuthException`.
**Expected Files:**
- `src/Ciclo.Infrastructure/Contracts/AuthDtos.cs`
- `src/Ciclo.Infrastructure/Services/AuthService.cs`
- `src/Ciclo.Api/Controllers/AuthController.cs`
**Validation:** `dotnet build Ciclo.sln` sem erro; uma chamada de aceite com token válido retorna 204 e o login subsequente com a senha definida devolve 200.
**Status:** [x] done

### Task 3: Bloquear o login enquanto houver convite pendente

**Objective:** Alterar `AuthService.LoginAsync` para rejeitar com `invitation_pending` 403 quando existir convite pendente não expirado para o usuário, antes de qualquer tentativa de senha.
**Requirements Covered:** FR-003
**Design References:** Component `AuthService.LoginAsync`, seção Data Flow (Primeiro login).
**Actions:**
1. Em `src/Ciclo.Infrastructure/Services/AuthService.cs`, inserir a consulta de convite pendente (`UsedAt == null` e `ExpiresAt > DateTime.UtcNow`) para o `UserId` logo após a checagem de `IsActive` e antes de `CheckPasswordSignInAsync`, lançando `AuthException("invitation_pending", 403)` quando houver convite.
2. Confirmar que o caminho de bloqueio não incrementa `AccessFailedCount` nem altera `LockoutEnd`, e que `account_inactive` continua com precedência.
**Expected Files:**
- `src/Ciclo.Infrastructure/Services/AuthService.cs`
**Validation:** `dotnet build Ciclo.sln` sem erro; login de usuário com convite pendente devolve 403 com `invitation_pending` e `AccessFailedCount` permanece zero.
**Status:** [x] done

### Task 4: Revogar todas as sessões na desativação

**Objective:** Alterar `AuthService.SetActiveAsync` para revogar os refresh tokens ativos do usuário quando a desativação é persistida com sucesso.
**Requirements Covered:** FR-004
**Design References:** Component `AuthService.SetActiveAsync`, seção Data Flow (Desativação).
**Actions:**
1. Em `src/Ciclo.Infrastructure/Services/AuthService.cs`, no ramo `!isActive` e depois do `UpdateAsync` bem-sucedido, chamar `RevokeAllActiveTokensAsync(user.Id, "account_deactivated")` e persistir.
2. Manter intactas as validações existentes (`cannot_deactivate_self`, `user_not_found`, `last_admin`) e não alterar o ramo de reativação.
**Expected Files:**
- `src/Ciclo.Infrastructure/Services/AuthService.cs`
**Validation:** `dotnet build Ciclo.sln` sem erro; após desativar, a renovação com um refresh token emitido antes falha, e após reativar o usuário só obtém sessão por novo login.
**Status:** [x] done

### Task 5: Resgatar o convite pela página de senha existente

**Objective:** Expor `acceptInvite` no cliente HTTP, liberar o caminho anônimo e fazer `ForgotPasswordPage` operar em modo convite quando a URL trouxer `invite=1`.
**Requirements Covered:** FR-005
**Design References:** Component `Frontend — api/auth.ts e api/client.ts`, Component `Frontend — ForgotPasswordPage no modo convite`, seção Data Flow (Aceite).
**Actions:**
1. Em `frontend/src/api/auth.ts`, adicionar `export async function acceptInvite(email: string, token: string, newPassword: string): Promise<void>` postando em `/api/auth/accept-invite`.
2. Em `frontend/src/api/client.ts`, incluir `'/api/auth/accept-invite'` no array `anonymousAuthEndpoints`.
3. Em `frontend/src/pages/ForgotPasswordPage.tsx`, derivar `inviteMode` de `new URLSearchParams(window.location.search).get('invite') === '1'`, inicializar `email` e `resetToken` pelos parâmetros `email` e `token` da URL, iniciar `step` em `'reset'` no modo convite e chamar `acceptInvite` em vez de `resetPassword` em `handleResetSubmit`, navegando para `/login` no sucesso.
**Expected Files:**
- `frontend/src/api/auth.ts`
- `frontend/src/api/client.ts`
- `frontend/src/pages/ForgotPasswordPage.tsx`
**Validation:** `npm run build` e `npm run lint` no diretório `frontend` sem erro; abrir a página com `?invite=1&email=...&token=...` mostra o passo de senha e aceita o convite.
**Status:** [x] done

### Task 6: Cobrir o ciclo de convite e a revogação com testes

**Objective:** Criar a suíte `InvitationLifecycleTests` provando os cenários do design e ajustar os testes de convite existentes em `AuthServiceTests`.
**Requirements Covered:** FR-001, FR-002, FR-003, FR-004, NFR-001, NFR-002
**Design References:** seção Testing Strategy, tabela de Data Structures, seção Migration / Rollback.
**Actions:**
1. Criar `tests/Ciclo.Api.Tests/InvitationLifecycleTests.cs` no molde de `AuthServiceTests` (InMemory, `AddIdentityCore<User>` com roles, `ITenantContext` e `IJwtTokenGenerator` dublados, `NoopAuditTrail`, tenant e roles de `TestConstants`) e cobrir: hash e expiração na emissão, rotação no reconvite, aceite com senha e confirmação de e-mail, `invite_invalid`, `invite_expired`, `invite_used`, `account_inactive`, `invalid_password` sem alterar estado, `invitation_pending` antes do aceite e login liberado depois, e revogação de sessão na desativação com falha de renovação do token antigo.
2. Ajustar em `tests/Ciclo.Api.Tests/AuthServiceTests.cs` os testes de convite para o convite persistido, mantendo sem alteração as expectativas dos fluxos não tocados.
**Expected Files:**
- `tests/Ciclo.Api.Tests/InvitationLifecycleTests.cs`
- `tests/Ciclo.Api.Tests/AuthServiceTests.cs`
**Validation:** `dotnet test tests/Ciclo.Api.Tests` sem falha e a suíte nova cobrindo cada código de erro do ciclo.
**Status:** [x] done
