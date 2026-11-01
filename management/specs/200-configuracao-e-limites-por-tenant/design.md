# Spec 200: configuracao-e-limites-por-tenant — Design

## Design Approach

A configuração da escola não é um agregado novo: ela é o próprio tenant. O dono do domínio já existe — a entidade `Tenant` (Core) e o serviço `TenantService` (Infrastructure) — e é estendido com os campos de configuração e com as três operações que faltam (ler, atualizar e verificar cota de usuários). A API reutiliza o controller `TenantsController`, o canal de erro `ProblemResponse.FromCode` e a resolução de tenant por claim já existentes. O cache é o `IMemoryCache` do framework, com uma única chave por tenant e invalidação na escrita. No frontend, o módulo `frontend/src/api/tenants.ts` já é o dono das chamadas de tenant e é estendido; a página nova segue o padrão das demais páginas administrativas em `frontend/src/pages/admin/`.

Duas decisões guiam o resto:

1. **A leitura de configuração é cacheada; a verificação de cota não.** A cota precisa do valor corrente para não permitir nem bloquear de forma errada, então `EnsureUserQuotaAsync` lê `Tenants` direto. A leitura de configuração é dominada por consultas repetidas da interface, então é cacheada com expiração absoluta de 5 minutos e invalidação imediata na escrita.
2. **O plano define o conjunto inicial de flags; o tenant guarda o conjunto efetivo.** `TenantFeature.DefaultFor(plan)` é a fonte única dos conjuntos por plano. `FeatureFlags` é gravado como texto separado por vírgula, normalizado (ordenado, sem duplicatas, apenas nomes conhecidos). Isso entrega "flags de funcionalidades por plano" e "políticas distintas sem código específico" sem tabela de junção nem segundo conceito de domínio.

## Architecture Decisions

### ADR-200-1: A configuração vive na entidade `Tenant`

- **Contexto:** o V20 pede configuração por escola; já existem `Tenant` com `Name`, `Country`, `StorageQuotaBytes` e `IsActive`.
- **Decisão:** adicionar colunas a `Tenants` e projetar `TenantConfigurationDto` na borda. Nenhuma tabela `TenantConfigurations`.
- **Consequência:** uma migração aditiva; toda consulta de configuração continua sendo a leitura da linha do tenant, sob o filtro global por tenant.

### ADR-200-2: Modelo de planos e flags

- **Contexto:** o V20 pede flags de funcionalidades por plano e a decisão de modelo precisa ser registrada.
- **Decisão:** `TenantPlan` é um enum fechado (`Basic`, `Standard`, `Premium`). `TenantFeature` é uma classe estática com as constantes de flag, o mapa por plano e dois métodos: `DefaultFor(TenantPlan)` devolve a lista padrão; `Normalize(string? csv)` devolve a lista ordenada, sem duplicatas e validada contra as constantes, lançando `ArgumentException` para nomes desconhecidos. O conjunto efetivo é persistido em `Tenant.FeatureFlags` ou `FeatureFlags`, não derivado em tempo de leitura.
- **Consequência:** trocar de plano regrava as flags no conjunto padrão do plano novo quando a requisição não informa flags; informar flags explicitamente permite qualquer combinação conhecida, que é o "sem código específico" do V20. A flag não bloqueia endpoints de outras especificações — isso permanece fora de escopo.

Conjuntos padrão:

| Plano | Flags |
|---|---|
| `Basic` | `Documents`, `ParentPortal` |
| `Standard` | `Documents`, `ParentPortal`, `Notifications`, `AcademicPerformance` |
| `Premium` | `AcademicPerformance`, `AuditExport`, `BatchReenrollment`, `Documents`, `Notifications`, `ParentPortal` |

### ADR-200-3: Cache em memória com invalidação na escrita

- **Contexto:** o V20 pede cache com invalidação; a Api roda em instância única sobre banco único.
- **Decisão:** `IMemoryCache` injetado em `TenantService`; chave `"tenant-config:" + tenantId`; expiração absoluta de 5 minutos; `Remove` após `SaveChangesAsync` bem-sucedido.
- **Consequência:** sem dependência distribuída e sem risco de servir configuração de outro tenant (a chave contém o identificador); uma escrita concorrente em outro processo converge em até 5 minutos.

### ADR-200-4: A tela administrativa reutiliza o módulo de API de tenant

- **Contexto:** o frontend é React com Vite e já possui `frontend/src/api/tenants.ts`.
- **Decisão:** estender esse módulo com as duas chamadas de configuração e criar a página `frontend/src/pages/admin/TenantSettings.tsx`, registrada em `frontend/src/App.tsx` sob a guarda de papel existente.
- **Consequência:** um único dono das chamadas de tenant; nenhum cliente HTTP novo.

## Component / Module Breakdown

### Component: `Tenant` (entidade)

**Purpose:** persistir a configuração da escola na linha do tenant.
**Interface:** campos adicionados em `src/Ciclo.Core/Entities/Tenant.cs`.

```csharp
[MaxLength(200)] public string? DisplayName { get; set; }
[MaxLength(500)] public string? LogoUrl { get; set; }
[MaxLength(7)]   public string? PrimaryColor { get; set; }
[MaxLength(7)]   public string? SecondaryColor { get; set; }
[MaxLength(100)] public string TimeZone { get; set; } = "UTC";
public int DefaultSchoolYear { get; set; } = 2025;
[MaxLength(5)]   public string Locale { get; set; } = "pt-BR";
public TenantPlan Plan { get; set; } = TenantPlan.Basic;
[MaxLength(500)] public string FeatureFlags { get; set; } = string.Empty;
public int UserQuota { get; set; } = 50;
```

**Internal Logic:** entidade de dados; nenhuma regra de validação mora nela.
**Dependencies:** `TenantPlan`, `TenantFeature`.
**Error Handling:** nenhum. A entidade não lança; valores inválidos são impedidos antes da escrita.

### Component: `TenantPlan`

**Purpose:** enumerar os planos comerciais fechados.
**Interface:** `public enum TenantPlan { Basic = 0, Standard = 1, Premium = 2 }` em `src/Ciclo.Core/Entities/TenantPlan.cs`.
**Internal Logic:** sem lógica.
**Dependencies:** nenhuma.
**Error Handling:** texto desconhecido é recusado por `TenantService` com `invalid_plan`; nunca há `Enum.Parse` cru sem verificação.

### Component: `TenantFeature`

**Purpose:** ser a fonte única das flags conhecidas e do conjunto padrão por plano.
**Interface:** em `src/Ciclo.Core/Entities/TenantFeature.cs`.

```csharp
public static class TenantFeature
{
    public const string Documents = "Documents";
    public const string Notifications = "Notifications";
    public const string ParentPortal = "ParentPortal";
    public const string AcademicPerformance = "AcademicPerformance";
    public const string BatchReenrollment = "BatchReenrollment";
    public const string AuditExport = "AuditExport";

    public static IReadOnlySet<string> All { get; }
    public static IReadOnlyList<string> DefaultFor(TenantPlan plan);
    public static IReadOnlyList<string> Normalize(string? csv);
    public static string ToCsv(IEnumerable<string> flags);
}
```

**Internal Logic:** `Normalize` divide por vírgula, remove espaços e entradas vazias, valida cada nome contra `All` (lançando `ArgumentException` com os nomes inválidos), remove duplicatas e ordena com `StringComparer.Ordinal`. `DefaultFor` devolve a lista do mapa interno, já ordenada. `ToCsv` aplica `Normalize` e junta com vírgula.
**Dependencies:** `TenantPlan`.
**Error Handling:** `ArgumentException` é capturada por `TenantService` e traduzida em `AuthException("invalid_feature_flag", 400)`; leitura de dado legado com flag desconhecida devolve a interseção válida, sem exceção.

### Component: contratos de transporte

**Purpose:** contrato de leitura e escrita na borda HTTP.
**Interface:** em `src/Ciclo.Infrastructure/Contracts/TenantDtos.cs`.

```csharp
public record TenantConfigurationDto(
    Guid TenantId, string DisplayName, string? LogoUrl,
    string? PrimaryColor, string? SecondaryColor, string TimeZone,
    int DefaultSchoolYear, string Locale, long StorageQuotaBytes,
    int UserQuota, string Plan, IReadOnlyList<string> FeatureFlags);

public record UpdateTenantConfigurationRequest(
    string? DisplayName, string? LogoUrl,
    string? PrimaryColor, string? SecondaryColor, string? TimeZone,
    int? DefaultSchoolYear, string? Locale, long? StorageQuotaBytes,
    int? UserQuota, string? Plan, IReadOnlyList<string>? FeatureFlags);
```

**Internal Logic:** records de transporte; `Plan` é texto dos dois lados para manter o JSON estável e independente de configuração de serializador.
**Dependencies:** nenhuma.
**Error Handling:** campo nulo significa preservar; o serviço distingue nulo de vazio (vazio grava nulo em `DisplayName`, `LogoUrl` e cores).

### Component: `TenantService` — leitura de configuração

**Purpose:** prover o `TenantConfigurationDto` do tenant, com cache.
**Interface:** em `src/Ciclo.Infrastructure/Services/TenantService.cs`.

```csharp
Task<TenantConfigurationDto> GetConfigurationAsync(Guid tenantId);
```

**Internal Logic:**
1. Monta `cacheKey = "tenant-config:" + tenantId` e tenta `_cache.TryGetValue`; se existir, devolve.
2. Consulta `_dbContext.Tenants.AsNoTracking()` filtrando por `Id` e `IsActive`.
3. Projeta o DTO: `DisplayName` recebe `DisplayName` quando não vazio, senão `Name`; `FeatureFlags` recebe `TenantFeature.Normalize(tenant.FeatureFlags)`; `Plan` recebe `tenant.Plan.ToString()`.
4. Grava no cache com `AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)`.
5. Devolve o DTO.
**Dependencies:** `AppDbContext`, `IMemoryCache`, `TenantFeature`.
**Error Handling:** tenant ausente ou inativo lança `AuthException("tenant_not_found", 404)` e o cache não é populado.

### Component: `TenantService` — atualização de configuração

**Purpose:** aplicar as mudanças com validação e invalidar o cache.
**Interface:** em `src/Ciclo.Infrastructure/Services/TenantService.cs`.

```csharp
Task<TenantConfigurationDto> UpdateConfigurationAsync(Guid tenantId, UpdateTenantConfigurationRequest request);
```

**Internal Logic:**
1. Carrega o tenant rastreado; ausente ou inativo lança `AuthException("tenant_not_found", 404)`.
2. Aplica campo a campo: `DisplayName` (nulo preserva; vazio grava nulo), `LogoUrl` (idem), cores (nulo preserva; vazio grava nulo; não vazio precisa casar seis dígitos hexadecimais após `#`, senão `invalid_color`; grava em maiúsculas), `TimeZone` (nulo preserva; valida com `TimeZoneInfo.FindSystemTimeZoneById` em `try/catch (TimeZoneNotFoundException, InvalidTimeZoneException)`, senão `invalid_time_zone`), `DefaultSchoolYear` (nulo preserva; exige 2000 a 2100, senão `invalid_school_year`), `Locale` (nulo preserva; exige `pt-BR`, `en-US` ou `es-ES`, senão `invalid_locale`), `StorageQuotaBytes` (nulo preserva; exige maior que zero, senão `invalid_storage_quota`), `UserQuota` (nulo preserva; exige 1 a 10000, senão `invalid_user_quota`), `Plan` (nulo preserva; exige nome válido de `TenantPlan`, senão `invalid_plan`), `FeatureFlags` (informado: `TenantFeature.Normalize` e grava; omitido: grava `TenantFeature.DefaultFor` do plano final).
3. Executa `SaveChangesAsync`.
4. Remove `"tenant-config:" + tenantId` do cache.
5. Devolve o DTO reconstruído do estado persistido.
**Dependencies:** `AppDbContext`, `IMemoryCache`, `TenantFeature`, `TenantPlan`.
**Error Handling:** toda validação lança `AuthException(codigo, 400)` antes de qualquer escrita, de modo que requisição inválida não altera banco nem cache. Falha de `SaveChangesAsync` propaga e não remove a entrada de cache. Requisição sem campos devolve o estado atual sem escrever.

### Component: `TenantService` — cota de usuários

**Purpose:** ser o dono único da regra de cota de usuários por tenant.
**Interface:** em `src/Ciclo.Infrastructure/Services/TenantService.cs`.

```csharp
Task EnsureUserQuotaAsync(Guid tenantId);
```

**Internal Logic:**
1. Lê `Tenant` por `Id`, sem cache; ausente ou inativo lança `AuthException("tenant_not_found", 404)`.
2. Conta `_dbContext.Users.Count(u => u.TenantId == tenantId)`.
3. Se `contagem >= tenant.UserQuota`, lança `AuthException("user_quota_exceeded", 409)`.
**Dependencies:** `AppDbContext`.
**Error Handling:** falha de contagem propaga e o chamador não persiste o usuário; a verificação não escreve nada, então é idempotente e segura para repetir.

### Component: chamada da cota no serviço dono

**Purpose:** aplicar a cota no ponto em que o usuário é criado.
**Interface:** chamada `await _tenantService.EnsureUserQuotaAsync(tenantId)` imediatamente antes de persistir o novo `User`, no serviço de `src/Ciclo.Infrastructure/Services/` que insere o usuário — hoje `AuthService`, que atende o aceite de convite e a criação de usuário administrativa.
**Internal Logic:** uma chamada; nenhuma lógica de contagem duplicada no chamador.
**Dependencies:** `ITenantService`.
**Error Handling:** `AuthException` de cota sobe pelo mesmo caminho das demais falhas do fluxo e é traduzida em 409 com código `user_quota_exceeded`.

### Component: `TenantsController` — ações de configuração

**Purpose:** expor a configuração na borda HTTP autenticada.
**Interface:** em `src/Ciclo.Api/Controllers/TenantsController.cs`.

```csharp
[HttpGet("me/configuration")]
[Authorize]
Task<IActionResult> GetConfiguration();

[HttpPut("me/configuration")]
[Authorize(Roles = "Admin")]
Task<IActionResult> UpdateConfiguration([FromBody] UpdateTenantConfigurationRequest request);
```

**Internal Logic:** cada ação obtém o tenant por `GetTenantId()` e delega ao serviço, devolvendo `Ok(dto)`; `AuthException` é traduzida por `ProblemResponse.FromCode(ex.Message, ex.StatusCode)`, como em `Register` e `GetCurrent`.
**Dependencies:** `ITenantService`, `ProblemResponse`.
**Error Handling:** 401 por ausência de token; 401 `tenant_not_resolved` quando a claim falta ou não parseia; 403 pela política de papel na ação de escrita; 404 `tenant_not_found`; 400 com o código específico de cada validação.

### Component: composição do cache

**Purpose:** registrar `IMemoryCache` na Api.
**Interface:** `builder.Services.AddMemoryCache();` em `src/Ciclo.Api/Program.cs`, junto do registro de `ITenantService`.
**Internal Logic:** uma linha, sem configuração adicional; a expiração é por entrada, definida no serviço.
**Dependencies:** `Microsoft.Extensions.Caching.Memory`, já disponível no framework da Api.
**Error Handling:** sem o registro a injeção falha na inicialização, o que é detectado pelo host de teste — falha explícita, nunca silenciosa.

### Component: persistência (migração)

**Purpose:** criar as colunas novas em `Tenants`.
**Interface:** migração `src/Ciclo.Infrastructure/Migrations/20270101000000_AddTenantConfiguration.cs` e atualização de `src/Ciclo.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`, geradas por `dotnet ef migrations add AddTenantConfiguration`.
**Internal Logic:** `AddColumn` por campo, com `nullable` correta, `defaultValue` igual ao padrão da entidade e `maxLength` igual ao atributo.
**Dependencies:** `AppDbContext`.
**Error Handling:** `Down` remove apenas as colunas desta spec; as colunas pré-existentes permanecem.

### Component: cliente de API do frontend

**Purpose:** concentrar as chamadas de configuração.
**Interface:** em `frontend/src/api/tenants.ts`, reutilizando o cliente HTTP de `frontend/src/api/client.ts`.

```ts
export type TenantConfiguration = {
  tenantId: string; displayName: string; logoUrl: string | null;
  primaryColor: string | null; secondaryColor: string | null;
  timeZone: string; defaultSchoolYear: number; locale: string;
  storageQuotaBytes: number; userQuota: number; plan: string;
  featureFlags: string[];
};

export type UpdateTenantConfiguration = Partial<Omit<TenantConfiguration, 'tenantId'>>;

export function getTenantConfiguration(): Promise<TenantConfiguration>;
export function updateTenantConfiguration(payload: UpdateTenantConfiguration): Promise<TenantConfiguration>;
```

**Internal Logic:** `GET` e `PUT` em `api/tenants/me/configuration` pelo cliente existente, que já trata token e formato de erro.
**Dependencies:** `frontend/src/api/client.ts`.
**Error Handling:** erros propagam no formato já usado pelo cliente; a página consome o `code` do corpo de erro pela tradução existente em `frontend/src/api/errorMessages.ts`.

### Component: página administrativa

**Purpose:** permitir que o Admin edite a configuração sem chamada manual à API.
**Interface:** `frontend/src/pages/admin/TenantSettings.tsx`, exportando o componente `TenantSettings`; rota registrada em `frontend/src/App.tsx` sob a proteção de papel existente.
**Internal Logic:** ao montar, chama `getTenantConfiguration()`; renderiza formulário com nome de exibição, logotipo, cor primária, cor secundária, fuso, ano letivo padrão, locale, cota de armazenamento, cota de usuários, plano e flags; ao salvar, chama `updateTenantConfiguration` com os campos alterados e substitui o estado pelo retorno.
**Dependencies:** `frontend/src/api/tenants.ts`, componentes existentes de formulário e o contexto de autenticação.
**Error Handling:** falha na carga exibe o componente existente de erro de carregamento, sem habilitar o formulário; resposta 400 ou 403 mantém os valores carregados e exibe o código de erro; envio sem alteração não chama a API.

## Data Flow

**Leitura (`GET api/tenants/me/configuration`)**
1. `TenantMiddleware` resolve o tenant da sessão a partir da claim `tenant_id`.
2. `TenantsController.GetConfiguration` obtém o identificador e chama `GetConfigurationAsync`.
3. `TenantService` consulta o `IMemoryCache` pela chave `tenant-config:<guid>`.
4. Em ausência, lê `Tenants` por `Id`, projeta o DTO, grava no cache por 5 minutos e devolve.
5. O controller responde 200 com `TenantConfigurationDto`.

**Escrita (`PUT api/tenants/me/configuration`)**
1. A autorização exige papel Admin.
2. `TenantService.UpdateConfigurationAsync` carrega o tenant rastreado.
3. Aplica campo a campo com validação; qualquer falha lança `AuthException` e nada é persistido.
4. `SaveChangesAsync` grava; em seguida a chave de cache do tenant é removida.
5. A resposta é o DTO reconstruído do estado persistido, então tela e próxima leitura convergem imediatamente.

**Cota de usuários (criação de usuário)**
1. O serviço dono da criação chama `EnsureUserQuotaAsync(tenantId)`.
2. `TenantService` lê `Tenant.UserQuota` no banco, sem cache.
3. Conta os usuários do tenant e compara com a cota.
4. Cota atingida lança `AuthException("user_quota_exceeded", 409)` e o chamador não persiste o usuário.

## Data Structures

`Tenants` (colunas novas na mesma tabela):

| Coluna | Tipo | Nulo | Limite | Padrão |
|---|---|---|---|---|
| `DisplayName` | texto | sim | 200 | nulo |
| `LogoUrl` | texto | sim | 500 | nulo |
| `PrimaryColor` | texto | sim | 7 | nulo |
| `SecondaryColor` | texto | sim | 7 | nulo |
| `TimeZone` | texto | não | 100 | `"UTC"` |
| `DefaultSchoolYear` | inteiro | não | não se aplica | 2025 |
| `Locale` | texto | não | 5 | `"pt-BR"` |
| `Plan` | inteiro (enum) | não | não se aplica | 0 (`Basic`) |
| `FeatureFlags` | texto | não | 500 | string vazia |
| `UserQuota` | inteiro | não | não se aplica | 50 |

Códigos de erro estáveis: `invalid_color`, `invalid_time_zone`, `invalid_school_year`, `invalid_locale`, `invalid_user_quota`, `invalid_storage_quota`, `invalid_feature_flag`, `invalid_plan`, `tenant_not_found`, `tenant_not_resolved`, `user_quota_exceeded`.

## File / Module Layout

| Arquivo | Operação | Conteúdo |
|---|---|---|
| src/Ciclo.Core/Entities/Tenant.cs | modificar | dez propriedades novas de configuração |
| src/Ciclo.Core/Entities/TenantPlan.cs | criar | enum `TenantPlan` |
| src/Ciclo.Core/Entities/TenantFeature.cs | criar | constantes, `All`, `DefaultFor`, `Normalize`, `ToCsv` |
| src/Ciclo.Infrastructure/Contracts/TenantDtos.cs | modificar | `TenantConfigurationDto`, `UpdateTenantConfigurationRequest` |
| src/Ciclo.Infrastructure/Services/TenantService.cs | modificar | três operações novas, `IMemoryCache`, validação |
| src/Ciclo.Infrastructure/Services/AuthService.cs | modificar | chamada de `EnsureUserQuotaAsync` antes de criar usuário |
| src/Ciclo.Infrastructure/Migrations/20270101000000_AddTenantConfiguration.cs | criar | migração aditiva |
| src/Ciclo.Infrastructure/Migrations/AppDbContextModelSnapshot.cs | modificar | snapshot regenerado |
| src/Ciclo.Api/Controllers/TenantsController.cs | modificar | `GET` e `PUT` de `me/configuration` |
| src/Ciclo.Api/Program.cs | modificar | `AddMemoryCache` |
| tests/Ciclo.Api.Tests/TenantConfigurationTests.cs | criar | configuração ponta a ponta |
| tests/Ciclo.Api.Tests/TenantQuotaTests.cs | criar | cota de usuários |
| tests/Ciclo.Api.Tests/TenantServiceTests.cs | modificar | construtor com `IMemoryCache` |
| tests/Ciclo.Api.Tests/TenantSummaryTests.cs | modificar | construtor com `IMemoryCache` |
| frontend/src/api/tenants.ts | modificar | tipos e duas funções de configuração |
| frontend/src/pages/admin/TenantSettings.tsx | criar | formulário da configuração |
| frontend/src/App.tsx | modificar | rota protegida da página |

## Testing Strategy

`tests/Ciclo.Api.Tests/TenantConfigurationTests.cs` — hospeda a Api com o host de teste existente e prova: leitura de um tenant com valores padrão; `DisplayName` caindo para `Name`; normalização de cor em minúsculas; `FeatureFlags` ordenado e sem duplicatas; cada código de erro de validação (`invalid_color`, `invalid_time_zone`, `invalid_school_year`, `invalid_locale`, `invalid_user_quota`, `invalid_storage_quota`, `invalid_feature_flag`, `invalid_plan`) com status 400; 403 para papel que não é Admin; 401 sem token; 404 `tenant_not_found`; preservação de campo nulo; regravação das flags ao trocar de plano sem informar flags; isolamento entre dois tenants; leitura após escrita devolvendo o valor novo, o que prova a invalidação de cache.

`tests/Ciclo.Api.Tests/TenantQuotaTests.cs` — prova: `EnsureUserQuotaAsync` aceita a criação até `UserQuota` e recusa a seguinte com 409 `user_quota_exceeded`; usuários de outro tenant não entram na contagem; a criação de usuário devolve 409 quando a cota está esgotada; tenant inexistente devolve `tenant_not_found`.

`tests/Ciclo.Api.Tests/TenantServiceTests.cs` e `tests/Ciclo.Api.Tests/TenantSummaryTests.cs` — atualizados para o novo construtor de `TenantService` com um `MemoryCache` real; provam que o cadastro de escola e a identificação da escola continuam com o mesmo contrato.

Validação do projeto: `dotnet build` e `dotnet test tests/Ciclo.Api.Tests`, mais `npm run build` em `frontend/`.

## Migration / Rollback

- **Migração:** `dotnet ef migrations add AddTenantConfiguration --project src/Ciclo.Infrastructure --startup-project src/Ciclo.Api`. As colunas não anuláveis recebem `defaultValue` igual ao padrão da entidade, então as linhas já existentes em `Tenants` continuam válidas e passam a operar com `Basic`, `UTC`, `pt-BR`, 2025, cota de 50 usuários e nenhuma flag.
- **Dados históricos:** não há migração de dados; `StorageQuotaBytes` já existia e mantém o valor de cada escola.
- **Rollback:** `dotnet ef database update <migração anterior>`; o `Down` remove apenas as colunas desta spec. Reverter o código sem reverter o esquema é seguro porque as colunas novas não são lidas pelo código anterior.
- **Cache:** não há estado a invalidar em rollback; o cache é em memória e esvazia a cada reinício do processo.
