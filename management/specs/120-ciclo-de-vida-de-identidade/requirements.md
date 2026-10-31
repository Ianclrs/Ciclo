---
name: ciclo-de-vida-de-identidade
status: verified
determinism: D
references: [V12]
---

# Spec 120: Ciclo de Vida de Identidade — Requirements

## Determinism Classification

**Class:** D — Deterministic

Todo entregável é uma transição de estado sobre entidades persistidas (`UserInvitation`, `User`, `RefreshToken`), observável por asserção determinística em xUnit com `EntityFrameworkCore.InMemory`. Nenhuma saída de modelo participa da decisão: o resultado é verificado por código de status HTTP, pelo código de erro (`invite_expired`, `invite_used`, `invitation_pending`) e pelo estado relido do banco. A mesma entrada produz sempre o mesmo veredito.

## Value Delivery

**V12: Ciclo de Vida de Identidade** — Gestão completa de usuários por escola: convite de Admin/Staff, verificação de e-mail antes do primeiro login, troca de papel, desativação e reativação, revogação de sessões e poda de refresh tokens expirados. Hoje apenas o cadastro da escola cria um Admin e o e-mail nunca é verificado; com este valor a escola consegue operar com equipe real sem intervenção manual no banco.

**Canonical creation path:** `management/specs/120-ciclo-de-vida-de-identidade/` — convite de staff por token de uso único com hash e expiração; verificação de e-mail obrigatória antes do primeiro login; troca de papel e desativação/reativação restritas a Admin; revogação de todas as sessões ao desativar; poda de refresh tokens expirados; endpoints e tela de gestão de usuários; testes de permissão e ciclo de convite.

**Delta entregue por esta spec.** O plano de correções já entregou a gestão de usuários (M11: endpoints de convidar, trocar papel, ativar/desativar e a tela administrativa), a detecção de reuso no refresh (B3) e a poda de tokens (B4). O que permanece do valor V12, e é o escopo integral desta spec, é o ciclo de identidade propriamente dito: convite nominal com token hasheado de uso único e com expiração, verificação de e-mail bloqueante no primeiro login e revogação de todas as sessões na desativação. O ciclo fecha o caminho ponta a ponta do convite até o primeiro login autenticado, sem edição manual no banco.

## Functional Requirements

### FR-001: Convite nominal com token de uso único, hasheado e com expiração

**What:** Cria `src/Ciclo.Core/Entities/UserInvitation.cs` e altera `AuthService.InviteUserAsync` em `src/Ciclo.Infrastructure/Services/AuthService.cs` para persistir o convite como entidade própria, guardando apenas o hash SHA-256 do token, a expiração e o instante de consumo, e devolvendo o token em claro uma única vez na resposta.
**Why:** V12 exige convite por token de uso único com hash e expiração. Sem expiração e sem hash, o token de convite é um segredo permanente em claro e um convite antigo continua válido indefinidamente.
**Acceptance Criteria:**
- [ ] A entidade `UserInvitation` existe com `UserId`, `TenantId`, `TokenHash`, `CreatedAt`, `ExpiresAt` e `UsedAt`. [verify: file-exists `src/Ciclo.Core/Entities/UserInvitation.cs`]
- [ ] `AuthService.InviteUserAsync` persiste o convite por `TokenHash` e `ExpiresAt`, sem gravar o token em claro. [verify: grep-present `TokenHash` `src/Ciclo.Core/Entities/UserInvitation.cs`]
- [ ] Um novo convite para o mesmo usuário invalida os convites anteriores não consumidos, provado por asserção sobre o estado persistido. [verify: project-validation]
**Failure modes:**
- Papel fora de `Admin` ou `Staff` continua rejeitado com `invalid_role` 400, como hoje.
- E-mail já pertencente a outro tenant continua rejeitado por `EmailUniqueness.EnsureAvailableAsync`.
- Falha de persistência na criação do usuário aborta antes de criar o convite; nenhum convite órfão é gravado.
- A resposta mantém o formato atual (`UserDto` mais token), portanto a tela administrativa e o DTO `InviteUserRequest` seguem funcionando sem alteração.

**Detalhes de implementação (obrigatórios):**
- Campos da entidade: `Id` (Guid, chave), `UserId` (Guid, FK para `User`, cascade delete), `TenantId` (Guid), `TokenHash` (string de 64 caracteres hexadecimais), `CreatedAt` (DateTime UTC), `ExpiresAt` (DateTime UTC), `UsedAt` (DateTime? UTC). `DbSet<UserInvitation> Invitations` em `src/Ciclo.Infrastructure/Data/AppDbContext.cs`, com índice único em `TokenHash` e índice em `UserId`.
- Geração do token: 32 bytes de `RandomNumberGenerator` em Base64Url (43 caracteres). Hash: SHA-256 sobre os bytes UTF-8 do token, em hexadecimal minúsculo.
- Validade: constante `InvitationLifetimeDays = 7` em `AuthService`, no mesmo padrão das constantes de sessão existentes.
- Rotação: ao convidar um e-mail que já pertence a um usuário do mesmo tenant com convite pendente, os convites pendentes daquele usuário recebem `UsedAt` preenchido e um novo convite é emitido; o token antigo deixa de ser aceito.
- O token em claro é devolvido apenas no retorno de `InviteUserAsync`; a coluna `TokenHash` é a única representação persistida.

### FR-002: Aceite do convite define a senha e confirma o e-mail

**What:** Adiciona `AuthService.AcceptInviteAsync(AcceptInviteRequest)` em `src/Ciclo.Infrastructure/Services/AuthService.cs`, o record `AcceptInviteRequest` em `src/Ciclo.Infrastructure/Contracts/AuthDtos.cs` e o endpoint anônimo `POST api/auth/accept-invite` em `src/Ciclo.Api/Controllers/AuthController.cs`; o aceite define a senha da conta sem senha, marca `EmailConfirmed = true` e consome o convite.
**Why:** A conta criada por convite nasce sem senha, e aceitar o convite é a prova de posse da caixa de e-mail. Sem este passo o convite é um registro sem efeito e a conta permanece inutilizável.
**Acceptance Criteria:**
- [ ] `AuthService.AcceptInviteAsync` existe e consome o convite pelo hash do token. [verify: function-exists `AcceptInviteAsync` `src/Ciclo.Infrastructure/Services/AuthService.cs`]
- [ ] O endpoint anônimo `POST api/auth/accept-invite` está publicado em `AuthController`. [verify: grep-present `accept-invite` `src/Ciclo.Api/Controllers/AuthController.cs`]
- [ ] O aceite marca `UsedAt` e `EmailConfirmed` na mesma operação e define a senha, provado por asserção de estado persistido. [verify: project-validation]
**Failure modes:**
- Token em branco, e-mail inexistente ou hash sem convite correspondente retornam 400 `invite_invalid`, sem revelar se o e-mail existe.
- Convite com `ExpiresAt` no passado retorna 400 `invite_expired`.
- Convite já consumido (`UsedAt` preenchido) retorna 400 `invite_used`.
- Usuário com `IsActive` falso retorna 403 `account_inactive` e o convite permanece não consumido.
- Senha que viola a política do Identity retorna 400 `invalid_password` com as descrições agregadas de `IdentityResult`; nenhum estado é alterado.
- Duas requisições concorrentes com o mesmo token: a primeira consome e grava; a segunda encontra `UsedAt` preenchido e retorna `invite_used`; a senha nunca é sobrescrita por credencial divergente depois do primeiro sucesso.

**Detalhes de implementação (obrigatórios):**
- `AcceptInviteRequest(string Email, string Token, string NewPassword)` em `src/Ciclo.Infrastructure/Contracts/AuthDtos.cs`.
- Resolução do usuário por e-mail com `IgnoreQueryFilters()`, mesmo padrão de `ConfirmEmailAsync`; `_tenantContext.SetTenant(user.TenantId)` antes de qualquer escrita.
- O convite é buscado por `UserId` e `TokenHash`, com `UsedAt == null` e `ExpiresAt > DateTime.UtcNow`.
- A senha é definida por `_userManager.AddPasswordAsync` (a conta de convite não possui `PasswordHash`); falha mapeia para `invalid_password` 400.
- Em sucesso: `UsedAt` recebe o instante UTC, `EmailConfirmed` recebe `true`, os demais convites pendentes do mesmo usuário recebem `UsedAt`, e uma única chamada de persistência grava o conjunto.
- O endpoint retorna 204 sem corpo. O atributo é `[AllowAnonymous]` com a política de rate limit `auth`, seguindo os endpoints anônimos vizinhos.

### FR-003: Convite pendente bloqueia a autenticação

**What:** Altera `AuthService.LoginAsync` para rejeitar com `invitation_pending` 403 quando o usuário possui convite pendente não expirado, antes de registrar tentativa de senha.
**Why:** V12 exige verificação de e-mail antes do primeiro login. O convite pendente é a evidência determinística de que a posse da caixa ainda não foi provada por um token de uso único.
**Acceptance Criteria:**
- [ ] `LoginAsync` rejeita o login de usuário com convite pendente usando o código `invitation_pending`. [verify: grep-present `invitation_pending` `src/Ciclo.Infrastructure/Services/AuthService.cs`]
- [ ] O login do mesmo usuário ingressa normalmente depois do aceite do convite, provado por teste. [verify: project-validation]
**Failure modes:**
- Usuário inativo mantém a precedência existente: `account_inactive` 403, mesmo com convite pendente.
- A tentativa bloqueada não incrementa `AccessFailedCount` e não aproxima a conta do bloqueio por lockout.
- Convite expirado não bloqueia por si só: a conta continua sem senha, portanto segue sem autenticar, e o caminho de saída é um novo convite.
- Credenciais inválidas de usuário sem convite pendente preservam `invalid_credentials` 401.

**Detalhes de implementação (obrigatórios):**
- A verificação ocorre depois da checagem de `IsActive` e antes da verificação de senha, sobre o convite com `UsedAt == null` e `ExpiresAt > DateTime.UtcNow`.
- O código `invitation_pending` acompanha status 403, no mesmo formato de `AuthException` usado pelos demais códigos de autenticação.

### FR-004: Desativação revoga todas as sessões

**What:** Altera `AuthService.SetActiveAsync` para chamar `RevokeAllActiveTokensAsync(user.Id, "account_deactivated")` quando `isActive` é `false`, depois de persistir a alteração no usuário.
**Why:** V12 exige revogação de todas as sessões ao desativar. Sem isso, o desativado continua renovando sessão por até 30 dias com um refresh token já emitido, e a desativação não produz efeito prático.
**Acceptance Criteria:**
- [ ] `SetActiveAsync` revoga os tokens ativos do usuário quando `isActive` é `false`, usando o motivo `account_deactivated`. [verify: grep-present `account_deactivated` `src/Ciclo.Infrastructure/Services/AuthService.cs`]
- [ ] Um refresh token emitido antes da desativação falha na renovação e um refresh token novo só é obtido por login após a reativação, provado por teste. [verify: project-validation]
**Failure modes:**
- Falha ao persistir a desativação impede a revogação; nenhum token é revogado sem a alteração efetiva no usuário.
- Reativação não restaura tokens revogados: o usuário reativa e autentica por novo login.
- Desativar a si mesmo continua rejeitado com `cannot_deactivate_self` 400.
- Desativar o último Admin ativo continua rejeitado com `last_admin` 400, sem revogação parcial.

**Detalhes de implementação (obrigatórios):**
- A revogação reutiliza `RevokeAllActiveTokensAsync`, que já existe em `AuthService`; nenhum caminho de revogação paralelo é criado.
- A revogação ocorre somente no ramo `!isActive`; a reativação não toca em tokens.

### FR-005: O convite é resgatável pela página de senha existente

**What:** Adiciona `acceptInvite(email, token, newPassword)` em `frontend/src/api/auth.ts`, inclui `/api/auth/accept-invite` em `anonymousAuthEndpoints` de `frontend/src/api/client.ts` e faz `frontend/src/pages/ForgotPasswordPage.tsx` consumir `acceptInvite` quando a URL carrega o parâmetro `invite=1`, redirecionando para o login no sucesso.
**Why:** O token em claro só é exibido uma vez ao Admin, e o convidado precisa de um caminho de resgate. A página de definição de senha já existe e já recebe e-mail e token pela URL; reaproveitá-la evita uma rota nova e mantém o fluxo do convite fechado ponta a ponta.
**Acceptance Criteria:**
- [ ] `frontend/src/api/auth.ts` exporta `acceptInvite`, que envia o token para `/api/auth/accept-invite`. [verify: grep-present `acceptInvite` `frontend/src/api/auth.ts`]
- [ ] O caminho `/api/auth/accept-invite` está declarado entre os endpoints anônimos do cliente HTTP. [verify: grep-present `/api/auth/accept-invite` `frontend/src/api/client.ts`]
- [ ] A página de senha chama `acceptInvite` no modo convite e a compilação do frontend passa. [verify: project-validation]
**Failure modes:**
- Sem o parâmetro `invite=1`, a página mantém exatamente o comportamento atual de recuperação de senha.
- Erro do endpoint (400 ou 403) exibe a mensagem do envelope de erro já usado pela página e mantém o formulário preenchido.
- Token ausente na URL mantém o campo editável para colagem manual, como hoje.

**Detalhes de implementação (obrigatórios):**
- Assinatura: `export async function acceptInvite(email: string, token: string, newPassword: string): Promise<void>`, com `await api.post('/api/auth/accept-invite', { email, token, newPassword })`.
- O modo convite é derivado de `new URLSearchParams(window.location.search).get('invite') === '1'`; nenhuma rota nova é registrada no roteador.
- No sucesso do modo convite, o redirecionamento vai para a rota de login já usada pela página de recuperação.

## Non-Functional Requirements

### NFR-001: O token de convite nunca é persistido em claro

**What:** O valor devolvido ao Admin existe apenas em memória e na resposta HTTP; a única representação persistida é o hash SHA-256 em `UserInvitation.TokenHash`.
**Acceptance Criteria:**
- [ ] O hash do token é produzido por função dedicada no serviço de identidade. [verify: function-exists `HashInvitationToken` `src/Ciclo.Infrastructure/Services/AuthService.cs`]
- [ ] O teste de ciclo de convite confirma que o valor gravado difere do token devolvido. [verify: project-validation]

### NFR-002: Nenhum fluxo existente de autenticação regride

**What:** Registro de escola, registro de responsável, login, refresh, revogação, recuperação de senha e poda de tokens mantêm comportamento e códigos de erro atuais; a suíte existente continua passando sem alteração de expectativa.
**Acceptance Criteria:**
- [ ] A poda de tokens continua presente e acionada na emissão de sessão. [verify: grep-present `PruneStaleTokensAsync` `src/Ciclo.Infrastructure/Services/AuthService.cs`]
- [ ] A suíte existente do projeto passa integralmente com as alterações aplicadas. [verify: project-validation]

## Constraints

- A autorização de gestão continua no dono único `AccessPolicy`, sem verificação de papel local nos novos caminhos. [verify: grep-present `UserManage` `src/Ciclo.Infrastructure/Security/AccessPolicy.cs`]
- A revogação reutiliza o método existente do serviço em vez de duplicar a lógica. [verify: function-exists `RevokeAllActiveTokensAsync` `src/Ciclo.Infrastructure/Services/AuthService.cs`]
- O contrato do pedido de convite atual é preservado, sem renomear campos consumidos pela tela administrativa. [verify: grep-present `InviteUserRequest` `src/Ciclo.Infrastructure/Contracts/AuthDtos.cs`]
- O modelo novo é registrado no contexto de dados existente, que permanece o único dono do mapeamento. [verify: grep-present `DbSet<RefreshToken>` `src/Ciclo.Infrastructure/Data/AppDbContext.cs`]
- Os testes usam o projeto xUnit existente, com banco InMemory e os dublês já disponíveis. [verify: file-exists `tests/Ciclo.Api.Tests/AuthServiceTests.cs`]
- Toda a validação do projeto passa ao final. [verify: project-validation]

## Edge Cases & Error States

- Token em branco, e-mail em branco ou combinação sem convite: 400 `invite_invalid`, sem distinguir e-mail inexistente de token inválido.
- Convite expirado: 400 `invite_expired`; o caminho de recuperação é um novo convite emitido pelo Admin, nunca edição manual no banco.
- Convite já consumido, inclusive por requisição concorrente: 400 `invite_used`.
- Usuário desativado com convite pendente: 403 `account_inactive`; o convite não é consumido e, após a reativação, o mesmo token segue aceito até expirar.
- Senha abaixo da política do Identity: 400 `invalid_password`, com o texto das regras violadas, sem alterar `PasswordHash`, `EmailConfirmed` ou `UsedAt`.
- Reconvite de um usuário com convite pendente: o token antigo retorna `invite_used` a partir da rotação e o token novo é aceito.
- E-mail com caixa diferente da cadastrada: a comparação usa a forma minúscula já normalizada no cadastro, então o aceite funciona com qualquer caixa.
- Relógio: todas as comparações de expiração usam `DateTime.UtcNow`, sem hora local.
- Usuário sem convite pendente e sem senha (registro antigo interrompido): o login segue `invalid_credentials` e o Admin resolve emitindo um convite.
- Desativação de usuário sem nenhum token ativo: a revogação é no-op e a resposta de sucesso não muda.

## Anti-Scope

- Nenhum controller novo é criado para convites; os endpoints vivem no controller de autenticação existente. [verify: file-absent `src/Ciclo.Api/Controllers/InvitationsController.cs`]
- Nenhum serviço em segundo plano é adicionado para expirar convites; a expiração é avaliada na leitura. [verify: grep-absent `IHostedService` `src/Ciclo.Infrastructure/Services/AuthService.cs`]
- O armazenamento de refresh token não é alterado por esta spec. [verify: grep-absent `TokenHash` `src/Ciclo.Core/Entities/RefreshToken.cs`]
- Nenhum provedor de e-mail é integrado ao fluxo de convite. [verify: grep-absent `SmtpClient` `src/Ciclo.Infrastructure/Services/AuthService.cs`]

## Dependencies

- Spec 30 (autenticação) — `AuthService`, `JwtTokenGenerator`, `AccessPolicy`, `AppDbContext`, `RevokeAllActiveTokensAsync` e a tela administrativa já existentes; esta spec estende esses donos em vez de criar paralelos.
- Spec 110 (auditoria de operações) não é pré-requisito: nenhum evento auditável é exigido pela V12.
- Entrega real de e-mail fica na spec 140; aqui o token continua sendo devolvido ao Admin na resposta, como hoje.

## Out of Scope

- Revogação de sessões na troca de papel — a V12 nomeia revogação apenas na desativação.
- Hash do refresh token (o armazenamento atual em claro permanece).
- Verificação de e-mail bloqueante para contas de auto-registro — a decisão registrada mantém o convite como gate.
- Provedor real de e-mail, templates de notificação e reenvio automático.
- Revogação de sessão por alteração de senha fora do fluxo de convite.
- Política de complexidade de senha além da já configurada no Identity.

## Test Scope

- `tests/Ciclo.Api.Tests/InvitationLifecycleTests.cs` — suíte nova que prova FR-001 a FR-004: criação com hash e expiração, rotação no reconvite, aceite com senha e confirmação de e-mail, códigos `invite_invalid`, `invite_expired`, `invite_used` e `account_inactive`, bloqueio `invitation_pending` antes do aceite e login liberado depois, revogação de sessão na desativação e falha de renovação do token revogado.
- `tests/Ciclo.Api.Tests/AuthServiceTests.cs` — suíte existente ajustada para o novo ciclo de convite, mantendo as asserções dos fluxos não tocados.
