---
name: configuracao-e-limites-por-tenant
status: verified
determinism: D
references: [V20]
---

# Spec 200: configuracao-e-limites-por-tenant — Requirements

## Determinism Classification

**Class:** D — Deterministic

A configuração é um conjunto finito de campos tipados, validados por regras fechadas (formato hexadecimal, faixa numérica, lista de locales, lista de flags conhecidas) e persistidos em colunas da tabela existente. Cada entrada produz uma saída observável única por HTTP (código de status, corpo JSON, código de erro) e o efeito no banco é verificável por consulta direta. Não há conteúdo gerado por modelo, nem alteração de fluxo do pipeline SDD. A única dimensão temporal (expiração do cache) é declarada como constante de 5 minutos e o comportamento testado é a invalidação imediata após escrita, que é determinística.

## Value Delivery

V20: Configuração e Limites por Escola — Cada tenant passa a ter configuração própria: identidade visual (nome de exibição, cores, logotipo), fuso horário, ano letivo padrão, cota de armazenamento e de usuários, e flags de funcionalidades por plano. Permite operar várias escolas com políticas distintas sem código específico.

**Delivered by:** Spec 200 (configuracao-e-limites-por-tenant)

## Functional Requirements

### FR-001: Configuração por tenant na entidade existente

**What:** A entidade `Tenant` em `src/Ciclo.Core/Entities/Tenant.cs` passa a declarar os campos de configuração da escola: `DisplayName` (texto, 200), `LogoUrl` (texto, 500), `PrimaryColor` e `SecondaryColor` (texto, 7, hexadecimal), `TimeZone` (texto, 100, padrão `"UTC"`), `DefaultSchoolYear` (inteiro, padrão 2025), `Locale` (texto, 5, padrão `"pt-BR"`), `Plan` (enum `TenantPlan`), `FeatureFlags` (texto, 500, lista separada por vírgula, padrão vazio) e `UserQuota` (inteiro, padrão 50). Criam-se `src/Ciclo.Core/Entities/TenantPlan.cs` com o enum `TenantPlan` (`Basic`, `Standard`, `Premium`) e `src/Ciclo.Core/Entities/TenantFeature.cs` com a classe estática `TenantFeature` (constantes `Documents`, `Notifications`, `ParentPortal`, `AcademicPerformance`, `BatchReenrollment`, `AuditExport`; método `DefaultFor(TenantPlan plan)` que devolve o conjunto padrão ordenado; método `Normalize(string? csv)` que devolve a lista ordenada, sem duplicatas e validada contra as constantes conhecidas; método `ToCsv` que grava a lista normalizada). Nenhuma tabela nova é criada: as colunas entram em `Tenants` por migração.
**Why:** É a base do V20 — sem os campos persistidos nenhuma política distinta por escola pode existir.
**Acceptance Criteria:**
- [ ] `Tenant` declara `DisplayName`, `LogoUrl`, `PrimaryColor` e `SecondaryColor` com limites de tamanho. [verify: grep-present `PrimaryColor` `src/Ciclo.Core/Entities/Tenant.cs`]
- [ ] `Tenant` declara `TimeZone`, `Locale` e `DefaultSchoolYear` com valor padrão atribuído. [verify: grep-present `DefaultSchoolYear` `src/Ciclo.Core/Entities/Tenant.cs`]
- [ ] `Tenant` declara `Plan`, `FeatureFlags` e `UserQuota`. [verify: grep-present `FeatureFlags` `src/Ciclo.Core/Entities/Tenant.cs`]
- [ ] O enum `TenantPlan` existe com os valores `Basic`, `Standard` e `Premium`. [verify: file-exists `src/Ciclo.Core/Entities/TenantPlan.cs`]
- [ ] `TenantFeature` declara as flags suportadas e o conjunto padrão por plano. [verify: grep-present `TenantFeature` `src/Ciclo.Core/Entities/TenantFeature.cs`]
- [ ] `Tenant.StorageQuotaBytes` permanece na entidade como fonte única da cota de armazenamento. [verify: grep-present `StorageQuotaBytes` `src/Ciclo.Core/Entities/Tenant.cs`]
- [ ] A migração que adiciona as colunas novas existe e atualiza o snapshot do modelo. [verify: grep-present `UserQuota` `src/Ciclo.Infrastructure/Migrations/`]
**Failure modes:**
- `FeatureFlags` com valor fora do conjunto conhecido é rejeitado por `TenantFeature.Normalize`, que lança `ArgumentException` com a lista de flags inválidas.
- `Tenant` lido com `FeatureFlags` nulo ou vazio produz lista de flags vazia, nunca exceção.
- As colunas novas recebem valor padrão declarado na migração, de modo que linhas já existentes em `Tenants` continuam válidas após a alteração da tabela.

### FR-002: Leitura da configuração efetiva pela API

**What:** `TenantsController` em `src/Ciclo.Api/Controllers/TenantsController.cs` ganha `[HttpGet("me/configuration")]` com `[Authorize]`, que delega a `ITenantService.GetConfigurationAsync(Guid tenantId)`. Essa operação, em `src/Ciclo.Infrastructure/Services/TenantService.cs`, lê `Tenants` filtrando por `Id == tenantId` e projeta o record `TenantConfigurationDto` (definido em `src/Ciclo.Infrastructure/Contracts/TenantDtos.cs`) com os campos `TenantId`, `DisplayName`, `LogoUrl`, `PrimaryColor`, `SecondaryColor`, `TimeZone`, `DefaultSchoolYear`, `Locale`, `StorageQuotaBytes`, `UserQuota`, `Plan` (texto: `Basic`, `Standard` ou `Premium`) e `FeatureFlags` (lista de textos ordenada e sem duplicatas). `DisplayName` na resposta nunca é vazio: quando `Tenant.DisplayName` é nulo ou em branco, devolve `Tenant.Name`.
**Why:** Entrega a leitura da configuração da escola do usuário logado, primeiro termo do V20 (cada tenant passa a ter configuração própria).
**Acceptance Criteria:**
- [ ] `TenantsController` declara a rota `me/configuration` para `HttpGet`. [verify: grep-present `me/configuration` `src/Ciclo.Api/Controllers/TenantsController.cs`]
- [ ] `TenantConfigurationDto` existe com os campos de identidade visual, fuso, ano letivo, locale, cotas, plano e flags. [verify: grep-present `TenantConfigurationDto` `src/Ciclo.Infrastructure/Contracts/TenantDtos.cs`]
- [ ] `ITenantService.GetConfigurationAsync` existe e devolve `TenantConfigurationDto`. [verify: function-exists `GetConfigurationAsync` `src/Ciclo.Infrastructure/Services/TenantService.cs`]
- [ ] A resposta traz `DisplayName` preenchido com `Tenant.Name` quando o nome de exibição não está definido. [verify: project-validation]
- [ ] A resposta traz `FeatureFlags` ordenado e sem duplicatas. [verify: project-validation]
- [ ] Requisição sem token válido recebe 401. [verify: project-validation]
**Failure modes:**
- Tenant inexistente devolve 404 com código `tenant_not_found` pelo mesmo canal `ProblemResponse.FromCode` usado pelas demais ações do controller.
- Tenant inativo devolve 404 com código `tenant_not_found`, sem expor dados de configuração.
- Claim `tenant_id` ausente ou não parseável devolve 401 com código `tenant_not_resolved`, reutilizando `GetTenantId` do controller.

### FR-003: Atualização da configuração pela API

**What:** `TenantsController` ganha `[HttpPut("me/configuration")]` com `[Authorize(Roles = "Admin")]`, recebendo `UpdateTenantConfigurationRequest` (todos os campos anuláveis) e delegando a `ITenantService.UpdateConfigurationAsync(Guid tenantId, UpdateTenantConfigurationRequest request)`, que devolve o `TenantConfigurationDto` atualizado. Campo nulo em `UpdateTenantConfigurationRequest` preserva o valor atual. `DisplayName`, `LogoUrl` e cores em branco são gravados como nulo. Cores são normalizadas para maiúsculas. `Plan` é aceito como texto e convertido para `TenantPlan`. Quando `FeatureFlags` é omitido, as flags são regravadas a partir de `TenantFeature.DefaultFor` do plano informado. Quando `FeatureFlags` é informado, exatamente esse conjunto é gravado após `TenantFeature.Normalize`.
**Why:** Entrega a edição da configuração por escola, permitindo políticas distintas sem código específico, conforme o V20.
**Acceptance Criteria:**
- [ ] `TenantsController` declara a rota `me/configuration` para `HttpPut`. [verify: grep-present `HttpPut` `src/Ciclo.Api/Controllers/TenantsController.cs`]
- [ ] `ITenantService.UpdateConfigurationAsync` existe e devolve `TenantConfigurationDto`. [verify: function-exists `UpdateConfigurationAsync` `src/Ciclo.Infrastructure/Services/TenantService.cs`]
- [ ] A atualização exige o papel Admin e devolve 403 para os demais papéis. [verify: project-validation]
- [ ] Campo nulo na requisição preserva o valor atual do tenant. [verify: project-validation]
- [ ] Cor fora do formato hexadecimal de seis dígitos devolve 400 com código `invalid_color`. [verify: project-validation]
- [ ] Fuso desconhecido devolve 400 com código `invalid_time_zone`. [verify: project-validation]
- [ ] `DefaultSchoolYear` fora de 2000 a 2100 devolve 400 com código `invalid_school_year`. [verify: project-validation]
- [ ] `Locale` fora de `pt-BR`, `en-US` e `es-ES` devolve 400 com código `invalid_locale`. [verify: project-validation]
- [ ] `UserQuota` fora de 1 a 10000 devolve 400 com código `invalid_user_quota`. [verify: project-validation]
- [ ] `StorageQuotaBytes` menor ou igual a zero devolve 400 com código `invalid_storage_quota`. [verify: project-validation]
- [ ] Flag fora do conjunto conhecido devolve 400 com código `invalid_feature_flag`. [verify: project-validation]
- [ ] `Plan` fora de `Basic`, `Standard` e `Premium` devolve 400 com código `invalid_plan`. [verify: project-validation]
- [ ] Omitir `FeatureFlags` regrava as flags a partir do conjunto padrão do plano informado. [verify: project-validation]
**Failure modes:**
- Requisição sem nenhum campo preenchido não altera nada e devolve 200 com o estado atual.
- Falha de persistência não invalida o cache e devolve o erro pelo middleware de exceções existente.
- Tenant inexistente devolve 404 com código `tenant_not_found`.

### FR-004: Cota de usuários aplicada no serviço dono

**What:** `ITenantService` ganha `Task EnsureUserQuotaAsync(Guid tenantId)`, implementado em `TenantService`, que conta os usuários do tenant (registros de `Users` com o mesmo `TenantId`) e lança `AuthException("user_quota_exceeded", 409)` quando a contagem é maior ou igual a `Tenant.UserQuota`. A contagem lê o valor corrente de `Tenant.UserQuota` no banco, sem cache, para que uma redução de cota passe a valer na criação seguinte. O serviço dono da criação de usuário em `src/Ciclo.Infrastructure/Services/` chama `EnsureUserQuotaAsync` imediatamente antes de persistir o novo usuário. A cota é por tenant: a contagem é restrita ao `TenantId` solicitado.
**Why:** Entrega a cota de usuários do V20 aplicada onde ela é decidida, sem duplicar a regra em cada chamador.
**Acceptance Criteria:**
- [ ] `ITenantService.EnsureUserQuotaAsync` existe em `TenantService`. [verify: function-exists `EnsureUserQuotaAsync` `src/Ciclo.Infrastructure/Services/TenantService.cs`]
- [ ] O caminho de criação de usuário chama `EnsureUserQuotaAsync` antes de persistir. [verify: grep-present `EnsureUserQuotaAsync` `src/Ciclo.Infrastructure/Services/AuthService.cs`]
- [ ] Criar usuário com a cota já atingida devolve 409 com código `user_quota_exceeded`. [verify: project-validation]
- [ ] Exatamente `UserQuota` usuários são permitidos; o usuário seguinte é recusado. [verify: project-validation]
- [ ] Usuários de outro tenant não contam para a cota do tenant solicitado. [verify: project-validation]
**Failure modes:**
- Tenant inexistente lança `AuthException` com código `tenant_not_found` e status 404 antes de qualquer contagem.
- `UserQuota` igual a zero, vindo de dado legado, recusa toda criação com `user_quota_exceeded`.
- A verificação ocorre antes da escrita no mesmo fluxo do chamador; o erro não deixa usuário parcialmente criado.

### FR-005: Cache da configuração com invalidação na escrita

**What:** `TenantService` recebe `IMemoryCache` por injeção de construtor. `GetConfigurationAsync` lê primeiro a chave `"tenant-config:"` concatenada com o identificador do tenant e, em ausência, consulta o banco e grava o `TenantConfigurationDto` com expiração absoluta de 5 minutos. `UpdateConfigurationAsync` remove a chave do tenant atualizado imediatamente após `SaveChangesAsync` concluir com sucesso, antes de responder. `src/Ciclo.Api/Program.cs` registra o serviço de cache em memória pelo método de extensão `AddMemoryCache`, no mesmo ponto de composição onde `ITenantService` é registrado.
**Why:** Entrega o cache com invalidação do V20 sem introduzir abstração de cache própria.
**Acceptance Criteria:**
- [ ] `TenantService` declara a dependência `IMemoryCache` e usa a chave com o prefixo `tenant-config:`. [verify: grep-present `IMemoryCache` `src/Ciclo.Infrastructure/Services/TenantService.cs`]
- [ ] `Program.cs` registra o cache em memória com `AddMemoryCache`. [verify: grep-present `AddMemoryCache` `src/Ciclo.Api/Program.cs`]
- [ ] A escrita remove a entrada de cache do tenant atualizado. [verify: project-validation]
- [ ] A leitura imediatamente após a escrita devolve os valores novos. [verify: project-validation]
- [ ] A entrada de cache de um tenant não é servida para outro tenant. [verify: project-validation]
- [ ] A entrada expira por tempo absoluto de 5 minutos. [verify: project-validation]
**Failure modes:**
- Cache sem a entrada cai na consulta ao banco, sem erro para o chamador.
- Falha em `SaveChangesAsync` não remove a entrada de cache, mantendo o último estado persistido.
- Alteração de `Tenant.UserQuota` fora deste serviço só aparece na leitura após a expiração de 5 minutos; a cota de usuários não é afetada porque FR-004 lê o banco.

### FR-006: Tela administrativa de configuração no frontend

**What:** O módulo `frontend/src/api/tenants.ts` (dono atual das chamadas de tenant) ganha `getTenantConfiguration()` e `updateTenantConfiguration(payload)`, que chamam `GET` e `PUT` em `api/tenants/me/configuration` pelo cliente HTTP existente, além dos tipos `TenantConfiguration` e `UpdateTenantConfiguration`. A página `frontend/src/pages/admin/TenantSettings.tsx` renderiza o formulário com nome de exibição, logotipo, cor primária, cor secundária, fuso, ano letivo padrão, locale, cota de armazenamento, cota de usuários, plano e flags, carrega pela leitura ao montar e envia pela escrita ao salvar; a rota é registrada em `frontend/src/App.tsx` sob a proteção de papel existente, acessível apenas ao Admin.
**Why:** Entrega a tela administrativa do V20, sem a qual a configuração só seria editável por chamada direta à API.
**Acceptance Criteria:**
- [ ] A página administrativa existe em `frontend/src/pages/admin/TenantSettings.tsx`. [verify: file-exists `frontend/src/pages/admin/TenantSettings.tsx`]
- [ ] O módulo de API de tenant chama `me/configuration` na leitura e na escrita. [verify: grep-present `me/configuration` `frontend/src/api/tenants.ts`]
- [ ] A rota da página é registrada em `frontend/src/App.tsx` sob a guarda de papel. [verify: grep-present `TenantSettings` `frontend/src/App.tsx`]
- [ ] A tela exibe o código de erro devolvido pela API em cada recusa 400. [verify: project-validation]
- [ ] A tela recusa acesso de quem não é Admin, mantendo o redirecionamento já usado pelas demais páginas administrativas. [verify: project-validation]
**Failure modes:**
- Falha na carga inicial exibe o componente de erro de carregamento existente e não habilita o formulário.
- Recusa 403 na escrita mantém os valores carregados na tela e exibe o código de erro.
- Envio sem alteração não chama a API, evitando escrita vazia.

## Non-Functional Requirements

### NFR-001: Isolamento da configuração entre tenants

**What:** Toda leitura e toda escrita de configuração são filtradas pelo identificador do tenant da sessão. Uma configuração nunca é servida para outro tenant, seja pela via do banco, seja pela via do cache.
**Acceptance Criteria:**
- [ ] A configuração de um tenant nunca é retornada a outro tenant. [verify: project-validation]
- [ ] `GetConfigurationAsync` consulta `Tenants` filtrando pelo `Id` do tenant recebido. [verify: function-exists `GetConfigurationAsync` `src/Ciclo.Infrastructure/Services/TenantService.cs`]
- [ ] A chave de cache inclui o identificador do tenant. [verify: grep-present `tenant-config:` `src/Ciclo.Infrastructure/Services/TenantService.cs`]

### NFR-002: Compatibilidade com os fluxos de tenant existentes

**What:** As operações já entregues em `TenantService` e `TenantsController` mantêm contrato e comportamento: cadastro público de escola em `POST api/tenants/register`, identificação da escola do usuário logado em `GET api/tenants/me` e resolução do tenant por requisição.
**Acceptance Criteria:**
- [ ] O cadastro de escola continua disponível com `RegisterSchoolAsync`. [verify: function-exists `RegisterSchoolAsync` `src/Ciclo.Infrastructure/Services/TenantService.cs`]
- [ ] `GET api/tenants/me` continua respondendo `TenantSummaryDto`. [verify: grep-present `TenantSummaryDto` `src/Ciclo.Infrastructure/Contracts/TenantDtos.cs`]
- [ ] A resolução do tenant por requisição continua disponível com `SetTenant`. [verify: function-exists `SetTenant` `src/Ciclo.Infrastructure/Tenancy/TenantContext.cs`]
- [ ] A suíte de testes do projeto continua passando após a mudança. [verify: project-validation]

## Constraints

- A cota de armazenamento continua aplicada no serviço dono ao publicar documento. [verify: grep-present `storage_quota_exceeded` `src/Ciclo.Infrastructure/Services/DocumentService.cs`]
- As respostas de erro de validação reutilizam o canal existente do controller. [verify: grep-present `ProblemResponse.FromCode` `src/Ciclo.Api/Controllers/TenantsController.cs`]
- Toda coluna nova em `Tenant` declara limite de tamanho na própria entidade. [verify: grep-present `MaxLength` `src/Ciclo.Core/Entities/Tenant.cs`]
- A migração nova é adicionada ao projeto de Infraestrutura e atualiza o snapshot do modelo. [verify: grep-present `AppDbContextModelSnapshot` `src/Ciclo.Infrastructure/Migrations/`]
- Nenhum pacote externo novo é adicionado; o cache usa apenas o serviço em memória do framework da Api. [verify: project-validation]
- O locale persistido nesta spec é apenas o valor padrão da escola; a resolução do locale efetivo continua com a spec 190 e não é reimplementada aqui. [verify: grep-present `Locale` `src/Ciclo.Core/Entities/Tenant.cs`]
- A validação completa do projeto passa ao final da implementação. [verify: project-validation]

## Edge Cases & Error States

- Cores informadas em minúsculas são normalizadas para maiúsculas antes de gravar.
- `LogoUrl` em branco grava nulo e remove o logotipo da resposta.
- Reduzir `StorageQuotaBytes` abaixo do uso atual é aceito; a publicação de documento novo passa a ser recusada com `storage_quota_exceeded` até o uso ficar abaixo da cota.
- Reduzir `UserQuota` abaixo da contagem atual é aceito; novas criações de usuário são recusadas com `user_quota_exceeded`.
- Alterar `Plan` sem informar `FeatureFlags` regrava as flags com o conjunto padrão do novo plano.
- Alterar `Plan` informando `FeatureFlags` mantém exatamente as flags informadas.
- Requisição de atualização sem nenhum campo devolve 200 com o estado atual e não escreve no banco.
- Fuso horário é validado por identificador do sistema; identificador desconhecido devolve 400 com código `invalid_time_zone`.
- Tenant inexistente ou inativo devolve 404 com código `tenant_not_found` na leitura e na escrita.
- Falha de banco durante a escrita não invalida o cache e devolve o erro pelo middleware de exceções existente.
- Lista de flags vazia é um valor válido e significa escola sem funcionalidades habilitadas.

## Anti-Scope

- Nenhuma entidade separada de configuração é criada. [verify: file-absent `src/Ciclo.Core/Entities/TenantConfiguration.cs`]
- Nenhum controller paralelo para a configuração é criado. [verify: file-absent `src/Ciclo.Api/Controllers/TenantConfigurationController.cs`]
- Nenhum middleware de bloqueio por flag é criado. [verify: grep-absent `FeatureFlagMiddleware` `src/`]
- Nenhuma abstração de cache própria é criada. [verify: file-absent `src/Ciclo.Infrastructure/Caching/TenantConfigurationCache.cs`]
- Nenhuma interface de gate de funcionalidade é criada. [verify: grep-absent `IFeatureGate` `src/`]
- Nenhum middleware novo de resolução de tenant é criado. [verify: file-absent `src/Ciclo.Api/Middleware/TenantConfigurationMiddleware.cs`]

## Dependencies

- Spec 20 (multi-tenant): `Tenant`, `TenantContext`, `TenantMiddleware` e o filtro global por tenant que sustenta o isolamento do NFR-001.
- Spec 70 (documents): `DocumentService` é o dono da aplicação da cota de armazenamento e lê `Tenant.StorageQuotaBytes`.
- Spec 100 (frontend): shell, roteamento, `frontend/src/api/client.ts` e os componentes de formulário reutilizados pela página do FR-006.
- Spec 120 (identity lifecycle): `AuthService` é o dono do caminho de criação de usuário que passa a chamar a verificação de cota do FR-004.
- Spec 30 (auth): papéis e regras de autorização usados pelas rotas de configuração.
- Spec 190 (i18n): resolve o locale efetivo da interface; esta spec apenas persiste o valor padrão do tenant.

## Out of Scope

- Aplicar as flags para bloquear endpoints das funcionalidades existentes; cada spec dona consome a flag quando entregar o próprio gate.
- Cobrança, faturamento ou autoatendimento de troca de plano.
- Upload e armazenamento do arquivo de logotipo; esta spec persiste apenas a URL.
- Tela global de operadora para administrar vários tenants em uma única visão.
- Tradução das mensagens de erro da configuração; os códigos estáveis são consumidos pela spec 190.
- Migração de dados de tenants existentes para valores diferentes dos padrões declarados.
- Infraestrutura de teste de frontend; a spec 180 é a dona desse alicerce e esta spec não a antecipa.

## Test Scope

- `tests/Ciclo.Api.Tests/TenantConfigurationTests.cs` — prova a leitura, a atualização, as validações de campo, o isolamento entre tenants e a invalidação de cache.
- `tests/Ciclo.Api.Tests/TenantQuotaTests.cs` — prova a cota de usuários: limite exato, recusa com `user_quota_exceeded` e contagem restrita ao tenant.
