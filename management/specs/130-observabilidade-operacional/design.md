# Spec 130: Observabilidade Operacional — Design

## Design Approach

A entrega se apoia em três pilares independentes, cada um com um dono único:

1. **Saúde** — dois endpoints com semânticas distintas (liveness sem dependências, readiness com banco e disco), apoiados em `IHealthCheck` do próprio framework.
2. **Correlação** — resolvida **uma vez** na borda (`CorrelationMiddleware`), gravada em `HttpContext.Items`, e consumida por três leitores: auditoria, log e `ProblemDetails`.
3. **Métricas** — `prometheus-net` para as métricas HTTP e um gauge da fila lido do próprio `Channel` no momento da coleta.

A ordem dos middlewares é parte do contrato: a correlação precisa existir antes de qualquer código que a leia, e o request logging do Serilog precisa enriquecer o evento de conclusão lendo `HttpContext.Items`.

Nenhuma mudança de schema. Nenhum endpoint existente muda de contrato.

## Architecture Decisions

### AD-130-01 — Métricas por scrape Prometheus, sem collector

**Decisão:** usar `prometheus-net.AspNetCore` e expor `/metrics` no formato de texto do Prometheus.

**Alternativas descartadas:**
- **OTLP/OpenTelemetry:** exigiria um collector (ou backend) para produzir valor; em projeto acadêmico, seria infraestrutura sem consumidor.
- **Instrumentação própria com `System.Diagnostics.Metrics`:** mais código para reimplementar o que o pacote entrega, e sem formato de texto pronto para scrape.

**Consequência:** trocar para OTLP no futuro exige reinstrumentar. Aceito porque as métricas são poucas e os nomes são estáveis.

### AD-130-02 — E-mail fora do readiness

**Decisão:** readiness cobre `db` e `storage`. A fila de e-mail é métrica, não check.

**Motivo:** o canal é `ConsoleEmailSender` (inerte por decisão da Spec 80). Incluir “e-mail” no readiness deixaria o serviço permanentemente `Unhealthy` em produção até existir provedor real — um alerta permanente é ruído, não sinal. Quando o provedor existir, o check pode ser adicionado com a mesma porta `IEmailQueueStatus`.

### AD-130-03 — Liveness não toca dependências

**Decisão:** `/health/live` responde 200 sem resolver `AppDbContext`, disco ou fila.

**Motivo:** se o liveness falhar quando o banco cai, o orquestrador reinicia um processo saudável em laço. A distinção é o que permite reiniciar de verdade um processo travado.

### AD-130-04 — Correlação resolvida uma vez

**Decisão:** `CorrelationMiddleware` é o único resolvedor. `AuditContextMiddleware` lê `Items` e usa `AuditCorrelation.Resolve` apenas como fallback defensivo (testes unitários que invocam o middleware isolado).

**Motivo:** três resolvedores independentes produzem três identificadores para o mesmo incidente. A função de resolução continua sendo `AuditCorrelation.Resolve`, já coberta por testes da Spec 110.

### AD-130-05 — `correlationId` por filtro, não em 43 chamadas

**Decisão:** um `IActionFilter` global adiciona a extensão quando o resultado é `ObjectResult` com `ProblemDetails`. `ProblemResponse.WriteAsync` adiciona a extensão diretamente; `FromCode` permanece com a assinatura atual.

**Motivo:** mudar `FromCode` para receber `HttpContext` obrigaria a tocar todos os controllers e reintroduziria a duplicação que o M3 eliminou. O filtro roda exatamente onde o `ObjectResult` é produzido.

### AD-130-06 — Gauge da fila lido na coleta, sem contador paralelo

**Decisão:** `ciclo_email_queue_depth` é atualizado por um `AddBeforeCollectCallback` que lê `_channel.Reader.Count` através de `IEmailQueueStatus`.

**Motivo:** um contador incrementado no enfileiramento e decrementado no consumo diverge sempre que uma operação falha no meio. Ler o estado no scrape é O(1) e não tem estado próprio.

### AD-130-07 — Limiares de alerta como documentação

**Decisão:** a spec entrega limiares objetivos no README; nenhuma integração com Slack/Alertmanager.

**Motivo:** sem destino definido, “alerta” não é implementável. A regra objetiva é o que evita critérios inventados por operador.

## Component / Module Breakdown

### Component: `AuditCorrelation` (Core, alteração)

Acrescentar a constante da chave de item, para que resolvedor e leitores usem a mesma string:

```csharp
public const string ItemsKey = "CorrelationId";
```

Nada mais muda: `HeaderName`, `MaxLength` e `Resolve` permanecem como estão.

### Component: `CorrelationMiddleware` (Api, novo)

`src/Ciclo.Api/Middleware/CorrelationMiddleware.cs`

```csharp
using Ciclo.Core.Auditing;
using Serilog.Context;

namespace Ciclo.Api.Middleware;

/// <summary>
/// Resolve a correlação uma única vez por request e a disponibiliza para todo o pipeline:
/// HttpContext.Items (auditoria e ProblemDetails), cabeçalho de resposta e LogContext.
/// </summary>
public sealed class CorrelationMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = AuditCorrelation.Resolve(
            context.Request.Headers[AuditCorrelation.HeaderName].ToString(),
            context.TraceIdentifier);

        context.Items[AuditCorrelation.ItemsKey] = correlationId;
        context.Response.Headers[AuditCorrelation.HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
```

### Component: `AuditContextMiddleware` (Api, alteração)

Passa a consumir o item; deixa de escrever o cabeçalho (o dono agora é o `CorrelationMiddleware`).

```csharp
var correlationId = context.Items[AuditCorrelation.ItemsKey] as string
    ?? AuditCorrelation.Resolve(
        context.Request.Headers[AuditCorrelation.HeaderName].ToString(),
        context.TraceIdentifier);

actor.SetCorrelationId(correlationId);
```

A linha `context.Response.Headers[AuditCorrelation.HeaderName] = correlationId;` é removida deste middleware.

### Component: `ProblemResponse` (Api, alteração)

```csharp
internal const string CorrelationExtension = "correlationId";

internal static Task WriteAsync(HttpContext context, int status, string code, string title)
{
    var problem = new ProblemDetails { Status = status, Title = title };
    problem.Extensions[CodeExtension] = code;

    if (context.Items[AuditCorrelation.ItemsKey] is string correlationId
        && !string.IsNullOrWhiteSpace(correlationId))
    {
        problem.Extensions[CorrelationExtension] = correlationId;
    }

    context.Response.StatusCode = status;
    return context.Response.WriteAsJsonAsync(problem, options: null, contentType: ContentType);
}
```

`FromCode` não muda.

### Component: `ProblemDetailsCorrelationFilter` (Api, novo)

`src/Ciclo.Api/Filters/ProblemDetailsCorrelationFilter.cs`

```csharp
using Ciclo.Api.Middleware;
using Ciclo.Core.Auditing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ciclo.Api.Filters;

/// <summary>
/// Acrescenta correlationId a toda resposta ProblemDetails produzida por controller.
/// Roda antes da serialização, então não exige tocar os chamadores de ProblemResponse.FromCode.
/// </summary>
public sealed class ProblemDetailsCorrelationFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Result is ObjectResult { Value: ProblemDetails problem }
            && context.HttpContext.Items[AuditCorrelation.ItemsKey] is string correlationId
            && !string.IsNullOrWhiteSpace(correlationId))
        {
            problem.Extensions[ProblemResponse.CorrelationExtension] = correlationId;
        }
    }
}
```

### Component: `IEmailQueueStatus` (Core, novo)

`src/Ciclo.Core/Interfaces/IEmailQueueStatus.cs`

```csharp
namespace Ciclo.Core.Interfaces;

/// <summary>Estado observável da fila de envio de e-mail.</summary>
public interface IEmailQueueStatus
{
    int Depth { get; }
    int Capacity { get; }
}
```

### Component: `ChannelEmailDispatcher` (Infrastructure, alteração)

- Renomear a constante `Capacity` para `QueueCapacity` (evita colisão com a propriedade da interface).
- Implementar `IEmailQueueStatus`:

```csharp
public int Depth => _channel.Reader.Count;
public int Capacity => QueueCapacity;
```

- Manter `TryWrite` e o log de descarte como estão.

### Component: `EmailQueueMetrics` (Infrastructure, novo)

`src/Ciclo.Infrastructure/Email/EmailQueueMetrics.cs`

```csharp
using Ciclo.Core.Interfaces;
using Prometheus;

namespace Ciclo.Infrastructure.Email;

/// <summary>
/// Publica a profundidade e a capacidade da fila no registro do Prometheus.
/// A leitura acontece na coleta (callback), não em contador paralelo.
/// </summary>
internal sealed class EmailQueueMetrics
{
    private readonly IEmailQueueStatus _status;
    private readonly Gauge _depth;
    private readonly Gauge _capacity;

    public EmailQueueMetrics(IEmailQueueStatus status)
    {
        _status = status;

        _depth = Metrics.CreateGauge(
            "ciclo_email_queue_depth", "Mensagens de e-mail aguardando envio.");
        _capacity = Metrics.CreateGauge(
            "ciclo_email_queue_capacity", "Capacidade do canal de e-mail.");

        Metrics.DefaultRegistry.AddBeforeCollectCallback(Collect);
    }

    private void Collect()
    {
        _depth.Set(_status.Depth);
        _capacity.Set(_status.Capacity);
    }
}
```

Registro em `EmailDispatchSetup`:

```csharp
services.AddSingleton<IEmailQueueStatus>(sp => (IEmailQueueStatus)sp.GetRequiredService<IEmailDispatcher>());
services.AddSingleton<EmailQueueMetrics>();
```

### Component: `DatabaseHealthCheck` (Api, novo)

`src/Ciclo.Api/Health/DatabaseHealthCheck.cs`

```csharp
using Ciclo.Infrastructure.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ciclo.Api.Health;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseHealthCheck(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            return await db.Database.CanConnectAsync(timeout.Token)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("database_unreachable");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("database_timeout");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("database_error", exception);
        }
    }
}
```

O `IServiceScopeFactory` é usado porque `AppDbContext` é scoped e o health check é resolvido como singleton pelo registro de health checks; injetar o contexto diretamente criaria uma dependência de vida inválida.

### Component: `StorageHealthCheck` (Api, novo)

`src/Ciclo.Api/Health/StorageHealthCheck.cs`

```csharp
using Ciclo.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ciclo.Api.Health;

public sealed class StorageHealthCheck : IHealthCheck
{
    private const string ProbeFileName = "health-probe.tmp";

    private readonly string _rootPath;

    public StorageHealthCheck(IOptions<FileStorageOptions> options)
        => _rootPath = Path.GetFullPath(options.Value.RootPath);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var probePath = Path.Combine(_rootPath, ProbeFileName);

        try
        {
            Directory.CreateDirectory(_rootPath);
            await File.WriteAllBytesAsync(probePath, [1], cancellationToken);
            File.Delete(probePath);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("storage_unavailable", exception);
        }
    }
}
```

A constante `ProbeFileName` contém `health-probe`, citado no requisito.

### Component: `HealthResponseWriter` (Api, novo)

`src/Ciclo.Api/Health/HealthResponseWriter.cs`

```csharp
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ciclo.Api.Health;

public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Status.ToString())
        };

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, Options));
    }
}
```

Liveness usa o mesmo writer com relatório vazio: `Predicate = _ => false`.

### Component: `Program` (Api, alterações)

**Pacote** (`src/Ciclo.Api/Ciclo.Api.csproj`):

```xml
<PackageReference Include="prometheus-net.AspNetCore" Version="8.2.1" />
```

**Serviços:**

```csharp
builder.Services.Configure<FileStorageOptions>(
    builder.Configuration.GetSection(FileStorageOptions.SectionName));

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("db", tags: ["ready"])
    .AddCheck<StorageHealthCheck>("storage", tags: ["ready"]);
```

`AddController` passa a registrar o filtro:

```csharp
builder.Services.AddControllers(options =>
    options.Filters.Add<ProblemDetailsCorrelationFilter>());
```

**Serilog:**

```csharp
builder.Host.UseSerilog((context, services, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}",
            formatProvider: CultureInfo.InvariantCulture));
```

**Pipeline (ordem exata a partir de `app.UseSerilogRequestLogging()`):**

```csharp
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnostic, http) =>
    {
        if (http.Items[AuditCorrelation.ItemsKey] is string correlationId)
        {
            diagnostic.Set("CorrelationId", correlationId);
        }
    };
});

app.UseMiddleware<CorrelationMiddleware>();

// Rate limiting, autenticação, tenant, auditoria e autorização seguem como estão.
```

**Métricas e saúde, antes de `app.MapControllers()`:**

```csharp
app.UseHttpMetrics();

app.MapMetrics("/metrics");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});
```

## Data Flow

### Requisição com correlação

```
cliente ── X-Correlation-Id: abc ──► CorrelationMiddleware
                                      ├─ Items["CorrelationId"] = abc
                                      ├─ Response.Headers["X-Correlation-Id"] = abc
                                      ├─ LogContext("CorrelationId") = abc
                                      ├─► AuditContextMiddleware (lê Items → AuditActor)
                                      ├─► controller (auditoria grava CorrelationId)
                                      └─► log de conclusão do Serilog (enriquecido de Items)
```

### Erro de domínio

```
controller → ProblemResponse.FromCode(code, status) → ObjectResult(ProblemDetails)
           → ProblemDetailsCorrelationFilter acrescenta correlationId
           → resposta application/problem+json
```

### Erro de framework (401/403)

```
JwtBearer.OnChallenge → ProblemResponse.WriteAsync(context, 401, "unauthorized", "unauthorized")
                       → lê Items e inclui correlationId
```

### Readiness

```
GET /health/ready
  ├─ DatabaseHealthCheck: escopo → CanConnectAsync (timeout 2s)
  ├─ StorageHealthCheck: cria dir → escreve health-probe.tmp → apaga
  └─ HealthResponseWriter: {"status":"Unhealthy","checks":{"db":"Healthy","storage":"Unhealthy"}} (200 ou 503)
```

### Métricas

```
GET /metrics
  ├─ http_requests_received_total{meth,route,code}
  ├─ http_request_duration_seconds
  ├─ ciclo_email_queue_depth  ← Reader.Count no callback de coleta
  └─ ciclo_email_queue_capacity
```

## Data Structures

- **Chave de correlação:** `AuditCorrelation.ItemsKey = "CorrelationId"` (string constante).
- **Health payload:** `{ "status": "Healthy|Degraded|Unhealthy", "checks": { "<nome>": "<status>" } }`.
- **Métricas:** nomes fixos acima; rótulos apenas `method`, `route`, `code` (fornecidos pelo `UseHttpMetrics`).
- **ProblemDetails:** extensões `code` (existente) e `correlationId` (nova).

## File / Module Layout

| Caminho | Ação |
|---|---|
| `src/Ciclo.Api/Middleware/CorrelationMiddleware.cs` | criar |
| `src/Ciclo.Api/Middleware/AuditContextMiddleware.cs` | alterar |
| `src/Ciclo.Api/Middleware/ProblemResponse.cs` | alterar |
| `src/Ciclo.Api/Filters/ProblemDetailsCorrelationFilter.cs` | criar |
| `src/Ciclo.Api/Health/DatabaseHealthCheck.cs` | criar |
| `src/Ciclo.Api/Health/StorageHealthCheck.cs` | criar |
| `src/Ciclo.Api/Health/HealthResponseWriter.cs` | criar |
| `src/Ciclo.Core/Auditing/AuditCorrelation.cs` | alterar |
| `src/Ciclo.Core/Interfaces/IEmailQueueStatus.cs` | criar |
| `src/Ciclo.Infrastructure/Email/EmailDispatcher.cs` | alterar |
| `src/Ciclo.Infrastructure/Email/EmailQueueMetrics.cs` | criar |
| `src/Ciclo.Infrastructure/Email/EmailDispatchSetup.cs` | alterar |
| `src/Ciclo.Api/Program.cs` | alterar |
| `src/Ciclo.Api/Ciclo.Api.csproj` | alterar (pacote) |
| `README.md` | alterar (limiares e endpoints) |
| `management/adr.md` | alterar (ADR-005) |
| `tests/Ciclo.Api.Tests/CorrelationTests.cs` | criar |
| `tests/Ciclo.Api.Tests/EmailQueueStatusTests.cs` | criar |
| `tests/Ciclo.Api.Tests/HealthCheckTests.cs` | criar |
| `tests/Ciclo.Api.Tests/ObservabilityEndpointTests.cs` | criar |

## Testing Strategy

| Cenário | Tipo | Asserção |
|---|---|---|
| Cabeçalho válido é preservado | unitário (`DefaultHttpContext`) | `Items` e cabeçalho de resposta iguais ao enviado |
| Cabeçalho ausente | unitário | valor gerado não vazio, igual ao `TraceIdentifier` |
| Cabeçalho com 200 caracteres | unitário | valor descartado; `TraceIdentifier` usado |
| Filtro com `ProblemDetails` | unitário | extensão `correlationId` presente e igual ao item |
| Filtro sem item | unitário | extensão ausente |
| `WriteAsync` com item | unitário (resposta em memória) | corpo contém `correlationId` |
| Storage saudável | unitário (diretório temporário) | `Healthy`; arquivo-sonda removido |
| Storage inválido | unitário (raiz apontando para um arquivo) | `Unhealthy` |
| Banco no InMemory | unitário | `Healthy` |
| Profundidade da fila | unitário | 3 enfileirados sem worker → `Depth == 3`, `Capacity == 1000` |
| `/health/live` | integração (`ApiTestHost`) | 200 e `{"status":"Healthy"}` |
| `/health/ready` | integração | 200 com `db` e `storage` saudáveis |
| `/metrics` | integração | 200, contém `ciclo_email_queue_depth` e `http_requests_received_total` |
| `/Health` e `/Health/db` | integração | continuam 200 (FR-010) |
| Correção de log | inspeção manual | template exibe `[<id>]` em requisição com cabeçalho |

`ApiTestHost` já injeta os segredos obrigatórios; para o readiness é preciso configurar `FileStorage:RootPath` com um diretório temporário via `builder.UseSetting`.

## Migration / Rollback

**Migration:** nenhuma. A spec não altera schema nem dados.

**Rollback:** remover a referência ao pacote, os endpoints de saúde/métricas e o filtro de correlação; restaurar o `AuditContextMiddleware` ao estado anterior (resolver próprio e escrita do cabeçalho). Não há dado persistido a desfazer.

**Riscos de implantação:**
- Host configurado para usar `/health` continua funcionando (FR-010); migrar para `/health/live` exige atualizar a configuração do host, não o código.
- `/metrics` anônimo: em rede pública, restringir no proxy é responsabilidade do ambiente; o endpoint não expõe dado pessoal.
- Se o diretório de storage for somente-leitura, o readiness passa a reportar `Unhealthy` — o que é o comportamento desejado, mas é uma mudança visível para quem só usava `/Health`.
