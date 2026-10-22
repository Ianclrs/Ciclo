# Spec 110: Auditoria de Operações — Design

## Design Approach

A trilha de auditoria é implementada como **um fato de domínio persistido na mesma unidade de trabalho da operação que o produziu**. Não há interceptores de EF Core, eventos de domínio, fila ou barramento: a operação sensível chama `IAuditTrail.Record(entry)`, que apenas adiciona uma entidade ao `AppDbContext` já usado pelo serviço; o `SaveChangesAsync` existente confirma estado e auditoria juntos. Se a auditoria falhar, a operação falha — fail-closed.

A identidade do ator e a origem da requisição são capturadas uma única vez na borda HTTP (`AuditContextMiddleware` → `AuditActor`) e lidas pela porta `IAuditActor`. Os serviços recebem `IAuditTrail` por injeção de construtor, o que torna a dependência explícita e testável.

Imutabilidade é garantida em duas camadas com a mesma regra: aplicação (`AppDbContext.GuardAuditImmutability`) e banco (função/gatilhos PostgreSQL). O piso de 90 dias é o elo entre elas: a retenção configurável nunca pode ser menor que o piso, e o gatilho permite `DELETE` apenas fora do piso — exatamente o que o expurgo faz.

A leitura é projeção direta para DTO (`Select`), sem carregar entidades e sem navegações; o filtro global de tenant já existente é a única barreira necessária de isolamento.

## Architecture Decisions

### AD-110-01 — Vocabulário de ações como constantes de string

`AuditActions` e `AuditTargetTypes` são classes estáticas de constantes `string`, não `enum`. Motivo: o valor precisa ser legível em SQL, em exportação CSV e em consulta por filtro textual. Um `enum` persistido como `int` exigiria conversão em toda leitura externa e tornaria o arquivo exportado dependente do código. Os valores seguem o formato `<recurso>.<evento>` em minúsculas (ex.: `enrollment.approved`) e são contrato estável: renomear um valor é quebra de contrato.

### AD-110-02 — Um único ponto de escrita, sem interceptor

A escrita vive em `AuditTrail` (implementação de `IAuditTrail`). Não há interceptor porque o evento de auditoria não é derivável genericamente do estado do EF: `Before` e `After` são snapshots explícitos escolhidos pelo serviço, e a ação depende da intenção (`approve` vs `reject`), não do diff de colunas. Interceptor produziria diff de colunas e perderia a intenção — além de acoplar a auditoria ao mapeamento relacional.

### AD-110-03 — Contexto do ator é scoped, preenchido na borda

`IAuditActor` é resolvido por requisição. IP e user-agent só existem no `HttpContext`; passá-los como parâmetro por todas as camadas espalharia um detalhe de transporte pelo domínio. O contexto é preenchido por middleware e lido pela porta de escrita. Em execução fora de HTTP (testes, seeders), o ator fica vazio e o evento é gravado com `ActorUserId` nulo — ator desconhecido é um fato registrado, não um erro.

### AD-110-04 — IP do socket, nunca de cabeçalho encaminhado

`IpAddress` vem de `HttpContext.Connection.RemoteIpAddress`. Cabeçalhos `X-Forwarded-For` e equivalentes são ignorados de propósito: são controlados pelo cliente e permitiriam forjar a origem do evento, anulando seu valor probatório. Quando houver proxy reverso confiável, a spec que o introduzir deve definir a lista de proxies confiáveis — não esta.

### AD-110-05 — Piso de imutabilidade de 90 dias compartilhado pelas duas camadas

`AuditSettings.MinimumRetentionDays = 90` é a fonte única na aplicação. A migration contém o literal `interval '90 days'`, e um teste amarra os dois valores (ver Testing Strategy). O expurgo só remove eventos mais antigos que `RetentionDays`, que nunca é menor que 90 — logo o expurgo nunca tenta remover algo que a guarda bloqueia.

### AD-110-06 — Retenção e base legal (ADR-004)

A decisão de retenção e base legal é registrada em `management/adr.md` como **ADR-004** e é a autoridade desta seção:

- prazo padrão de **365 dias**, com faixa permitida de **90 a 3650 dias**;
- piso de imutabilidade de **90 dias**, nas duas camadas;
- finalidade: defesa da escola em auditorias escolares e responsabilização por decisões administrativas (aprovação, rejeição, cancelamento, verificação documental, revogação de sessão);
- base legal: LGPD, Art. 7º, incisos II (cumprimento de obrigação legal ou regulatória) e IX (legítimo interesse), e Art. 16 (conservação pelo prazo necessário à finalidade), com minimização — o evento guarda apenas o necessário e não guarda credenciais.

### AD-110-07 — FK para `User` ausente de propósito

`AuditEvent.ActorUserId` é `Guid?` sem chave estrangeira para `Users`. Um evento precisa sobreviver à remoção ou desativação do usuário; uma FK com `Restrict` impediria a remoção e com `SetNull` apagaria o ator — ambos degradam a prova. O identificador permanece consultável.

### AD-110-08 — Exportação falha em vez de truncar

`MaxExportRows = 10000`. Acima disso a API responde `400 export_exceeds_limit` em vez de gerar arquivo parcial. Um arquivo truncado seria evidência falsa por omissão; falhar obriga o operador a estreitar o filtro.

## Component / Module Breakdown

### Component: `AuditEvent` (Core, entidade)

**Purpose:** Representar um fato auditado, tenant-scoped e imutável por convenção de camada.
**Interface:** entidade com as propriedades abaixo.
**Internal Logic:** nenhuma; é um registro de dados.
**Dependencies:** `Ciclo.Core.Interfaces.ITenantScoped`.
**Error Handling:** não aplicável.

```csharp
namespace Ciclo.Core.Entities;

public class AuditEvent : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public Guid? ActorUserId { get; set; }
    public string? ActorRole { get; set; }
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
}
```

### Component: `AuditActions` e `AuditTargetTypes` (Core, vocabulário)

**Purpose:** Dono único dos nomes de ação e de tipo de alvo.
**Interface:** constantes `public const string`.
**Internal Logic:** nenhuma.
**Dependencies:** nenhuma.
**Error Handling:** não aplicável.

```csharp
namespace Ciclo.Core.Auditing;

public static class AuditActions
{
    public const string EnrollmentApproved = "enrollment.approved";
    public const string EnrollmentRejected = "enrollment.rejected";
    public const string EnrollmentCancelled = "enrollment.cancelled";
    public const string DocumentVerified = "document.verified";
    public const string DocumentRejected = "document.rejected";
    public const string SessionRevoked = "session.revoked";
}

public static class AuditTargetTypes
{
    public const string Enrollment = "Enrollment";
    public const string Document = "Document";
    public const string Session = "Session";
}
```

### Component: `AuditEntry`, `IAuditTrail`, `IAuditActor` (Core, contratos)

**Purpose:** Definir a porta de escrita, o payload de entrada e o contexto do ator sem depender de ASP.NET Core.
**Interface:** exatamente como abaixo.
**Internal Logic:** nenhuma (contratos).
**Dependencies:** nenhuma.
**Error Handling:** documentado em cada implementação.

```csharp
namespace Ciclo.Core.Auditing;

public sealed record AuditEntry(
    string Action,
    string TargetType,
    Guid TargetId,
    object? Before = null,
    object? After = null);
```

```csharp
using Ciclo.Core.Auditing;

namespace Ciclo.Core.Interfaces;

public interface IAuditTrail
{
    void Record(AuditEntry entry);
}
```

```csharp
namespace Ciclo.Core.Interfaces;

public interface IAuditActor
{
    Guid? UserId { get; }
    string? Role { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
}
```

### Component: `AuditCorrelation` (Core, função pura)

**Purpose:** Resolver o identificador de correlação a partir de candidato não confiável.
**Interface:** `public static string Resolve(string? candidate, string traceIdentifier)`.
**Internal Logic:** se `candidate` não for nulo/vazio e casar com `^[A-Za-z0-9._:-]{8,64}$`, devolve-o; caso contrário devolve `traceIdentifier` truncado em 64 caracteres.
**Dependencies:** `System.Text.RegularExpressions` com `[GeneratedRegex]` (evita SYSLIB1045 sob `TreatWarningsAsErrors`).
**Error Handling:** nunca lança; entrada nula ou inválida cai no `traceIdentifier`.

```csharp
using System.Text.RegularExpressions;

namespace Ciclo.Core.Auditing;

public static partial class AuditCorrelation
{
    public const string HeaderName = "X-Correlation-Id";
    public const int MaxLength = 64;

    [GeneratedRegex("^[A-Za-z0-9._:-]{8,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedPattern();

    public static string Resolve(string? candidate, string traceIdentifier)
    {
        if (!string.IsNullOrWhiteSpace(candidate) && AllowedPattern().IsMatch(candidate))
            return candidate;

        var fallback = traceIdentifier ?? string.Empty;
        return fallback.Length <= MaxLength ? fallback : fallback[..MaxLength];
    }
}
```

### Component: `AuditImmutabilityException` (Core, exceção)

**Purpose:** Sinalizar tentativa de alterar ou remover evento protegido.
**Interface:** `public class AuditImmutabilityException : InvalidOperationException` com construtor de mensagem.
**Internal Logic:** nenhuma.
**Dependencies:** nenhuma.
**Error Handling:** propagada; não é capturada por nenhum `catch` do projeto.

### Component: `AuditActor` (Infrastructure, contexto scoped)

**Purpose:** Armazenar o ator e a origem da requisição corrente.
**Interface:** implementa `IAuditActor`; expõe `Capture(Guid? userId, string? role, string? ipAddress, string? userAgent)` e `SetCorrelationId(string correlationId)`.
**Internal Logic:** truncar `Role` em 32, `IpAddress` em 64 e `UserAgent` em 512; valores vazios viram `null`.
**Dependencies:** `Ciclo.Core.Interfaces.IAuditActor`.
**Error Handling:** não lança.

```csharp
namespace Ciclo.Infrastructure.Auditing;

public sealed class AuditActor : IAuditActor
{
    public const int MaxRoleLength = 32;
    public const int MaxIpAddressLength = 64;
    public const int MaxUserAgentLength = 512;

    public Guid? UserId { get; private set; }
    public string? Role { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? CorrelationId { get; private set; }

    public void Capture(Guid? userId, string? role, string? ipAddress, string? userAgent)
    {
        UserId = userId;
        Role = Truncate(role, MaxRoleLength);
        IpAddress = Truncate(ipAddress, MaxIpAddressLength);
        UserAgent = Truncate(userAgent, MaxUserAgentLength);
    }

    public void SetCorrelationId(string correlationId) => CorrelationId = correlationId;

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
```

### Component: `AuditTrail` (Infrastructure, escrita)

**Purpose:** Traduzir `AuditEntry` em `AuditEvent` e adicioná-lo à unidade de trabalho corrente.
**Interface:** `public sealed class AuditTrail : IAuditTrail` com `Record(AuditEntry entry)`.
**Internal Logic:**
1. `ArgumentNullException.ThrowIfNull(entry)`.
2. `Action` ou `TargetType` vazios → `ArgumentException`.
3. `ITenantContext.IsResolved == false` → `TenantNotResolvedException("audit_event_requires_resolved_tenant")`.
4. Serializar `Before`/`After` com `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }`.
5. JSON com mais de `MaxPayloadCharacters = 8192` → `InvalidOperationException` com o tamanho observado.
6. `_db.AuditEvents.Add(new AuditEvent)` com `TenantId` do contexto, `OccurredAt = DateTime.UtcNow` e os campos do ator preenchidos no inicializador de objeto.
7. Não chamar `SaveChangesAsync`.

**Dependencies:** `AppDbContext`, `ITenantContext`, `IAuditActor`.
**Error Handling:** todas as falhas propagam; nenhuma exceção é capturada.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Infrastructure.Auditing;

public sealed class AuditTrail : IAuditTrail
{
    internal const int MaxPayloadCharacters = 8192;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditActor _actor;

    public AuditTrail(AppDbContext db, ITenantContext tenantContext, IAuditActor actor)
    {
        _db = db;
        _tenantContext = tenantContext;
        _actor = actor;
    }

    public void Record(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.Action))
            throw new ArgumentException("audit action is required", nameof(entry));

        if (string.IsNullOrWhiteSpace(entry.TargetType))
            throw new ArgumentException("audit target type is required", nameof(entry));

        if (!_tenantContext.IsResolved)
            throw new TenantNotResolvedException("audit_event_requires_resolved_tenant");

        _db.AuditEvents.Add(new AuditEvent
        {
            TenantId = _tenantContext.TenantId,
            OccurredAt = DateTime.UtcNow,
            ActorUserId = _actor.UserId,
            ActorRole = _actor.Role,
            Action = entry.Action,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            BeforeJson = Serialize(entry.Before, nameof(entry.Before)),
            AfterJson = Serialize(entry.After, nameof(entry.After)),
            IpAddress = _actor.IpAddress,
            UserAgent = _actor.UserAgent,
            CorrelationId = _actor.CorrelationId
        });
    }

    private static string? Serialize(object? value, string parameterName)
    {
        if (value is null) return null;

        var json = JsonSerializer.Serialize(value, JsonOptions);
        if (json.Length > MaxPayloadCharacters)
        {
            throw new InvalidOperationException(
                $"audit payload exceeds {MaxPayloadCharacters} characters (observed {json.Length}) for {parameterName}.");
        }

        return json;
    }
}
```

### Component: `AppDbContext` (Infrastructure, modelo e guarda)

**Purpose:** Persistir `AuditEvent`, aplicar o filtro global de tenant e impedir mutação de eventos.
**Interface:** `DbSet<AuditEvent> AuditEvents`; blocos em `OnModelCreating`; `GuardAuditImmutability()` chamado nas duas substituições existentes de `SaveChangesAsync`.
**Internal Logic:** o filtro global já é aplicado por reflexão sobre `ITenantScoped`; `AuditEvent` passa a ser incluído automaticamente. A guarda percorre `ChangeTracker.Entries<AuditEvent>()`.
**Dependencies:** `AuditSettings.MinimumRetentionDays`.
**Error Handling:** `AuditImmutabilityException` com os códigos `audit_events_are_append_only` e `audit_events_within_the_retention_floor_cannot_be_deleted`.

Bloco de configuração a inserir em `OnModelCreating` (antes do laço de filtro global):

```csharp
builder.Entity<AuditEvent>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Action).HasMaxLength(64).IsRequired();
    entity.Property(e => e.TargetType).HasMaxLength(32).IsRequired();
    entity.Property(e => e.ActorRole).HasMaxLength(32);
    entity.Property(e => e.IpAddress).HasMaxLength(64);
    entity.Property(e => e.UserAgent).HasMaxLength(512);
    entity.Property(e => e.CorrelationId).HasMaxLength(64);
    entity.Property(e => e.BeforeJson).HasColumnType("jsonb");
    entity.Property(e => e.AfterJson).HasColumnType("jsonb");
    entity.Property(e => e.OccurredAt).HasDefaultValueSql("now()");

    entity.HasIndex(e => new { e.TenantId, e.OccurredAt });
    entity.HasIndex(e => new { e.TenantId, e.Action, e.OccurredAt });
    entity.HasIndex(e => new { e.TenantId, e.TargetType, e.TargetId });

    entity.HasOne<Tenant>()
        .WithMany()
        .HasForeignKey(e => e.TenantId)
        .OnDelete(DeleteBehavior.Restrict);
});
```

Guarda:

```csharp
private void GuardAuditImmutability()
{
    foreach (var entry in ChangeTracker.Entries<AuditEvent>())
    {
        if (entry.State == EntityState.Modified)
            throw new AuditImmutabilityException("audit_events_are_append_only");

        if (entry.State == EntityState.Deleted &&
            entry.Entity.OccurredAt > DateTime.UtcNow.AddDays(-AuditSettings.MinimumRetentionDays))
        {
            throw new AuditImmutabilityException(
                "audit_events_within_the_retention_floor_cannot_be_deleted");
        }
    }
}
```

As duas substituições passam a ser:

```csharp
public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    AutoSetTenantId();
    GuardAuditImmutability();
    return base.SaveChangesAsync(cancellationToken);
}

public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
    CancellationToken cancellationToken = default)
{
    AutoSetTenantId();
    GuardAuditImmutability();
    return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
}
```

### Component: `AuditSettings` (Infrastructure, configuração)

**Purpose:** Ler e validar a política de retenção.
**Interface:** `public static AuditSettings Read(IConfiguration configuration)`; propriedade `RetentionDays`.
**Internal Logic:** chave `Audit:RetentionDays`; ausente → `DefaultRetentionDays = 365`; não inteiro, menor que `MinimumRetentionDays = 90` ou maior que `MaximumRetentionDays = 3650` → `InvalidOperationException` com a faixa permitida na mensagem.
**Dependencies:** `Microsoft.Extensions.Configuration`.
**Error Handling:** fail-closed na inicialização.

```csharp
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Ciclo.Infrastructure.Auditing;

public sealed record AuditSettings(int RetentionDays)
{
    public const string RetentionDaysKey = "Audit:RetentionDays";
    public const int DefaultRetentionDays = 365;
    public const int MinimumRetentionDays = 90;
    public const int MaximumRetentionDays = 3650;

    public static AuditSettings Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var raw = configuration[RetentionDaysKey];
        var retentionDays = DefaultRetentionDays;

        if (!string.IsNullOrWhiteSpace(raw) &&
            !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out retentionDays))
        {
            throw new InvalidOperationException(
                $"{RetentionDaysKey} must be an integer; the configured value is '{raw}'.");
        }

        if (retentionDays < MinimumRetentionDays || retentionDays > MaximumRetentionDays)
        {
            throw new InvalidOperationException(
                $"{RetentionDaysKey} must be between {MinimumRetentionDays} and {MaximumRetentionDays} days; " +
                $"the configured value is {retentionDays}.");
        }

        return new AuditSettings(retentionDays);
    }
}
```

### Component: `AuditRetentionPurger` (Infrastructure, expurgo)

**Purpose:** Remover eventos fora da retenção, em lotes, atravessando tenants de forma explícita.
**Interface:** `public async Task<int> PurgeAsync(DateTime utcNow, CancellationToken cancellationToken)`.
**Internal Logic:** `cutoff = utcNow.AddDays(-RetentionDays)`; repetir: buscar até `PurgeBatchSize = 5000` eventos com `OccurredAt < cutoff` ordenados por `OccurredAt`, `RemoveRange`, `SaveChangesAsync`, somar; parar quando o lote vier vazio ou incompleto.
**Dependencies:** `AppDbContext`, `AuditSettings`.
**Error Handling:** exceções propagam para o chamador; nenhuma captura local.

### Component: `AuditRetentionService` (Infrastructure, host)

**Purpose:** Executar o expurgo a cada 24 horas.
**Interface:** `public sealed class AuditRetentionService : BackgroundService`.
**Internal Logic:** `PeriodicTimer(TimeSpan.FromHours(24))`; a cada ciclo cria escopo via `IServiceScopeFactory`, resolve `AuditRetentionPurger` e chama `PurgeAsync(DateTime.UtcNow, stoppingToken)`; registra o total removido em `Information`.
**Dependencies:** `IServiceScopeFactory`, `ILogger<AuditRetentionService>`.
**Error Handling:** `OperationCanceledException` quando `stoppingToken.IsCancellationRequested` encerra o laço; qualquer outra exceção é registrada com `LogError` e o host continua (a próxima execução tenta novamente). Este é o único ponto autorizado a capturar exceção na pasta de auditoria.

### Component: `AuditCsvWriter` (Infrastructure, exportação)

**Purpose:** Serializar DTOs em CSV RFC 4180 determinístico.
**Interface:** `public static string Write(IReadOnlyList<AuditEventDto> events)`.
**Internal Logic:** cabeçalho fixo literal; linhas em ordem de entrada; `\r\n` como terminador; `OccurredAt` em `yyyy-MM-dd'T'HH:mm:ss.fff'Z'` com `CultureInfo.InvariantCulture`; escape apenas quando o campo contém `,`, `"`, `\n` ou `\r`, duplicando aspas internas. Não usar arrays como argumentos (evita CA1861): escrever campo a campo em `StringBuilder`.
**Dependencies:** `AuditEventDto`.
**Error Handling:** não lança para dados válidos; campos nulos viram string vazia.

Cabeçalho literal exato:

```text
OccurredAt,ActorUserId,ActorRole,Action,TargetType,TargetId,CorrelationId,IpAddress,UserAgent,BeforeJson,AfterJson
```

### Component: `AuditQueryService` (Infrastructure, leitura)

**Purpose:** Consultar e exportar eventos do tenant corrente.
**Interface:**

```csharp
public interface IAuditQueryService
{
    Task<PagedResponse<AuditEventDto>> QueryAsync(
        AuditEventFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<byte[]> ExportCsvAsync(
        AuditEventFilter filter, CancellationToken cancellationToken = default);
}
```

**Internal Logic:**
- `NormalizeRange`: `to` ausente → `DateTime.UtcNow`; `from` ausente → `to - 30 dias`; `Kind == Local` → `ToUniversalTime()`; `Kind == Unspecified` → `SpecifyKind(value, Utc)`; `to < from` → `AuditException("invalid_date_range", 400)`; intervalo > 400 dias → `AuditException("date_range_too_wide", 400)`.
- `BuildQuery`: `AsNoTracking()`, `OccurredAt >= from && OccurredAt <= to`, filtros opcionais por `Action`, `ActorUserId`, `TargetType`, `TargetId`; NÃO usa `IgnoreQueryFilters` (o filtro global de tenant é a barreira de isolamento).
- `QueryAsync`: `page < 1` → 1; `pageSize < 1` → 50; `pageSize > 200` → 200; `CountAsync`; `OrderByDescending(OccurredAt).ThenByDescending(Id)`; `Skip/Take`; projeção para DTO.
- `ExportCsvAsync`: mesma consulta ordenada por `OccurredAt` e `Id` crescentes; `CountAsync > 10000` → `AuditException("export_exceeds_limit", 400)`; `AuditCsvWriter.Write`; retorno `[.. preamble, .. body]` usando `new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)`.

**Dependencies:** `AppDbContext`, `AuditCsvWriter`, `PagedResponse<T>`.
**Error Handling:** `AuditException` com `StatusCode` para os três casos de validação; o controlador traduz para ProblemDetails.

`AuditException` (declarada no mesmo arquivo, seguindo o padrão de `EnrollmentException`/`DocumentException`):

```csharp
public sealed class AuditException : Exception
{
    public int StatusCode { get; }

    public AuditException(string message, int statusCode) : base(message) => StatusCode = statusCode;
}
```

### Component: `AuditContextMiddleware` (Api, borda)

**Purpose:** Preencher o ator/origem e ecoar a correlação.
**Interface:** `public sealed class AuditContextMiddleware` com `InvokeAsync(HttpContext context, AuditActor actor)`.
**Internal Logic:** resolver correlação; `actor.SetCorrelationId`; `actor.Capture(userId, role, ip, userAgent)`; escrever `X-Correlation-Id` na resposta; chamar `_next`.
**Dependencies:** `AuditCorrelation`, `AuditActor`.
**Error Handling:** não captura exceções; `Guid.TryParse` de claim ausente devolve falso sem lançar.

```csharp
using System.Security.Claims;
using Ciclo.Core.Auditing;
using Ciclo.Infrastructure.Auditing;

namespace Ciclo.Api.Middleware;

public sealed class AuditContextMiddleware
{
    private readonly RequestDelegate _next;

    public AuditContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AuditActor actor)
    {
        var correlationId = AuditCorrelation.Resolve(
            context.Request.Headers[AuditCorrelation.HeaderName].ToString(),
            context.TraceIdentifier);

        actor.SetCorrelationId(correlationId);
        actor.Capture(
            ResolveUserId(context.User),
            context.User.FindFirst(ClaimTypes.Role)?.Value ?? context.User.FindFirst("role")?.Value,
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Headers.UserAgent.ToString());

        context.Response.Headers[AuditCorrelation.HeaderName] = correlationId;

        await _next(context);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId) ? userId : null;
}
```

### Component: `AuditController` (Api, consulta)

**Purpose:** Expor consulta e exportação Admin-only.
**Interface:** classe com `[ApiController]`, `[Route("api/audit-events")]`, `[Authorize(Roles = "Admin")]`; ações `GetAll` e `Export`.
**Internal Logic:** montar `AuditEventFilter` a partir dos parâmetros de query; delegar ao serviço; traduzir `AuditException` em `Problem(title: ex.Message, statusCode: ex.StatusCode)` — o mesmo padrão dos demais controladores.
**Dependencies:** `IAuditQueryService`.
**Error Handling:** apenas `AuditException` é tratada; o restante segue para o middleware global.

```csharp
[ApiController]
[Route("api/audit-events")]
[Authorize(Roles = "Admin")]
public class AuditController : ControllerBase
{
    private readonly IAuditQueryService _service;

    public AuditController(IAuditQueryService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? action,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? targetType,
        [FromQuery] Guid? targetId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var filter = new AuditEventFilter(from, to, action, actorUserId, targetType, targetId);
            return Ok(await _service.QueryAsync(filter, page, pageSize));
        }
        catch (AuditException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? action,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? targetType,
        [FromQuery] Guid? targetId)
    {
        try
        {
            var filter = new AuditEventFilter(from, to, action, actorUserId, targetType, targetId);
            var bytes = await _service.ExportCsvAsync(filter);
            return File(bytes, "text/csv; charset=utf-8", "audit-events.csv");
        }
        catch (AuditException ex)
        {
            return Problem(title: ex.Message, statusCode: ex.StatusCode);
        }
    }
}
```

## Data Flow

### Fluxo de escrita (operação sensível)

```
POST /api/enrollments/{id}/approve
  → ExceptionMiddleware
  → RequestLogging → RateLimiter → Authentication
  → TenantMiddleware            (tenant_id do JWT → TenantContext.SetTenant)
  → AuditContextMiddleware      (claims/IP/UA → AuditActor; X-Correlation-Id → resposta)
  → Authorization               (Admin/Staff)
  → AuditController.Approve
  → EnrollmentService.ApproveAsync
      ├─ carrega Enrollment (filtro global de tenant aplicado)
      ├─ valida transição e documentação  → falha: nenhum evento
      ├─ previousStatus = enrollment.Status
      ├─ muta Enrollment/Student
      ├─ _audit.Record(entry)              → AppDbContext.AuditEvents.Add (sem SaveChanges)
      └─ _db.SaveChangesAsync()
            ├─ AutoSetTenantId
            ├─ GuardAuditImmutability (Added: permitido)
            └─ COMMIT: Enrollment + AuditEvent
  → 200 EnrollmentDto
```

### Fluxo de consulta

```
GET /api/audit-events?from=<inicio>&to=<fim>&action=<acao>
  → TenantMiddleware → AuditContextMiddleware → Authorization (Admin)
  → AuditController.GetAll
  → AuditQueryService.QueryAsync
      ├─ NormalizeRange (400 em intervalo inválido)
      ├─ CountAsync (filtro global de tenant + filtros)
      └─ OrderByDescending(OccurredAt).ThenByDescending(Id) + Skip/Take + Select(DTO)
  → 200 PagedResponse<AuditEventDto>
```

### Fluxo de exportação

```
GET /api/audit-events/export?from=<inicio>&to=<fim>
  → AuditController.Export
  → AuditQueryService.ExportCsvAsync
      ├─ NormalizeRange
      ├─ CountAsync > 10000 → AuditException(export_exceeds_limit, 400)
      ├─ OrderBy(OccurredAt).ThenBy(Id) + Select(DTO)
      └─ AuditCsvWriter.Write → UTF-8 com BOM
  → 200 text/csv; charset=utf-8
```

### Fluxo de expurgo

```
AuditRetentionService (a cada 24h, fora do ambiente Testing)
  → cria escopo
  → AuditRetentionPurger.PurgeAsync(DateTime.UtcNow, stoppingToken)
      ├─ cutoff = utcNow - RetentionDays (>= 90 dias)
      ├─ IgnoreQueryFilters().Where(OccurredAt < cutoff).OrderBy(OccurredAt).Take(5000)
      ├─ RemoveRange + SaveChangesAsync  (guarda permite: fora do piso)
      └─ repete até lote incompleto
  → LogInformation(total removido)
```

## Data Structures

### Tabela `"AuditEvents"`

| Coluna | Tipo EF/PostgreSQL | Restrições |
|---|---|---|
| `Id` | `uuid` | PK |
| `TenantId` | `uuid` | NOT NULL, FK → `Tenants(Id)` `ON DELETE RESTRICT` |
| `OccurredAt` | `timestamp with time zone` | NOT NULL, default `now()` |
| `ActorUserId` | `uuid` | NULL, **sem FK** |
| `ActorRole` | `varchar(32)` | NULL |
| `Action` | `varchar(64)` | NOT NULL |
| `TargetType` | `varchar(32)` | NOT NULL |
| `TargetId` | `uuid` | NOT NULL |
| `BeforeJson` | `jsonb` | NULL |
| `AfterJson` | `jsonb` | NULL |
| `IpAddress` | `varchar(64)` | NULL |
| `UserAgent` | `varchar(512)` | NULL |
| `CorrelationId` | `varchar(64)` | NULL |

Índices: `IX_AuditEvents_TenantId_OccurredAt`, `IX_AuditEvents_TenantId_Action_OccurredAt`, `IX_AuditEvents_TenantId_TargetType_TargetId`.

Gatilhos: `audit_events_no_update` (BEFORE UPDATE), `audit_events_no_delete` (BEFORE DELETE), ambos executando `audit_events_append_only_guard()`.

### Contratos de leitura

```csharp
namespace Ciclo.Infrastructure.Contracts;

public sealed record AuditEventFilter(
    DateTime? From,
    DateTime? To,
    string? Action,
    Guid? ActorUserId,
    string? TargetType,
    Guid? TargetId);

public sealed record AuditEventDto(
    Guid Id,
    DateTime OccurredAt,
    Guid? ActorUserId,
    string? ActorRole,
    string Action,
    string TargetType,
    Guid TargetId,
    string? BeforeJson,
    string? AfterJson,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId);
```

Projeção única usada por consulta e exportação:

```csharp
private static readonly Expression<Func<AuditEvent, AuditEventDto>> Projection = e =>
    new AuditEventDto(
        e.Id,
        e.OccurredAt,
        e.ActorUserId,
        e.ActorRole,
        e.Action,
        e.TargetType,
        e.TargetId,
        e.BeforeJson,
        e.AfterJson,
        e.IpAddress,
        e.UserAgent,
        e.CorrelationId);
```

### Snapshots `Before`/`After` por operação

| Operação | `Before` | `After` |
|---|---|---|
| `enrollment.approved` | `{ "status": "<anterior>" }` | `{ "status": "Aprovado", "approvedAt": "<UTC>" }` |
| `enrollment.rejected` | `{ "status": "<anterior>" }` | `{ "status": "Rejeitado", "motivoRejeicao": "<texto>" }` |
| `enrollment.cancelled` | `{ "status": "<anterior>" }` | `{ "status": "Cancelado" }` |
| `document.verified` | `{ "status": "<anterior>", "dataValidade": <anterior ou null> }` | `{ "status": "Aprovado", "dataValidade": <nova>, "motivoRejeicao": null }` |
| `document.rejected` | `{ "status": "<anterior>", "dataValidade": <anterior ou null> }` | `{ "status": "Rejeitado", "dataValidade": <anterior>, "motivoRejeicao": "<texto>" }` |
| `session.revoked` | `{ "revokedAt": null }` | `{ "revokedAt": "<UTC>", "reason": "reuse_detected" ou ausente }` |

Os valores de `Status` usam `Enum.ToString()` (ex.: `Aprovado`, `DocumentacaoPendente`, `Cancelado`), não o inteiro — o registro precisa ser legível fora do código.

### Configuração

| Chave | Tipo | Default | Validação |
|---|---|---|---|
| `Audit:RetentionDays` | inteiro | `365` | 90 ≤ valor ≤ 3650; inválido → `InvalidOperationException` na inicialização |

## File / Module Layout

### Novos arquivos

| Caminho | Conteúdo |
|---|---|
| `src/Ciclo.Core/Entities/AuditEvent.cs` | entidade tenant-scoped |
| `src/Ciclo.Core/Auditing/AuditActions.cs` | constantes de ação |
| `src/Ciclo.Core/Auditing/AuditTargetTypes.cs` | constantes de tipo de alvo |
| `src/Ciclo.Core/Auditing/AuditEntry.cs` | payload de escrita |
| `src/Ciclo.Core/Auditing/AuditCorrelation.cs` | resolução de correlação |
| `src/Ciclo.Core/Interfaces/IAuditTrail.cs` | porta de escrita |
| `src/Ciclo.Core/Interfaces/IAuditActor.cs` | contrato do ator |
| `src/Ciclo.Core/Exceptions/AuditImmutabilityException.cs` | exceção de imutabilidade |
| `src/Ciclo.Infrastructure/Auditing/AuditSettings.cs` | leitura e validação da retenção |
| `src/Ciclo.Infrastructure/Auditing/AuditActor.cs` | contexto scoped do ator |
| `src/Ciclo.Infrastructure/Auditing/AuditTrail.cs` | implementação da escrita |
| `src/Ciclo.Infrastructure/Auditing/AuditCsvWriter.cs` | serialização CSV |
| `src/Ciclo.Infrastructure/Auditing/AuditRetentionPurger.cs` | expurgo em lotes |
| `src/Ciclo.Infrastructure/Auditing/AuditRetentionService.cs` | `BackgroundService` de expurgo |
| `src/Ciclo.Infrastructure/Contracts/AuditDtos.cs` | `AuditEventFilter`, `AuditEventDto` |
| `src/Ciclo.Infrastructure/Services/AuditQueryService.cs` | `IAuditQueryService`, `AuditException`, `AuditQueryService`; requer `using System.Linq.Expressions;`, `using Ciclo.Infrastructure.Contracts;` e `using Microsoft.EntityFrameworkCore;` |
| `src/Ciclo.Api/Middleware/AuditContextMiddleware.cs` | captura de ator/origem |
| `src/Ciclo.Api/Controllers/AuditController.cs` | consulta e exportação |
| Migration `AddAuditEvents` (gerada em `src/Ciclo.Infrastructure/Migrations/`) | tabela, índices, gatilhos |

### Arquivos modificados

| Caminho | Modificação |
|---|---|
| `src/Ciclo.Infrastructure/Data/AppDbContext.cs` | `DbSet`, configuração do modelo, guarda de imutabilidade nas duas substituições de `SaveChangesAsync`; requer `using Ciclo.Infrastructure.Auditing;` |
| `src/Ciclo.Infrastructure/Services/EnrollmentService.cs` | construtor recebe `IAuditTrail`; três chamadas de `Record` |
| `src/Ciclo.Infrastructure/Services/DocumentService.cs` | construtor recebe `IAuditTrail`; uma chamada de `Record` em `VerifyAsync` |
| `src/Ciclo.Infrastructure/Services/AuthService.cs` | construtor recebe `IAuditTrail`; registros em `RevokeTokenAsync` |
| `src/Ciclo.Api/Program.cs` | `AuditSettings.Read`, registros de DI, `AuditContextMiddleware` no pipeline, serviço hospedado fora de `Testing` |
| `src/Ciclo.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | atualizado pela geração da migration |
| `management/adr.md` | ADR-004 (retenção e base legal) |
| `tests/Ciclo.Api.Tests/DocumentComplianceTests.cs` | passa `IAuditTrail` ao construir `EnrollmentService`/`DocumentService` |
| `tests/Ciclo.Api.Tests/AuthServiceTests.cs` | registra `IAuditTrail` e `IAuditActor` no `ServiceCollection` |

### Registro em `Program.cs`

Inserir junto aos demais registros de serviço (após `AddInfrastructure` e junto dos serviços de aplicação):

```csharp
// Auditoria (Spec 110) — configuração fail-closed e porta de escrita transversal.
builder.Services.AddSingleton(AuditSettings.Read(builder.Configuration));
builder.Services.AddScoped<AuditActor>();
builder.Services.AddScoped<IAuditActor>(sp => sp.GetRequiredService<AuditActor>());
builder.Services.AddScoped<IAuditTrail, AuditTrail>();
builder.Services.AddScoped<IAuditQueryService, AuditQueryService>();
builder.Services.AddScoped<AuditRetentionPurger>();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<AuditRetentionService>();
}
```

Inserir no pipeline imediatamente após `app.UseMiddleware<TenantMiddleware>();`:

```csharp
app.UseMiddleware<AuditContextMiddleware>(); // Spec 110 — ator, origem e correlação
```

### Migration e gatilhos

A migration é gerada com a receita registrada em `management/cross-session/plano-correcoes.md` §1 (CLI do EF via `.nupkg` extraído, `--no-build`, `APPDATA` exportado). O conteúdo gerado para a tabela e índices não é editado à mão; apenas os comandos SQL de gatilho são acrescentados ao final de `Up` e ao início de `Down`.

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // conteúdo gerado por `dotnet ef migrations add AddAuditEvents` (CreateTable + índices)

    migrationBuilder.Sql(
        """
        CREATE OR REPLACE FUNCTION audit_events_append_only_guard() RETURNS trigger AS $$
        BEGIN
            IF TG_OP = 'UPDATE' THEN
                RAISE EXCEPTION 'audit_events are append-only: UPDATE is not allowed';
            END IF;
            IF OLD."OccurredAt" > (now() - interval '90 days') THEN
                RAISE EXCEPTION 'audit_events within the immutability floor (90 days) cannot be deleted';
            END IF;
            RETURN OLD;
        END;
        $$ LANGUAGE plpgsql;

        CREATE TRIGGER audit_events_no_update
            BEFORE UPDATE ON "AuditEvents"
            FOR EACH ROW EXECUTE FUNCTION audit_events_append_only_guard();

        CREATE TRIGGER audit_events_no_delete
            BEFORE DELETE ON "AuditEvents"
            FOR EACH ROW EXECUTE FUNCTION audit_events_append_only_guard();
        """);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(
        """
        DROP TRIGGER IF EXISTS audit_events_no_delete ON "AuditEvents";
        DROP TRIGGER IF EXISTS audit_events_no_update ON "AuditEvents";
        DROP FUNCTION IF EXISTS audit_events_append_only_guard();
        """);

    // conteúdo gerado: DropTable("AuditEvents")
}
```

O literal `90 days` é o espelho de `AuditSettings.MinimumRetentionDays`; o teste `AuditSettingsTests.TheDatabaseTriggerUsesTheSameImmutabilityFloor` compara os dois valores.

## Testing Strategy

Todos os testes novos ficam em `tests/Ciclo.Api.Tests`. O helper compartilhado `AuditTestData` cria contexto derivado com tenant fixo e InMemory de nome único (a mesma armadilha de cache de modelo já documentada: `Expression.Constant(_tenantContext)` prende a instância que construiu o modelo, e escrever `AsyncLocal` dentro de helper assíncrono não propaga de volta).

| Classe de teste | Método | Verifica |
|---|---|---|
| `AuditTrailTests` | `Record_PersistsActorOriginTenantAndSnapshots` | todos os campos gravados, JSON camelCase, tenant do contexto |
| `AuditTrailTests` | `Record_WhenTenantIsUnresolved_ThrowsAndPersistsNothing` | `TenantNotResolvedException`; contagem de eventos permanece 0 |
| `AuditTrailTests` | `Record_WhenActionIsEmpty_Throws` | `ArgumentException` |
| `AuditTrailTests` | `Record_WhenPayloadExceedsTheLimit_Throws` | `InvalidOperationException` citando o tamanho |
| `AuditTrailTests` | `Record_DoesNotSaveChanges` | evento está `Added` e `CountAsync` sem novo `SaveChanges` não o encontra |
| `AuditImmutabilityTests` | `ModifyingAPersistedEvent_Throws` | `AuditImmutabilityException` com `audit_events_are_append_only` |
| `AuditImmutabilityTests` | `DeletingAnEventInsideTheFloor_Throws` | `AuditImmutabilityException` com o código do piso |
| `AuditImmutabilityTests` | `DeletingAnEventOutsideTheFloor_Succeeds` | remoção confirmada |
| `AuditImmutabilityTests` | `AuditControllerExposesNoMutationEndpoint` | reflexão: nenhum método com `HttpPut`/`HttpPatch`/`HttpDelete` em `AuditController` |
| `AuditOperationCoverageTests` | `Approve_WritesEnrollmentApprovedEvent` | ação, alvo, `before`/`after` com status |
| `AuditOperationCoverageTests` | `Reject_WritesEnrollmentRejectedEventWithReason` | `motivoRejeicao` no `after` |
| `AuditOperationCoverageTests` | `Cancel_WritesEnrollmentCancelledEvent` | ação e alvo |
| `AuditOperationCoverageTests` | `FailedTransition_WritesNoEvent` | transição inválida não gera evento |
| `AuditOperationCoverageTests` | `VerifyApproved_WritesDocumentVerifiedEvent` | ação, validade anterior e nova |
| `AuditOperationCoverageTests` | `VerifyRejected_WritesDocumentRejectedEventWithReason` | motivo no `after` |
| `AuditOperationCoverageTests` | `Revoke_WritesSessionRevokedEvent` | ação, alvo de sessão, `revokedAt` |
| `AuditOperationCoverageTests` | `RevokeOnReuse_RecordsTheReason` | `reason = reuse_detected` |
| `AuditOperationCoverageTests` | `Approve_WhenAuditFails_DoesNotChangeTheEnrollment` | trilha que lança: status inalterado e nenhum evento |
| `AuditQueryServiceTests` | `Query_FiltersByActionAndDateRange` | filtros aplicados e `Total` correto |
| `AuditQueryServiceTests` | `Query_OrdersByOccurredAtThenIdDescending` | ordem estável |
| `AuditQueryServiceTests` | `Query_DoesNotReturnEventsFromAnotherTenant` | isolamento por tenant |
| `AuditQueryServiceTests` | `Query_NormalizesPageAndPageSize` | `page=0` → 1; `pageSize=0` → 50; `pageSize=500` → 200 |
| `AuditQueryServiceTests` | `Query_WhenRangeIsInverted_ThrowsInvalidDateRange` | `AuditException` 400 com `invalid_date_range` |
| `AuditQueryServiceTests` | `Query_WhenRangeExceeds400Days_ThrowsDateRangeTooWide` | `AuditException` 400 com `date_range_too_wide` |
| `AuditQueryServiceTests` | `Export_OrdersChronologicallyAscending` | primeira linha é o evento mais antigo |
| `AuditQueryServiceTests` | `Export_WhenAboveTheLimit_ThrowsExportExceedsLimit` | `AuditException` 400 com `export_exceeds_limit` |
| `AuditQueryServiceTests` | `Export_StartsWithUtf8Bom` | bytes `EF BB BF` no início |
| `AuditCsvWriterTests` | `Write_EmitsTheFixedHeader` | cabeçalho exato |
| `AuditCsvWriterTests` | `Write_EscapesCommaQuoteAndNewline` | RFC 4180 |
| `AuditCsvWriterTests` | `Write_FormatsOccurredAtAsIsoUtc` | `yyyy-MM-ddTHH:mm:ss.fffZ` |
| `AuditCorrelationTests` | `Resolve_KeepsAValidCandidate` | candidato válido preservado |
| `AuditCorrelationTests` | `Resolve_FallsBackToTraceIdentifierWhenInvalid` | inválido/curto/longo → trace id |
| `AuditCorrelationTests` | `Resolve_TruncatesTheFallback` | fallback truncado em 64 |
| `AuditActorTests` | `Capture_TruncatesUserAgentTo512` | truncamento |
| `AuditActorTests` | `Capture_NormalizesEmptyValuesToNull` | vazio → nulo |
| `AuditSettingsTests` | `Read_DefaultsTo365Days` | ausência de chave |
| `AuditSettingsTests` | `Read_RejectsRetentionBelowTheFloor` | 30 → `InvalidOperationException` citando 90 |
| `AuditSettingsTests` | `Read_RejectsNonIntegerValue` | texto → `InvalidOperationException` |
| `AuditSettingsTests` | `Read_RejectsRetentionAboveTheMaximum` | 4000 → `InvalidOperationException` |
| `AuditSettingsTests` | `TheDatabaseTriggerUsesTheSameImmutabilityFloor` | SQL da migration contém `90 days` e `MinimumRetentionDays == 90` |
| `AuditRetentionTests` | `Purge_DeletesOnlyEventsOlderThanRetention` | 3 antigos removidos, 2 recentes mantidos |
| `AuditRetentionTests` | `Purge_ReturnsTheNumberOfRemovedEvents` | retorno igual à contagem |
| `AuditRetentionTests` | `Purge_WhenNothingIsEligible_ReturnsZero` | retorno 0 |
| `AuditPipelineTests` | `Health_EchoesTheCorrelationIdHeader` | `ApiTestHost`, `/health`, cabeçalho presente e não vazio |

Teste da migration por reflexão (sem banco): instanciar `AddAuditEvents`, criar `new MigrationBuilder("Npgsql")`, invocar `Up` via reflexão e inspecionar `builder.Operations.OfType<SqlOperation>()` para confirmar a função e os dois gatilhos.

**Controle negativo obrigatório:** cada teste que afirma "X não acontece" (`FailedTransition_WritesNoEvent`, `Approve_WhenAuditFails_DoesNotChangeTheEnrollment`, `Query_DoesNotReturnEventsFromAnotherTenant`) deve ser provado por reversão temporária da implementação durante a execução da tarefa, com evidência registrada. Um teste que passa antes e depois da reversão é vazio e deve ser reescrito.

Validação com PostgreSQL real (uma vez, na tarefa de migration): aplicar a migration em banco local, executar `UPDATE` e `DELETE` de evento recente (ambos devem falhar), `DELETE` de evento com mais de 90 dias (deve funcionar) e confirmar que o schema criado contém os três índices.

## Migration / Rollback

**Aplicação:** `dotnet ef migrations add AddAuditEvents` (receita do §1 do plano) → acrescentar SQL dos gatilhos → `dotnet ef database update` (ou `MigrateAsync` em Development). O baseline `InitialCreate` já está aplicado; esta é uma migration aditiva (`CreateTable` + índices + gatilhos), sem alteração de tabelas existentes.

**Rollback:** `dotnet ef database update <migration-anterior>` executa `Down`, que remove gatilhos, função e a tabela. Como a tabela é nova e nenhuma outra tabela referencia `AuditEvents`, o rollback não afeta dados de negócio. Eventos gravados são perdidos no rollback — comportamento esperado e aceitável, pois rollback é operação de desenvolvimento.

**Compatibilidade:** nenhuma mudança em tabelas existentes; nenhuma coluna removida; nenhum contrato público alterado. Bancos existentes recebem a tabela nova sem migração de dados.

**Feature flag:** não aplicável. A funcionalidade é aditiva; desligá-la significaria deixar de auditar operações, o que violaria V11. O único controle operacional é `Audit:RetentionDays`.
