<!-- PIPELINE-CONTRACT: Task completion is determined by "**Status:** [x] done" at the end of each "### Task N" section.
     Checkboxes "- [ ]" under "**Validation:**" are human verification guides — the pipeline ignores them completely.
     To mark a task as done, add "**Status:** [x] done". To mark it as pending, use "**Status:** [ ] pending". -->
# Spec 200: configuracao-e-limites-por-tenant — Implementation Tasks

## Planned Scaffolds

`src/Ciclo.Core/Entities/TenantPlan.cs`
`src/Ciclo.Core/Entities/TenantFeature.cs`
`src/Ciclo.Infrastructure/Migrations/20270101000000_AddTenantConfiguration.cs`
`src/Ciclo.Infrastructure/Migrations/20270101000000_AddTenantConfiguration.Designer.cs`
`tests/Ciclo.Api.Tests/TenantConfigurationTests.cs`
`tests/Ciclo.Api.Tests/TenantQuotaTests.cs`
`frontend/src/pages/admin/TenantSettings.tsx`

## Task Order

Tasks MUST be executed sequentially in numerical order. Each task depends on the completion of all preceding tasks.

## Tasks

### Task 1: Domínio da configuração na entidade `Tenant`

**Objective:** Adicionar os campos de configuração à entidade `Tenant` e criar o enum de plano e a classe única de flags.
**Requirements Covered:** FR-001
**Design References:** ADR-200-1, ADR-200-2, Component `Tenant`, Component `TenantPlan`, Component `TenantFeature`, Data Structures.
**Actions:**
1. Em `src/Ciclo.Core/Entities/Tenant.cs`, adicionar as dez propriedades do design com os respectivos atributos `[MaxLength]` e valores padrão: `DisplayName` (200, nulo), `LogoUrl` (500, nulo), `PrimaryColor` (7, nulo), `SecondaryColor` (7, nulo), `TimeZone` (100, `"UTC"`), `DefaultSchoolYear` (2025), `Locale` (5, `"pt-BR"`), `Plan` (`TenantPlan.Basic`), `FeatureFlags` (500, `string.Empty`) e `UserQuota` (50). Não remover `StorageQuotaBytes` nem os campos existentes.
2. Criar `src/Ciclo.Core/Entities/TenantPlan.cs` com o enum `TenantPlan { Basic = 0, Standard = 1, Premium = 2 }`.
3. Criar `src/Ciclo.Core/Entities/TenantFeature.cs` com as seis constantes de flag, a propriedade `All`, o mapa interno por plano e os métodos `DefaultFor(TenantPlan)`, `Normalize(string?)` e `ToCsv(IEnumerable<string>)`, conforme as assinaturas do design. `Normalize` lança `ArgumentException` com os nomes inválidos quando encontra flag desconhecida e devolve a lista ordenada e sem duplicatas quando todas são válidas.
**Expected Files:**
- `src/Ciclo.Core/Entities/Tenant.cs`
- `src/Ciclo.Core/Entities/TenantPlan.cs`
- `src/Ciclo.Core/Entities/TenantFeature.cs`
**Validation:** `dotnet build src/Ciclo.Core` compila sem erro; `grep` confirma `PrimaryColor`, `DefaultSchoolYear`, `FeatureFlags` e `StorageQuotaBytes` em `src/Ciclo.Core/Entities/Tenant.cs`; `TenantFeature` presente em `src/Ciclo.Core/Entities/TenantFeature.cs`.
**Status:** [x] done

### Task 2: Contratos, serviço de configuração, cache e migração

**Objective:** Entregar a leitura, a atualização e a verificação de cota no dono de domínio, com cache invalidado na escrita e esquema persistido.
**Requirements Covered:** FR-002, FR-003, FR-004, FR-005, NFR-001
**Design References:** ADR-200-3, Componentes de `TenantService` (leitura, atualização, cota), Component `contratos de transporte`, Component `composição do cache`, Component `persistência (migração)`, Data Flow.
**Actions:**
1. Em `src/Ciclo.Infrastructure/Contracts/TenantDtos.cs`, adicionar os records `TenantConfigurationDto` e `UpdateTenantConfigurationRequest` exatamente com as assinaturas do design.
2. Em `src/Ciclo.Infrastructure/Services/TenantService.cs`, declarar os três métodos na interface `ITenantService` (`GetConfigurationAsync`, `UpdateConfigurationAsync`, `EnsureUserQuotaAsync`) e implementá-los conforme a lógica interna do design, injetando `IMemoryCache` no construtor; chave de cache `"tenant-config:" + tenantId`, expiração absoluta de 5 minutos, remoção da chave após `SaveChangesAsync` bem-sucedido.
3. Aplicar as validações na ordem do design, lançando `AuthException` com os códigos `invalid_color`, `invalid_time_zone`, `invalid_school_year`, `invalid_locale`, `invalid_user_quota`, `invalid_storage_quota`, `invalid_feature_flag` e `invalid_plan`, e `tenant_not_found` para tenant ausente ou inativo.
4. Gerar a migração `src/Ciclo.Infrastructure/Migrations/20270101000000_AddTenantConfiguration.cs` e o snapshot atualizado com `dotnet ef migrations add AddTenantConfiguration --project src/Ciclo.Infrastructure --startup-project src/Ciclo.Api`, garantindo `defaultValue` e `maxLength` por coluna.
5. Em `src/Ciclo.Api/Program.cs`, registrar `builder.Services.AddMemoryCache();` no mesmo ponto de composição onde `ITenantService` é registrado.
**Expected Files:**
- `src/Ciclo.Infrastructure/Contracts/TenantDtos.cs`
- `src/Ciclo.Infrastructure/Services/TenantService.cs`
- `src/Ciclo.Infrastructure/Migrations/20270101000000_AddTenantConfiguration.cs`
- `src/Ciclo.Infrastructure/Migrations/20270101000000_AddTenantConfiguration.Designer.cs`
- `src/Ciclo.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
- `src/Ciclo.Api/Program.cs`
**Validation:** `dotnet build` compila sem erro; `grep` confirma `GetConfigurationAsync`, `UpdateConfigurationAsync`, `EnsureUserQuotaAsync` e `IMemoryCache` em `src/Ciclo.Infrastructure/Services/TenantService.cs`; `AddMemoryCache` em `src/Ciclo.Api/Program.cs`; `UserQuota` aparece em `src/Ciclo.Infrastructure/Migrations/`.
**Status:** [x] done

### Task 3: Cota de usuários aplicada no serviço dono

**Objective:** Fazer o caminho de criação de usuário consultar a cota do tenant antes de persistir.
**Requirements Covered:** FR-004
**Design References:** Component `chamada da cota no serviço dono`, Data Flow (cota de usuários).
**Actions:**
1. Em `src/Ciclo.Infrastructure/Services/AuthService.cs`, injetar `ITenantService` e chamar `await _tenantService.EnsureUserQuotaAsync(tenantId)` imediatamente antes do `SaveChangesAsync` (ou equivalente) que persiste o novo `User`, em cada fluxo que cria usuário (aceite de convite e criação administrativa).
2. Não duplicar a contagem de usuários no chamador; a regra permanece só em `TenantService`.
**Expected Files:**
- `src/Ciclo.Infrastructure/Services/AuthService.cs`
**Validation:** `dotnet build` compila sem erro; `grep` confirma `EnsureUserQuotaAsync` em `src/Ciclo.Infrastructure/Services/AuthService.cs`.
**Status:** [x] done

### Task 4: Endpoints de configuração na API

**Objective:** Expor a leitura e a escrita da configuração no controller de tenant existente.
**Requirements Covered:** FR-002, FR-003
**Design References:** Component `TenantsController — ações de configuração`, Data Flow.
**Actions:**
1. Em `src/Ciclo.Api/Controllers/TenantsController.cs`, adicionar a ação `GetConfiguration` com `[HttpGet("me/configuration")]` e `[Authorize]`, delegando a `_tenantService.GetConfigurationAsync(GetTenantId())` e devolvendo `Ok(dto)`.
2. Adicionar a ação `UpdateConfiguration` com `[HttpPut("me/configuration")]` e `[Authorize(Roles = "Admin")]`, delegando a `_tenantService.UpdateConfigurationAsync(GetTenantId(), request)` e devolvendo `Ok(dto)`.
3. Em ambas, capturar `AuthException` e responder com `ProblemResponse.FromCode(ex.Message, ex.StatusCode)`, como as ações `Register` e `GetCurrent` já fazem.
**Expected Files:**
- `src/Ciclo.Api/Controllers/TenantsController.cs`
**Validation:** `dotnet build` compila sem erro; `grep` confirma `me/configuration`, `HttpPut` e `ProblemResponse.FromCode` em `src/Ciclo.Api/Controllers/TenantsController.cs`.
**Status:** [x] done

### Task 5: Tela administrativa de configuração no frontend

**Objective:** Entregar a página administrativa que lê e atualiza a configuração da escola.
**Requirements Covered:** FR-006
**Design References:** ADR-200-4, Component `cliente de API do frontend`, Component `página administrativa`.
**Actions:**
1. Em `frontend/src/api/tenants.ts`, adicionar os tipos `TenantConfiguration` e `UpdateTenantConfiguration` e as funções `getTenantConfiguration()` e `updateTenantConfiguration(payload)`, chamando `api/tenants/me/configuration` pelo cliente existente de `frontend/src/api/client.ts`.
2. Criar `frontend/src/pages/admin/TenantSettings.tsx` com o componente `TenantSettings`, que carrega pela leitura ao montar, renderiza os campos de configuração e envia a escrita ao salvar, exibindo o código de erro em recusas 400 e 403.
3. Registrar a rota `TenantSettings` em `frontend/src/App.tsx` sob a proteção de papel já usada pelas demais páginas administrativas.
**Expected Files:**
- `frontend/src/api/tenants.ts`
- `frontend/src/pages/admin/TenantSettings.tsx`
- `frontend/src/App.tsx`
**Validation:** `npm run build` em `frontend/` conclui sem erro; `grep` confirma `me/configuration` em `frontend/src/api/tenants.ts` e `TenantSettings` em `frontend/src/App.tsx`.
**Status:** [x] done

### Task 6: Testes da configuração, da cota e da compatibilidade

**Objective:** Provar por teste a leitura, a atualização, as validações, o isolamento, a invalidação de cache e a cota de usuários, mantendo os testes de tenant existentes verdes.
**Requirements Covered:** FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, NFR-001, NFR-002
**Design References:** Testing Strategy.
**Actions:**
1. Criar `tests/Ciclo.Api.Tests/TenantConfigurationTests.cs` com os cenários da Testing Strategy: valores padrão, herança de `DisplayName`, normalização de cor, flags ordenadas, um teste por código de erro de validação, 403 para papel não Admin, 401 sem token, 404 `tenant_not_found`, preservação de campo nulo, regravação de flags na troca de plano, isolamento entre dois tenants e leitura após escrita.
2. Criar `tests/Ciclo.Api.Tests/TenantQuotaTests.cs` com os cenários de cota: limite exato, recusa com `user_quota_exceeded`, contagem restrita ao tenant e tenant inexistente.
3. Atualizar `tests/Ciclo.Api.Tests/TenantServiceTests.cs` e `tests/Ciclo.Api.Tests/TenantSummaryTests.cs` para o novo construtor de `TenantService` com um `MemoryCache` real, preservando as asserções existentes.
**Expected Files:**
- `tests/Ciclo.Api.Tests/TenantConfigurationTests.cs`
- `tests/Ciclo.Api.Tests/TenantQuotaTests.cs`
- `tests/Ciclo.Api.Tests/TenantServiceTests.cs`
- `tests/Ciclo.Api.Tests/TenantSummaryTests.cs`
**Validation:** `dotnet test tests/Ciclo.Api.Tests` passa por completo, incluindo os testes de tenant preexistentes; `grep` confirma os dois arquivos de teste novos.
**Status:** [x] done
