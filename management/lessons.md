# Lessons Learned

---

## L1: .NET SDK 10.0.302 — NuGet Restore / Build Quebrado

**Date:** 2026-08-08  
**Category:** tooling

**Problem:** O .NET SDK 10.0.302 vem com `NuGet.Configuration.dll v7.6.0-rc.33009` (Release Candidate) que tem um bug no construtor estático da classe `ConfigurationDefaults`. Qualquer operação que toca no NuGet falha com:

```
error: Value cannot be null. (Parameter 'path1')
The type initializer for 'NuGet.Configuration.ConfigurationDefaults' threw an exception.
```

Afeta TODOS os comandos:
- `dotnet restore` ❌
- `dotnet build` ❌ (tenta ler `project.assets.json` via NuGet)
- `dotnet nuget *` ❌
- `dotnet test` com build implícito ❌

O reparo/reinstalação do SDK não resolve — a versão 10.0.302 é a mais recente disponível e mantém o mesmo binário RC bugado. O SDK 9.0.316 funciona mas não suporta `net10.0`. O SDK 10.0.202 está corrompido na instalação (pasta incompleta).

**Solution:** Usar ferramentas alternativas que não dependem do SDK bugado:

### Restore: `nuget.exe` standalone

```bash
# Baixar (uma vez)
curl -sL "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -o /tmp/nuget.exe

# Restaurar
/tmp/nuget.exe restore Ciclo.sln
```

O `nuget.exe` standalone tem seu próprio runtime NuGet, não usa o `NuGet.Configuration.dll` do SDK.

### Build: MSBuild do Visual Studio

```bash
MSBUILD="C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/bin/MSBuild.exe"

# Build
"$MSBUILD" Ciclo.sln -t:Build -p:Configuration=Debug -v:minimal
```

O MSBuild do VS é .NET Framework (não .NET Core), não sofre do bug do SDK. **Flags usam `-` (não `/`)**: `-t:Build`, `-p:Configuration=Debug`, `-v:minimal`.

### Testes: `dotnet test --no-build`

```bash
dotnet test --no-build
```

Funciona porque só executa DLLs já compiladas, sem tocar no NuGet.

### Migration EF Core

O `dotnet ef` não está instalado e `dotnet tool install` depende do NuGet. Solução: criar a migration manualmente (o código C# é determinístico baseado nas entidades).

### Fluxo completo de trabalho

```bash
# 1. Se adicionar/remover pacotes NuGet: editar .csproj, depois:
/tmp/nuget.exe restore Ciclo.sln

# 2. Build
"$MSBUILD" Ciclo.sln -t:Build -p:Configuration=Debug -v:minimal

# 3. Testes
dotnet test --no-build
```

**Takeaway:** Sempre verificar se `dotnet restore` funciona ANTES de tentar buildar. Se falhar com `Value cannot be null`, não insistir — usar imediatamente o `nuget.exe` + `MSBuild.exe` do VS. Este padrão deve ser reutilizado em TODAS as specs até que o SDK seja atualizado para uma versão com NuGet estável (10.0.303+).

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
