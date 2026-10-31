# Spec 140: comunicacao-proativa — Design

## Design Approach

Three pieces, each with one owner:

1. **A trigger contract** (`NotificationTriggerEvent` + `NotificationTriggerKind`) in Core, so the domain services name a domain event instead of building notification text.
2. **One dispatcher** (`ProactiveNotificationService`) that turns a trigger into per-recipient in-app rows and e-mail sends, with an insert-first `NotificationDelivery` row that carries both the idempotency key and the outcome.
3. **Two entry points into that dispatcher**: the domain services call `HandleAsync` after a committed status transition, and `ScheduledNotificationJob` calls `RunScheduledAsync` once per interval with the current clock.

The due set for the scheduled path is derived from `Document.DataValidade` and `EnrollmentPeriod.DataInicio`/`DataFim` instead of a separate schedule table: the persisted schedule state that matters — "was this already delivered for this period" — is exactly the `NotificationDelivery` row, so a second table would duplicate it. Templates live in one static catalog so in-app and e-mail text cannot drift apart.

## Architecture Decisions

**AD-140-1 — Own `BackgroundService`, not Hangfire/Quartz.** `management/arch.md` already records the project decision (no external scheduler; own `BackgroundService`), and `EmailDispatchService` is the working precedent. This resolves V14's §6-3 decision: `ScheduledNotificationJob` follows the same shape — `AddHostedService`, a scope per tick, exceptions caught per tick.

**AD-140-2 — No schedule table.** The candidate set is re-derived from `Document` and `EnrollmentPeriod` on every tick. The 24-hour date suffix in the idempotency key turns the derived set into a once-per-day delivery without persisting a plan that would then need its own reconciliation.

**AD-140-3 — Insert-first idempotency.** The delivery row is written in state `Pendente` and saved before the channel is called. A unique index on `(TenantId, IdempotencyKey, RecipientUserId, Channel)` is the arbiter, so two instances racing on the same trigger cannot both send.

**AD-140-4 — E-mail goes through `IEmailSender` directly, not `IEmailDispatcher`.** The dispatcher is a fire-and-forget queue; it cannot report per-recipient outcome. The proactive path needs `Enviado`/`Falhou` per recipient, so it calls `IEmailSender` inline. `NotificationService` keeps its queue-based hand-off untouched.

**AD-140-5 — The proactive dispatch is not part of the business transaction.** It runs after the status change is committed, and the domain service catches and logs any exception from it, so a notification problem never reports a successful approval as a failed request.

## Component / Module Breakdown

### Component: NotificationTriggerEvent (Core contract)

**Purpose:** Names a domain event for the dispatcher without leaking notification text into the domain services.
**Interface:** `src/Ciclo.Core/Notifications/NotificationTriggerEvent.cs`
```csharp
public sealed record NotificationTriggerEvent(
    NotificationTriggerKind Kind,
    Guid TenantId,
    Guid StudentId,
    Guid ReferenceId,
    DateTime OccurredAt);

public enum NotificationTriggerKind
{
    MatriculaSubmetida = 0,
    MatriculaAprovada = 1,
    MatriculaRejeitada = 2,
    DocumentoVerificado = 3,
    DocumentoRejeitado = 4,
    DocumentoAVencer = 5,
    RematriculaAberta = 6,
    RematriculaPrazoFinal = 7
}
```
**Internal Logic:** Pure data. `StudentId` is `Guid.Empty` for the two period kinds; the dispatcher resolves those recipients from `Enrollment` by `ReferenceId` (the period id).
**Dependencies:** none.
**Error Handling:** none — a value object.

### Component: NotificationDelivery (Core entity)

**Purpose:** One row per (trigger, recipient, channel) that carries the idempotency key and the delivery outcome.
**Interface:** `src/Ciclo.Core/Entities/NotificationDelivery.cs`
```csharp
public class NotificationDelivery : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid RecipientUserId { get; set; }
    public NotificationTriggerKind Kind { get; set; }
    public NotificationDeliveryChannel Channel { get; set; }
    public NotificationDeliveryState State { get; set; } = NotificationDeliveryState.Pendente;
    public string? Erro { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public Tenant Tenant { get; set; } = null!;
}

public enum NotificationDeliveryChannel { InApp = 0, Email = 1 }

public enum NotificationDeliveryState { Pendente = 0, Enviado = 1, Falhou = 2, Suprimida = 3 }
```
**Internal Logic:** Written only by the dispatcher. `Suprimida` is stored when a duplicate is detected after the row already exists (the pre-existing row keeps its own state; the attempt itself does not create a second row, so `Suprimida` is used only when the dispatcher wins the insert race and then decides not to send).
**Dependencies:** `ITenantScoped`.
**Error Handling:** `Erro` is truncated to 500 characters before assignment; the column is configured with `HasMaxLength(500)`.

### Component: NotificationTemplateCatalog (Infrastructure)

**Purpose:** Single source of subject and body text per trigger kind and locale.
**Interface:** `src/Ciclo.Infrastructure/Notifications/NotificationTemplateCatalog.cs`
```csharp
public sealed record NotificationTemplate(string Assunto, string Corpo);

public static class NotificationTemplateCatalog
{
    public const string DefaultLocale = "pt-BR";

    public static NotificationTemplate Resolve(NotificationTriggerKind kind, string? locale = null);
}
```
**Internal Logic:** A `static readonly IReadOnlyDictionary<NotificationTriggerKind, NotificationTemplate>` holds the `pt-BR` entries. `Resolve` normalises `locale` (`Trim`, `ToUpperInvariant`), looks up `(kind, locale)`, falls back to `(kind, DefaultLocale)`, and throws when the kind has no `pt-BR` entry. Bodies carry `{aluno}` / `{referencia}`.
**Dependencies:** `Ciclo.Core.Notifications`.
**Error Handling:** Unknown or blank locale silently falls back to `pt-BR`. A kind absent from the catalog throws `InvalidOperationException($"notification_template_missing:{kind}")`.

### Component: ProactiveNotificationService (Infrastructure)

**Purpose:** Turns one trigger into delivery rows for every recipient, and produces the scheduled triggers for a given clock value.
**Interface:** `src/Ciclo.Infrastructure/Notifications/ProactiveNotificationService.cs`
```csharp
public interface IProactiveNotificationService
{
    Task HandleAsync(NotificationTriggerEvent trigger, CancellationToken ct);
    Task<int> RunScheduledAsync(DateTime nowUtc, CancellationToken ct);
}

public sealed class ProactiveNotificationService : IProactiveNotificationService
{
    public const int JanelaVencimentoDias = 30;
    public const int JanelaPrazoRematriculaDias = 7;
    public const int LimitePorCiclo = 500;

    public ProactiveNotificationService(
        AppDbContext db,
        IEmailSender emailSender,
        ITenantContext tenantContext,
        ILogger<ProactiveNotificationService> logger);
}
```
**Internal Logic:**
- `HandleAsync`:
  1. Calls `tenantContext.SetTenant(trigger.TenantId)` when `trigger.TenantId` is not `Guid.Empty`, so the EF query filter and the tenant-stamped rows agree.
  2. `ResolveRecipientsAsync(trigger, ct)`: for `RematriculaAberta` / `RematriculaPrazoFinal` it returns the distinct responsáveis of the students that have an `Enrollment` with `EnrollmentPeriodId == trigger.ReferenceId`; for every other kind it returns the responsáveis of `trigger.StudentId`. Each recipient is `record Recipient(Guid UserId, string Nome, string? Email)` read from `StudentParent` joined to `User`, filtered by `TenantId`.
  3. Returns without writing anything when the recipient list is empty (student with no responsável) or when the student does not exist in that tenant — both cases log at warning level.
  4. Resolves the template once (`NotificationTemplateCatalog.Resolve(kind, "pt-BR")`), substitutes `{aluno}` with `Student.Nome` (empty for period kinds) and `{referencia}` with the period name or the document type name, then for each recipient and each channel calls `DispatchAsync`.
- `DispatchAsync(trigger, recipient, channel, subject, body, ct)`:
  1. Builds `key = BuildKey(kind, referenceId, occurredAt)` — `<kind>:<referenceId>` for the five event kinds, `<kind>:<referenceId>:<yyyy-MM-dd>` for the three scheduled kinds, formatted with `CultureInfo.InvariantCulture`.
  2. Adds and saves a `NotificationDelivery` with `State = Pendente`. A `DbUpdateException` on that save means the key already exists: the method logs at information level, leaves the existing row untouched and returns.
  3. Channel `InApp`: inserts a `Notification` (`Titulo` = subject, `Mensagem` = body, `Tipo` mapped from the kind, `ReferenceId`) and a `UserNotification` for the recipient; then sets `State = Enviado`, `SentAt = DateTime.UtcNow`.
  4. Channel `Email`: `await emailSender.SendAsync(recipient.Email!, recipient.Nome, subject, body)`; then `Enviado`.
  5. Any exception from step 3 or 4 is caught, the row is set to `Falhou` with the truncated message and saved, and the exception does not propagate — the loop continues with the next recipient.
- Kind → `NotificationType` mapping: `Matricula*` and `Rematricula*` → `NotificationType.Matricula`; `Documento*` → `NotificationType.DocumentoPendente`.
- `RunScheduledAsync`:
  1. Reads every tenant id from `db.Tenants` (the tenant list is not tenant-filtered).
  2. For each tenant: `SetTenant(id)`; then queries `Documents` where `Status == DocumentStatus.Aprovado && DataValidade != null && DataValidade >= nowUtc && DataValidade <= nowUtc.AddDays(JanelaVencimentoDias)`, `.OrderBy(d => d.DataValidade).Take(LimitePorCiclo)`; for each, `HandleAsync` with kind `DocumentoAVencer`, `StudentId = d.StudentId`, `ReferenceId = d.Id`, `OccurredAt = nowUtc`.
  3. Same tenant iteration for `EnrollmentPeriods` where `IsActive` and `DataInicio.Date == nowUtc.Date` → `RematriculaAberta` with `ReferenceId = period.Id`; and where `IsActive` and `DataFim >= nowUtc && DataFim <= nowUtc.AddDays(JanelaPrazoRematriculaDias)` → `RematriculaPrazoFinal`.
  4. Returns the number of triggers emitted, which is the value the tests assert on.
**Dependencies:** `AppDbContext`, `IEmailSender`, `ITenantContext`, `ILogger<ProactiveNotificationService>`, `NotificationTemplateCatalog`.
**Error Handling:** Per-recipient and per-channel failures are recorded as `Falhou` and never abort the batch. A tenant-level query failure propagates to `RunScheduledAsync`'s caller (the job catches and logs it per tick). A missing template throws and surfaces immediately.

### Component: ScheduledNotificationJob (Infrastructure)

**Purpose:** Hosts the scheduled tick inside the process.
**Interface:** `src/Ciclo.Infrastructure/Notifications/ScheduledNotificationJob.cs`
```csharp
public sealed class ScheduledNotificationJob : BackgroundService
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

    public ScheduledNotificationJob(IServiceScopeFactory scopeFactory, ILogger<ScheduledNotificationJob> logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken);
}
```
**Internal Logic:** Creates a `PeriodicTimer(Intervalo)`; the first tick runs immediately, then one tick per interval. Per tick: `scopeFactory.CreateScope()`, resolve `IProactiveNotificationService`, call `RunScheduledAsync(DateTime.UtcNow, stoppingToken)`, log the returned trigger count at information level. The scope is disposed at the end of every tick.
**Dependencies:** `IServiceScopeFactory`, `ILogger<ScheduledNotificationJob>`.
**Error Handling:** The tick body is wrapped in `try/catch (Exception ex)` when `ex is not OperationCanceledException`; the failure is logged at error level and the loop continues to the next interval. `OperationCanceledException` ends the loop cleanly.

### Component: SmtpEmailSender (Infrastructure)

**Purpose:** Real e-mail provider behind the existing `IEmailSender` contract, with fail-closed configuration.
**Interface:** `src/Ciclo.Infrastructure/Email/SmtpEmailSender.cs`
```csharp
public sealed class SmtpEmailSender : IEmailSender
{
    public const string MissingConfigurationCode = "email_configuration_missing";

    public SmtpEmailSender(IConfiguration configuration);

    public Task SendAsync(string toEmail, string toName, string subject, string body);
}
```
**Internal Logic:** The constructor reads `Email:Smtp:Host`, `Email:Smtp:Porta`, `Email:Smtp:Remetente`, `Email:Smtp:Usuario`, `Email:Smtp:Senha`. `Host`, `Porta` (parsed as a positive integer) and `Remetente` are required; when any is missing it throws `InvalidOperationException` whose message starts with `email_configuration_missing` and names every missing key. `SendAsync` builds a `MailMessage` (`From` = `Remetente`, `To` = `toEmail`/`toName`, `Subject`, `Body`, `IsBodyHtml = false`) and a `SmtpClient(Host, Porta)` using the credentials when `Usuario` is present, then calls `SendMailAsync`. The client and message are disposed.
**Dependencies:** `Microsoft.Extensions.Configuration`, `System.Net.Mail`.
**Error Handling:** Configuration problems throw in the constructor, never on send. An SMTP failure from `SendMailAsync` propagates unchanged so the dispatcher records it as `Falhou`.

### Component: Domain wiring in EnrollmentService and DocumentService

**Purpose:** Raise the event trigger after the status transition is committed.
**Interface:** constructor additions only.
- `EnrollmentService(AppDbContext db, IAuditTrail audit, IProactiveNotificationService proactive, ILogger<EnrollmentService> logger)`
- `DocumentService(AppDbContext db, IFileStorage storage, INotificationService notifications, ILogger<DocumentService> logger, IAuditTrail audit, IProactiveNotificationService proactive)`
**Internal Logic:** After the `SaveChangesAsync` that persists the new status:
- `SubmitAsync` → kind `MatriculaSubmetida`, `StudentId = enrollment.StudentId`, `ReferenceId = enrollment.Id`.
- `ApproveAsync` → kind `MatriculaAprovada`; `RejectAsync` → kind `MatriculaRejeitada`.
- `DocumentService.VerifyAsync` → kind `DocumentoVerificado` when the resulting `DocumentStatus` is `Aprovado`, `DocumentoRejeitado` when it is `Rejeitado`; `ReferenceId = document.Id`.
**Dependencies:** `IProactiveNotificationService`.
**Error Handling:** Each call is wrapped in `try/catch (Exception ex)` with `logger.LogError(ex, ...)` when `ex is not OperationCanceledException`, so the committed domain operation still returns success. An `OperationCanceledException` is rethrown.

### Component: Registration (Program.cs)

**Purpose:** Compose the channel, the dispatcher and the job.
**Interface:** in `src/Ciclo.Api/Program.cs`, replacing the single `AddScoped<IEmailSender, ConsoleEmailSender>()` line with a provider branch:
```csharp
builder.Services.AddScoped<IProactiveNotificationService, ProactiveNotificationService>();
builder.Services.AddHostedService<ScheduledNotificationJob>();

var emailProvider = builder.Configuration["Email:Provedor"];
if (string.Equals(emailProvider, "Smtp", StringComparison.OrdinalIgnoreCase))
{
    _ = new SmtpEmailSender(builder.Configuration); // fail-closed at startup
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
}
else
{
    builder.Services.AddScoped<IEmailSender, ConsoleEmailSender>();
}
```
**Internal Logic:** The eager `new SmtpEmailSender(...)` mirrors the existing `FileStorageKey.Parse` startup validation at the same point in `Program.cs`, so a misconfigured deployment fails at boot instead of on the first e-mail.
**Dependencies:** `SmtpEmailSender`, `ProactiveNotificationService`, `ScheduledNotificationJob`.
**Error Handling:** With `Email:Provedor = Smtp` and missing required keys, startup throws `InvalidOperationException`. Without `Email:Provedor`, the console sender is registered exactly as today.

## Data Flow

**Event path:** `EnrollmentService.ApproveAsync` commits `Enrollment.Status` → builds `NotificationTriggerEvent(MatriculaAprovada, …)` → `HandleAsync` → recipients from `StudentParent` → per recipient: insert `NotificationDelivery(Pendente)` → insert `Notification` + `UserNotification` → mark `Enviado` → insert `NotificationDelivery(Email, Pendente)` → `IEmailSender.SendAsync` → mark `Enviado` or `Falhou`.

**Scheduled path:** `ScheduledNotificationJob` tick → new scope → `RunScheduledAsync(DateTime.UtcNow, ct)` → tenant list → per tenant `SetTenant` → due documents and periods → `HandleAsync` per due item → same per-recipient flow as the event path.

**Duplicate path:** a second identical trigger reaches `DispatchAsync` → the `Pendente` insert hits the unique index → `DbUpdateException` is caught → logged and returned, leaving the original row and the original notification untouched.

## Data Structures

**`NotificationDelivery` table (`NotificationDeliveries`)**

| Column | Type | Constraint |
|---|---|---|
| Id | Guid | PK |
| TenantId | Guid | FK → Tenant, required |
| IdempotencyKey | string(200) | required |
| RecipientUserId | Guid | required |
| Kind | int | enum `NotificationTriggerKind` |
| Channel | int | enum `NotificationDeliveryChannel` |
| State | int | enum `NotificationDeliveryState`, default `Pendente` |
| Erro | string(500) | nullable |
| CreatedAt | DateTime | required |
| SentAt | DateTime | nullable |

Unique index: `IX_NotificationDeliveries_Idempotency (TenantId, IdempotencyKey, RecipientUserId, Channel)`.
Index: `IX_NotificationDeliveries_Recipient (RecipientUserId, CreatedAt)`.

**Idempotency key format:** `"{kind}:{referenceId}"` for `MatriculaSubmetida`, `MatriculaAprovada`, `MatriculaRejeitada`, `DocumentoVerificado`, `DocumentoRejeitado`; `"{kind}:{referenceId}:{occurredAt:yyyy-MM-dd}"` for `DocumentoAVencer`, `RematriculaAberta`, `RematriculaPrazoFinal` — both with `CultureInfo.InvariantCulture`.

**Configuration keys:** `Email:Provedor`, `Email:Smtp:Host`, `Email:Smtp:Porta`, `Email:Smtp:Remetente`, `Email:Smtp:Usuario`, `Email:Smtp:Senha`.

## File / Module Layout

Created:

- `src/Ciclo.Core/Notifications/NotificationTriggerEvent.cs` — record and enum.
- `src/Ciclo.Core/Entities/NotificationDelivery.cs` — entity plus the two enums.
- `src/Ciclo.Infrastructure/Notifications/NotificationTemplateCatalog.cs` — catalog and `NotificationTemplate`.
- `src/Ciclo.Infrastructure/Notifications/ProactiveNotificationService.cs` — `IProactiveNotificationService` and the implementation.
- `src/Ciclo.Infrastructure/Notifications/ScheduledNotificationJob.cs` — the `BackgroundService`.
- `src/Ciclo.Infrastructure/Email/SmtpEmailSender.cs` — the SMTP provider.
- `tests/Ciclo.Api.Tests/ProactiveNotificationTests.cs` — the spec test suite.

Modified:

- `src/Ciclo.Infrastructure/Data/AppDbContext.cs` — `DbSet<NotificationDelivery> NotificationDeliveries` plus the entity configuration (indexes, lengths, enum-to-int conversions).
- `src/Ciclo.Api/Program.cs` — provider branch, dispatcher registration, hosted service registration.
- `src/Ciclo.Infrastructure/Services/EnrollmentService.cs` — constructor parameters and the three trigger calls.
- `src/Ciclo.Infrastructure/Services/DocumentService.cs` — constructor parameter and the two trigger calls.
- `src/Ciclo.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` — updated by the generated migration.
- `tests/Ciclo.Api.Tests/AccessPolicyTests.cs` — constructor arguments of `EnrollmentService` and `DocumentService`.
- `tests/Ciclo.Api.Tests/AuditInstrumentationTests.cs` — constructor arguments.
- `tests/Ciclo.Api.Tests/AuditOperationCoverageTests.cs` — constructor arguments.
- `tests/Ciclo.Api.Tests/StudentAccessTests.cs` — constructor arguments.

The schema change is delivered as the EF migration `AddNotificationDeliveries` generated from `AppDbContext`; the sequential migration timestamp is produced by the tool, so no fixed filename is declared here.

## Testing Strategy

`tests/Ciclo.Api.Tests/ProactiveNotificationTests.cs` (xUnit, EF InMemory, fakes in the file's own scope, mirroring `EmailDispatchTests.cs`):

- `HandleAsync_ForEachEventKind_CreatesOneInAppNotificationPerParent` — five kinds, two responsáveis, asserts two `Notification` rows and two `UserNotification` rows each.
- `HandleAsync_ForParentWithoutEmail_CreatesOnlyTheInAppChannel` — asserts one `InApp` row and no `Email` row.
- `HandleAsync_ForUnknownStudent_DoesNotThrowAndWritesNothing` — warning path.
- `RunScheduledAsync_DocumentInsideWindow_EmitsOneTrigger` and `RunScheduledAsync_DocumentOutsideWindow_EmitsNone` — boundaries at 30 days inclusive and 31 days.
- `RunScheduledAsync_ExpiredDocument_EmitsNone` — past `DataValidade`.
- `RunScheduledAsync_ActivePeriod_EmitsOpeningAndDeadline` and `_InactivePeriod_EmitsNone`.
- `RunScheduledAsync_TwoTenants_StampsEachRowWithItsOwnTenant` — tenant isolation.
- `Resolve_ForEveryKind_ReturnsNonEmptyTemplate` and `Resolve_ForUnknownLocale_FallsBackToPtBr`.
- `HandleAsync_CalledTwice_SendsOnlyOnce` — idempotent suppression for both channels.
- `HandleAsync_WhenEmailSenderThrows_RecordsFailedAndKeepsOtherRecipients` — three responsáveis, one failing sender.
- `SmtpEmailSender_WithMissingHost_ThrowsWithMissingConfigurationCode`.
- `AddCicloServices_WithoutProvider_ResolvesConsoleSender` — asserts the `IEmailSender` implementation resolved from a `ServiceCollection` built the same way `Program.cs` builds it.

The four existing test files listed under File / Module Layout are updated only by adding constructor arguments; their assertions stay unchanged, which is the anti-regression signal that the new dependency does not alter existing behavior.

## Migration / Rollback

**Migration:** one addition — table `NotificationDeliveries` with its two indexes. Existing tables and rows are untouched; the migration is additive and needs no backfill.

**Rollback:** drop the `NotificationDeliveries` table and remove the two `AddHostedService`/`AddScoped` registrations; `NotificationService`, `IEmailSender` and every domain service keep their pre-spec behavior because the trigger calls are additive and their exceptions are already contained. With `Email:Provedor` absent, `IEmailSender` resolves to `ConsoleEmailSender` as before, so the provider change is reversible by configuration alone. Because the proactive dispatch never participates in the domain transaction, disabling it cannot leave an enrollment or document in an inconsistent state.
