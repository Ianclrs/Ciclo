# Lessons Learned

---

## L1: NuGet restore quebrado neste ambiente — build e testes funcionam com `--no-restore`/`--no-build`

**Date:** 2026-08-08 (revisado em 2026-10-27 — D7)
**Category:** tooling

**Problem:** Neste ambiente **somente o `dotnet restore` está quebrado**. O componente NuGet do SDK lança
`Value cannot be null. (Parameter 'path1')` (`NuGet.Configuration.ConfigurationDefaults`). O defeito atinge
o comando que **toca o NuGet**; compilar e testar não passam por esse caminho quando se usa
`--no-restore`/`--no-build`.

A versão antiga desta lição dizia "build quebrado" — **está errado** e custou tempo: `dotnet build` falha
porque faz **restore implícito**, não porque o build em si esteja quebrado.

Medição de 2026-10-16 (SDK padrão 10.0.402; 10.0.302 e 9.0.316 também instalados):

| Comando | Resultado |
|---|---|
| `dotnet restore Ciclo.sln` | ❌ `path1` |
| `dotnet nuget locals all --list` | ❌ `path1` — confirma que é o componente do SDK, não o repositório |
| `dotnet build Ciclo.sln --no-restore` | ✅ 0 erros / 0 avisos |
| `dotnet test tests/Ciclo.Api.Tests --no-build` | ✅ (217/217 em 2026-10-27) |
| `dotnet ef` (10.0.13) | ✅ usado com sucesso no M12; nesta versão **não** aceita `--no-build` |

**Hipóteses já descartadas por medição — não repetir:** repositório, `NuGet.Config` do projeto, configs globais
(`%APPDATA%\NuGet`, `C:\ProgramData\NuGet`), variáveis de ambiente (`env -i`), registro/pastas especiais,
nós do MSBuild, `RestorePackagesPath`. Trocar de SDK não resolve: 10.0.400/10.0.402 mantêm o defeito.

**Solution:**

### Caminho normal (o que usar)

```bash
dotnet build Ciclo.sln --no-restore      # build
dotnet test tests/Ciclo.Api.Tests --no-build   # testes
```

### Quando é preciso restaurar de verdade (mudou `.csproj`)

`dotnet build --no-restore` falha com `NETSDK1060` (assets desatualizados). Aí há duas saídas que **não**
usam o componente quebrado:

```bash
# 1) MSBuild do Visual Studio (usa o NuGet do VS)
MSBUILD="C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/bin/MSBuild.exe"
"$MSBUILD" Ciclo.sln -t:Restore -p:Configuration=Debug -v:minimal

# 2) nuget.exe standalone
curl -sL "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -o /tmp/nuget.exe
/tmp/nuget.exe restore Ciclo.sln
```

> **No CI isso não se aplica:** o runner do GitHub tem NuGet íntegro, então `dotnet restore` roda normal
> (ver `.github/workflows/`). O contorno é **só desta máquina**.

**Takeaway:** antes de concluir que "o projeto não compila", rodar com `--no-restore`. Se o erro for
`Value cannot be null (path1)`, é o restore do ambiente — não o código.

---

## L2: `TreatWarningsAsErrors` + Code Analysis = Build Quebra em Testes

**Date:** 2026-08-08  
**Category:** implementation

**Problem:** O `Directory.Build.props` tem `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` e `<AnalysisLevel>latest-recommended</AnalysisLevel>`. Isso faz com que warnings de code analysis (CAxxxx) virem erros de build. Em código de testes e seeders, warnings como CA1848 (LoggerMessage delegates), CA1822 (método pode ser static), e CA1852 (tipo pode ser sealed) quebram o build desnecessariamente.

**Solution:**

1. **Código de produção:** Corrigir os warnings (renomear parâmetros, adicionar `static`/`sealed`).
2. **Seeders/Testes:** Suprimir com `#pragma warning disable` quando o warning for aceitável no contexto (ex: `LoggerExtensions` em seeders é aceitável).

**Takeaway:** Antes de implementar, sempre verificar o `Directory.Build.props` para saber o nível de rigor. Usar `#pragma warning disable CAxxxx` com moderação, apenas em código onde o warning é intencionalmente aceito.

---

## L3: Google OAuth com ClientId vazio crasha TODAS as requisições

**Date:** 2026-08-08  
**Category:** auth

**Problem:** Configurar `.AddGoogle()` com `ClientId = ""` (string vazia) faz o `OAuthOptions.Validate()` lançar `ArgumentException` em toda requisição HTTP — mesmo endpoints anônimos como `/health`. O middleware de autenticação processa todas as requisições e valida as opções na primeira vez.

**Solution:** Registrar Google OAuth condicionalmente — apenas quando `Google:ClientId` não está vazio:
```csharp
var googleClientId = builder.Configuration["Google:ClientId"];
if (!string.IsNullOrEmpty(googleClientId))
{
    builder.Services.AddAuthentication().AddGoogle(options => { ... });
}
```

**Takeaway:** Sempre verificar providers OAuth configurados com valores válidos antes de registrá-los. Providers com credenciais vazias quebram toda a pipeline de autenticação, não apenas o endpoint específico.

---

## L4: Global Query Filter bloqueia lookups pré-autenticação

**Date:** 2026-08-08  
**Category:** multi-tenancy + auth

**Problem:** `User` implementa `ITenantScoped`, então o EF Core aplica o filtro global `WHERE TenantId = @ctx`. Durante login (`FindByEmailAsync`), o tenant ainda não está resolvido (usuário não autenticado). O filtro tenta acessar `ITenantContext.TenantId` que lança `TenantNotResolvedException`, fazendo o lookup de usuário falhar silenciosamente.

**Solution:** Usar `.IgnoreQueryFilters()` em todas as queries que ocorrem antes da autenticação: `LoginAsync`, `ForgotPasswordAsync`, `ResetPasswordAsync`, `RefreshTokenAsync` (para buscar o user pelo token).

```csharp
var user = await _dbContext.Users
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(u => u.Email == request.Email);
```

**Takeaway:** Entidades `ITenantScoped` NÃO devem ser buscadas via `UserManager.FindByEmailAsync()` em fluxos pré-autenticação. Use sempre `DbContext.Users.IgnoreQueryFilters()`.

---

## L5: AddIdentityCore precisa de AddDefaultTokenProviders

**Date:** 2026-08-08  
**Category:** identity

**Problem:** `AddIdentityCore<User>()` registra `UserManager` e `RoleManager`, mas NÃO inclui os token providers para password reset (`GeneratePasswordResetTokenAsync`). Tentar gerar/resetar tokens de senha lança `NotSupportedException: No IUserTwoFactorTokenProvider<TUser> named 'Default' is registered`.

**Solution:** Sempre encadear `.AddDefaultTokenProviders()` após `.AddEntityFrameworkStores()`:
```csharp
services.AddIdentityCore<User>(...)
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();  // ← necessário para password reset
```

**Takeaway:** `AddIdentityCore` ≠ `AddIdentity`. O segundo já inclui token providers; o primeiro é minimalista e requer chamada explícita.

---

## L6: AddDefaultTokenProviders requer AddDataProtection em testes

**Date:** 2026-08-08  
**Category:** testing

**Problem:** Em testes unitários com `ServiceCollection` manual, `.AddDefaultTokenProviders()` depende de `IDataProtectionProvider` que não está registrado. Causa `InvalidOperationException: Unable to resolve service for type 'Microsoft.AspNetCore.DataProtection.IDataProtectionProvider'`.

**Solution:** Registrar `services.AddDataProtection()` antes de configurar Identity nos testes:
```csharp
services.AddDataProtection();
services.AddIdentityCore<User>(...)
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();
```

**Takeaway:** Testes que usam Identity completo (com token providers) precisam de Data Protection registrado manualmente. Em produção, o `WebApplication.CreateBuilder` já configura isso automaticamente.

---

## L7: Duas classes com `WebApplicationFactory<Program>` em paralelo quebram a suíte

**Date:** 2026-09-26  
**Category:** testing

**Problem:** Adicionar uma segunda classe de teste com `IClassFixture<WebApplicationFactory<Program>>` faz a suíte falhar com:

```
System.InvalidOperationException : The entry point exited without ever building an IHost.
   at Microsoft.Extensions.Hosting.HostFactoryResolver.HostingListener.CreateHost()
   at Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`1.CreateHost(...)
```

O xUnit executa classes diferentes em coleções paralelas. `WebApplicationFactory` descobre o host interceptando o entry point do `Program` por um hook de processo (`HostFactoryResolver`), e duas factories do mesmo entry point construindo host ao mesmo tempo disputam esse estado global. A falha aparece na classe **que já existia** (`HealthControllerTests`), não na nova — o que torna o diagnóstico enganoso.

**Solution:** Preferir testes de contrato **sem host**: reflexão sobre atributos (rotas, autorização) é determinística, não toca banco e não tem corrida. Exemplo: `tests/Ciclo.Api.Tests/ApiRouteConventionTests.cs`.

Se o host for realmente necessário em mais de uma classe, compartilhar **uma** factory via `ICollectionFixture` e marcar as classes com a mesma `[Collection]`, para serializá-las.

**Takeaway:** `WebApplicationFactory<Program>` é estado global do processo, não um objeto isolado. Antes de criar a segunda classe que usa o host, decidir entre teste por reflexão ou collection fixture compartilhada.

## L8: Selo de task e build verde não provam registro no DI

**Contexto:** Spec 150, task 4 ("Controlador administrativo e registro de dependência") selada como `done`. O controlador existia, compilava, e os testes de contrato por reflexão conferiam rota e autorização — todos verdes.

**Symptom:** Toda rota `api/academic` respondia **500** com `Unable to resolve service for type 'IAcademicService' while attempting to activate 'AcademicController'`. O registro `AddScoped<IAcademicService, AcademicService>()` nunca existiu em `Program.cs`.

**Cause:** Os gates da task provavam que o *tipo* e as *rotas* existiam, não que o container sabia construí-lo. Teste de reflexão sobre atributos não exercita o container; o build também não.

**Solution:** `builder.Services.AddScoped<IAcademicService, AcademicService>()` em `Program.cs`, ao lado de `IParentService`.

**Takeaway:** quando a task promete "registro de dependência", a prova precisa de uma chamada real (uma requisição ao endpoint, ou uma resolução no `IServiceProvider` do host). Verificar existência de arquivo/classe é evidência de outra coisa.

## L9: enum sem conversor atravessa como número, e o TypeScript não avisa

**Contexto:** Spec 160. O contrato do frontend declara `outcome: 'Renewed' | 'SkippedAlreadyExists' | 'BlockedPendingDocuments' | 'Failed'`; o backend devolve `RenewalOutcome`, um enum C#.

**Symptom:** nenhum. Compila dos dois lados e os testes de serviço passam — eles comparam o enum C#, não o JSON.

**Cause:** a API não configura `JsonStringEnumConverter` e o enum não tinha o atributo. `System.Text.Json` serializa enum como número por padrão, então o cliente recebia `0..3` enquanto o TypeScript acreditava em texto. O mapa de rótulos devolvia `undefined` e a tela mostrava "Indefinido" sem erro em lugar nenhum.

**Solution:** `[JsonConverter(typeof(JsonStringEnumConverter))]` no próprio enum — blast radius de um tipo, sem tocar no formato dos demais contratos — travado por `RenewalOutcomeWireFormatTests`, que serializa com `PropertyNamingPolicy = CamelCase` para medir o JSON que o MVC entrega.

**Takeaway:** tipo do frontend é declaração, não verificação. Todo campo enum, `DateTime` ou `decimal` que cruza a fronteira precisa de uma prova do **JSON**, não do objeto C#.
