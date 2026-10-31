---
name: comunicacao-proativa
status: verified
determinism: D
references: [V14]
---

# Spec 140: comunicacao-proativa — Requirements

## Determinism Classification

**Class:** D — Deterministic

Every behavior this spec delivers is decided by persisted state and by the clock value handed to the entry point: which entity is due, which recipient exists, which idempotency key was already used, and which delivery state was recorded. There is no model output and no free-text generation — notification bodies come from a static catalog keyed by `NotificationTriggerKind` and locale. The same fixture plus the same `nowUtc` value always produces the same set of `Notification` and `NotificationDelivery` rows, so the acceptance criteria are proven with plain assertions over database state and over the `IEmailSender` double.

## Value Delivery

**V14: Comunicação Proativa** — Notificações disparadas por evento e por agendamento, não apenas sob ação manual: matrícula submetida, aprovada ou rejeitada; documento verificado ou rejeitado; documento a vencer; abertura e prazo de rematrícula. Inclui templates por tipo, envio in-app e e-mail por provedor real, idempotência e registro de entrega. Completa a promessa das specs 70 e 80.

## Functional Requirements

### FR-001: Gatilho por evento de matrícula e de documento

**What:** `ProactiveNotificationService.HandleAsync(NotificationTriggerEvent trigger, CancellationToken ct)` in `src/Ciclo.Infrastructure/Notifications/ProactiveNotificationService.cs` creates the in-app notification and the e-mail delivery for one domain event. The supported events are exactly: matrícula submetida (`EnrollmentStatus.Pendente`), matrícula aprovada (`EnrollmentStatus.Aprovado`), matrícula rejeitada (`EnrollmentStatus.Rejeitado`), documento verificado (`DocumentStatus.Aprovado`) e documento rejeitado (`DocumentStatus.Rejeitado`).

- `NotificationTriggerEvent` is a record in `src/Ciclo.Core/Notifications/NotificationTriggerEvent.cs` carrying `NotificationTriggerKind Kind`, `Guid TenantId`, `Guid StudentId`, `Guid ReferenceId` e `DateTime OccurredAt`.
- `NotificationTriggerKind` is an enum in the same file with exactly the members `MatriculaSubmetida`, `MatriculaAprovada`, `MatriculaRejeitada`, `DocumentoVerificado`, `DocumentoRejeitado`, `DocumentoAVencer`, `RematriculaAberta`, `RematriculaPrazoFinal`.
- Recipients are the responsáveis of the student (`StudentParent`): one `Notification` plus `UserNotification` row per responsável through those existing entities, and one e-mail send per responsável whose `User.Email` is non-empty. `INotificationService.SendByStudentAsync` keeps its current behavior and is not used as the proactive channel, because it hands the e-mail to the in-memory queue and therefore cannot attribute a delivery state to an individual recipient.
- The trigger is raised by the status transitions that already exist; no new domain flow is introduced. The source is `EnrollmentService.SubmitAsync`, `EnrollmentService.ApproveAsync` and `EnrollmentService.RejectAsync` for the three matrícula kinds, and `DocumentService.VerifyAsync` for the two document kinds.

**Why:** V14 requires notifications triggered by events, not only by manual action. Matrícula and document decisions are the two event families the value block names.

**Acceptance Criteria:**
- [ ] `ProactiveNotificationService.HandleAsync` exists with that exact name in the proactive service file. [verify: function-exists `HandleAsync` `src/Ciclo.Infrastructure/Notifications/ProactiveNotificationService.cs`]
- [ ] The event contract file declares the enum `NotificationTriggerKind` with the eight named members. [verify: grep-present `enum NotificationTriggerKind` `src/Ciclo.Core/Notifications/NotificationTriggerEvent.cs`]
- [ ] Each of the five event kinds produces one in-app notification per responsável. [verify: project-validation]
- [ ] An event for a student whose responsáveis have no e-mail still produces the in-app notification. [verify: project-validation]
**Failure modes:**
- Student id does not exist in the tenant: no entity is created and the method returns without throwing; the miss is logged at warning level with the trigger kind and the id.
- Tenant mismatch between `trigger.TenantId` and the student's `TenantId`: no entity is created and the miss is logged at warning level.
- Persistence error while writing the delivery rows: the exception propagates to the caller so the originating operation is not silently recorded as notified.

### FR-002: Gatilho por agendamento — vencimento de documento e período de rematrícula

**What:** `ProactiveNotificationService.RunScheduledAsync(DateTime nowUtc, CancellationToken ct)` evaluates the whole tenant corpus at the instant `nowUtc` and emits the scheduled triggers. There is no separate schedule table: the due set is derived from the existing entities.

- Documento a vencer: `Document.Status == DocumentStatus.Aprovado`, `Document.DataValidade` not null, `DataValidade >= nowUtc` and `DataValidade <= nowUtc.AddDays(30)` → kind `DocumentoAVencer`.
- Rematrícula aberta: `EnrollmentPeriod.IsActive` and `DataInicio.Date == nowUtc.Date` → kind `RematriculaAberta`.
- Rematrícula prazo final: `EnrollmentPeriod.IsActive`, `DataFim >= nowUtc` and `DataFim <= nowUtc.AddDays(7)` → kind `RematriculaPrazoFinal`.
- The two window sizes are `const int JanelaVencimentoDias = 30` and `const int JanelaPrazoRematriculaDias = 7` on `ProactiveNotificationService`; they are code constants, not configuration keys.
- `ScheduledNotificationJob` in `src/Ciclo.Infrastructure/Notifications/ScheduledNotificationJob.cs` is a `BackgroundService` whose `ExecuteAsync` loops with `PeriodicTimer` at a fixed 1-hour interval, creates a scope per tick, calls `RunScheduledAsync(DateTime.UtcNow, stoppingToken)`, and catches plus logs any exception of a tick so the loop survives.

**Why:** V14 names documento a vencer, abertura e prazo de rematrícula as scheduled notifications. `management/arch.md` already fixes the host decision: the project has no external scheduler and uses its own `BackgroundService`.

**Acceptance Criteria:**
- [ ] `RunScheduledAsync` exists on the proactive service with the `nowUtc` parameter. [verify: function-exists `RunScheduledAsync` `src/Ciclo.Infrastructure/Notifications/ProactiveNotificationService.cs`]
- [ ] `ScheduledNotificationJob` overrides `ExecuteAsync` and drives the timer loop. [verify: function-exists `ExecuteAsync` `src/Ciclo.Infrastructure/Notifications/ScheduledNotificationJob.cs`]
- [ ] A document expiring inside the 30-day window produces exactly one `DocumentoAVencer` trigger; a document expiring in 31 days produces none. [verify: project-validation]
- [ ] An active period whose `DataInicio` equals the `nowUtc` date produces `RematriculaAberta`; an inactive period produces none. [verify: project-validation]
**Failure modes:**
- `Document.DataValidade` is null: the document is skipped and never treated as expiring.
- A document is `Pendente` or `Rejeitado`: it is not a vencimento candidate; only `Aprovado` documents are.
- The tick throws (database unavailable): the exception is caught, logged at error level, and the next tick proceeds at the next interval; the process does not stop.

### FR-003: Catálogo de templates por tipo e locale

**What:** `NotificationTemplateCatalog.Resolve(NotificationTriggerKind kind, string? locale)` in `src/Ciclo.Infrastructure/Notifications/NotificationTemplateCatalog.cs` returns a `NotificationTemplate(string Assunto, string Corpo)` for every `NotificationTriggerKind` member.

- The catalog holds one entry per kind in the `pt-BR` locale; `pt-BR` is the only authored locale in this spec.
- `locale` is trimmed and matched case-insensitively; any value that is null, empty or unknown falls back to `pt-BR`. `Resolve` never returns null for a declared member.
- The resolved `Assunto` feeds both the in-app `Notification.Titulo` and the e-mail subject; the resolved `Corpo` feeds the in-app `Mensagem` and the e-mail body. There is no second template source.
- `Corpo` may carry the placeholders `{aluno}` and `{referencia}`. `Resolve` returns them untouched and the caller substitutes `Student.Nome` and the trigger reference before persisting; a placeholder with no resolvable value is replaced by an empty string, never left literal in the stored text.

**Why:** V14 requires templates per type, and one catalog keeps in-app and e-mail text identical by construction.

**Acceptance Criteria:**
- [ ] `Resolve` exists with the kind and locale parameters. [verify: function-exists `Resolve` `src/Ciclo.Infrastructure/Notifications/NotificationTemplateCatalog.cs`]
- [ ] The catalog declares the `pt-BR` locale literal it falls back to. [verify: grep-present `pt-BR` `src/Ciclo.Infrastructure/Notifications/NotificationTemplateCatalog.cs`]
- [ ] Every `NotificationTriggerKind` member resolves to a non-empty subject and body, and an unknown locale resolves to the `pt-BR` entry. [verify: project-validation]
**Failure modes:**
- A `NotificationTriggerKind` value with no catalog entry: `Resolve` throws `InvalidOperationException` naming the member, so a missing template fails at the first dispatch instead of sending an empty body.
- `locale` is whitespace: treated as absent and the `pt-BR` entry is returned.

### FR-004: Entrega in-app reutilizada e e-mail por provedor real com configuração fail-closed

**What:** The in-app channel keeps using the existing `Notification` and `UserNotification` path; the e-mail channel gains a real provider. `SmtpEmailSender` in `src/Ciclo.Infrastructure/Email/SmtpEmailSender.cs` implements the existing `IEmailSender.SendAsync(string toEmail, string toName, string subject, string body)` contract over `System.Net.Mail.SmtpClient`.

- The constructor takes `IConfiguration` and reads `Email:Smtp:Host`, `Email:Smtp:Porta`, `Email:Smtp:Remetente`, `Email:Smtp:Usuario` e `Email:Smtp:Senha`.
- Fail-closed: `Host`, `Porta` and `Remetente` are required. When any of them is missing or blank the constructor throws `InvalidOperationException` whose message starts with `email_configuration_missing`, so a misconfigured deployment fails at startup service-provider validation instead of degrading to a silent no-op.
- Registration in `src/Ciclo.Api/Program.cs`: when `Email:Provedor` equals `Smtp` (case-insensitive) `IEmailSender` resolves to `SmtpEmailSender`; otherwise it stays `ConsoleEmailSender`, the current development behavior.
- The `IEmailSender` interface is not changed and `ConsoleEmailSender` keeps its current signature and behavior.

**Why:** V14 requires e-mail by a real provider with fail-closed configuration; a required-field check at construction makes a production misconfiguration visible at boot.

**Acceptance Criteria:**
- [ ] `SmtpEmailSender` implements `SendAsync` with the existing contract parameters. [verify: function-exists `SendAsync` `src/Ciclo.Infrastructure/Email/SmtpEmailSender.cs`]
- [ ] The missing-configuration failure carries the `email_configuration_missing` prefix. [verify: grep-present `email_configuration_missing` `src/Ciclo.Infrastructure/Email/SmtpEmailSender.cs`]
- [ ] Constructing `SmtpEmailSender` with a blank `Email:Smtp:Host` throws `InvalidOperationException` whose message contains `email_configuration_missing`. [verify: project-validation]
- [ ] With `Email:Provedor` absent the resolved `IEmailSender` is still `ConsoleEmailSender`. [verify: project-validation]
**Failure modes:**
- SMTP server refuses the connection or the authentication: `SendAsync` lets the exception surface to the caller, which records `NotificationDeliveryState.Falhou` with the message; nothing is swallowed inside the sender.
- Recipient address is blank: the caller skips the e-mail channel for that recipient and creates no `Email` row for it; the in-app notification still exists.

### FR-005: Idempotência por evento e destinatário

**What:** `NotificationDelivery` in `src/Ciclo.Core/Entities/NotificationDelivery.cs` carries a composite natural key and the dispatcher uses it to suppress duplicates before any side effect.

- Fields: `Guid Id`, `Guid TenantId`, `string IdempotencyKey`, `Guid RecipientUserId`, `NotificationTriggerKind Kind`, `NotificationDeliveryChannel Channel`, `NotificationDeliveryState State`, `string? Erro`, `DateTime CreatedAt`, `DateTime? SentAt`.
- `IdempotencyKey` is built as `<kind>:<referenceId>:<yyyy-MM-dd>` for scheduled triggers and `<kind>:<referenceId>` for event triggers, formatted with `CultureInfo.InvariantCulture`.
- `AppDbContext` declares `DbSet<NotificationDelivery> NotificationDeliveries` with a unique index on `(TenantId, IdempotencyKey, RecipientUserId, Channel)`.
- Insert-first: the dispatcher adds and saves the delivery row in state `Pendente` before calling the channel. A unique-index violation is caught, the duplicate is skipped and the method returns without sending, so a repeated event never produces a second in-app notification or a second e-mail.

**Why:** V14 requires idempotency keyed by event and recipient. Insert-first makes suppression a database guarantee instead of an in-memory convention that a second instance would break.

**Acceptance Criteria:**
- [ ] The entity declares the `IdempotencyKey` field. [verify: grep-present `IdempotencyKey` `src/Ciclo.Core/Entities/NotificationDelivery.cs`]
- [ ] `AppDbContext` declares the `NotificationDeliveries` set. [verify: grep-present `NotificationDeliveries` `src/Ciclo.Infrastructure/Data/AppDbContext.cs`]
- [ ] Handling the same trigger event twice produces exactly one in-app notification and one e-mail for the same responsável. [verify: project-validation]
- [ ] The second handling of a duplicate returns without throwing and leaves the first delivery row unchanged. [verify: project-validation]
**Failure modes:**
- Two instances insert the same key concurrently: the unique index rejects one of them and that instance treats the rejection as suppression, not as an error.
- An event reference id is `Guid.Empty`: the idempotency key still builds with the empty guid formatted as-is and behaves like any other key; no special case is added.

### FR-006: Registro de entrega

**What:** Every attempted delivery leaves exactly one `NotificationDelivery` row whose `State` records the outcome, and one delivery failure never aborts the rest of the batch.

- `NotificationDeliveryChannel` has exactly the members `InApp` and `Email`.
- `NotificationDeliveryState` has exactly the members `Pendente`, `Enviado`, `Falhou` e `Suprimida`.
- A successful channel call sets `State = Enviado` and `SentAt = DateTime.UtcNow`; a thrown exception sets `State = Falhou` and `Erro = ex.Message` truncated to 500 characters, and the loop continues with the next recipient.
- Per trigger one row exists per recipient per channel: a responsável with an e-mail produces an `InApp` row and an `Email` row; a responsável without an e-mail produces only the `InApp` row.

**Why:** V14 requires a delivery record; without it a failed e-mail is indistinguishable from a suppressed duplicate, which is exactly what the current console sender hides.

**Acceptance Criteria:**
- [ ] The entity declares the `NotificationDeliveryState` enum with the four states. [verify: grep-present `NotificationDeliveryState` `src/Ciclo.Core/Entities/NotificationDelivery.cs`]
- [ ] A failing e-mail channel leaves the row in `Falhou` with the error text stored. [verify: project-validation]
- [ ] A trigger for three responsáveis where one e-mail fails still records the in-app rows of all three. [verify: project-validation]
**Failure modes:**
- The channel call runs past the caller's token: the cancelled operation sets `State = Falhou` with the cancellation message instead of leaving the row in `Pendente`.
- The error message exceeds 500 characters: it is truncated before persistence so the column never overflows.

## Non-Functional Requirements

### NFR-001: Isolamento por tenant em execução sem escopo de requisição

**What:** The scheduled job runs outside any HTTP request, so tenant scope must be set explicitly. `NotificationDelivery` implements `ITenantScoped` like every other tenant entity, and `RunScheduledAsync` iterates tenants one at a time, calling `ITenantContext.SetTenant` for that tenant before its work, so no row is written with a tenant id taken from an ambient default.

**Acceptance Criteria:**
- [ ] `NotificationDelivery` implements `ITenantScoped`. [verify: grep-present `ITenantScoped` `src/Ciclo.Core/Entities/NotificationDelivery.cs`]
- [ ] A run over two tenants with pending documents writes each delivery row with its own tenant id and no row with the other tenant's id. [verify: project-validation]

### NFR-002: Execução limitada por ciclo e sobrevivência do laço

**What:** The scheduled loop is bounded per tick and cannot be killed by a single failing item. Each tick processes at most 500 candidate rows through `.Take(500)`, a per-item failure is caught and logged, and the loop keeps its interval regardless of how many items failed.

**Acceptance Criteria:**
- [ ] The job file uses `PeriodicTimer` for its interval. [verify: grep-present `PeriodicTimer` `src/Ciclo.Infrastructure/Notifications/ScheduledNotificationJob.cs`]
- [ ] A tick where every e-mail fails still completes and leaves the in-app rows recorded. [verify: project-validation]

## Constraints

- The `IEmailSender` contract stays byte-compatible: `Task SendAsync(string toEmail, string toName, string subject, string body)`. [verify: grep-present `Task SendAsync` `src/Ciclo.Infrastructure/Email/EmailSender.cs`]
- `Notification` keeps its tenant scoping and its existing type enum; the proactive path writes through it and does not replace it. [verify: grep-present `NotificationType` `src/Ciclo.Core/Entities/Notification.cs`]
- The existing e-mail hand-off stays: `NotificationService` keeps delegating to `IEmailDispatcher`. [verify: grep-present `IEmailDispatcher` `src/Ciclo.Infrastructure/Services/NotificationService.cs`]
- The scheduler decision is the project's own `BackgroundService`, as already recorded in `management/arch.md`; no external scheduler package is added. [verify: grep-present `BackgroundService` `src/Ciclo.Infrastructure/Notifications/ScheduledNotificationJob.cs`]
- The `§6-3` scheduling decision referenced by V14 is resolved by this spec as the own-`BackgroundService` choice above, so V14's open decision does not remain open. [verify: grep-present `PeriodicTimer` `src/Ciclo.Infrastructure/Notifications/ScheduledNotificationJob.cs`]
- Full project validation passes. [verify: project-validation]

## Edge Cases & Error States

- Student with no `StudentParent` row: no notification and no delivery row are created; the run continues with the next student.
- Responsável whose `User.Email` is null or whitespace: only the `InApp` channel is attempted and no `Email` row is created, so the absence is not recorded as a failure.
- Document with `DataValidade` equal to `nowUtc`: inside the window and produces the trigger.
- Document with `DataValidade` exactly `nowUtc.AddDays(30)`: inside the window, inclusive upper bound.
- Document with `DataValidade` in the past: not a vencimento candidate; this spec does not notify already-expired documents.
- `EnrollmentPeriod` inactive (`IsActive == false`): neither `RematriculaAberta` nor `RematriculaPrazoFinal` is produced.
- Period whose `DataFim` is in the past: no prazo trigger.
- The same scheduled tick running twice within the same day: the date part of the idempotency key makes the second run suppress both channels.
- SMTP failure for one recipient: that recipient's `Email` row is `Falhou`; the in-app rows and the remaining recipients are unaffected.
- Database unavailable at tick time: the tick's exception is logged and the loop waits for the next interval.
- Unknown locale passed by a caller: the `pt-BR` template is used; no exception and no empty body.
- A `NotificationTriggerKind` member added later without a catalog entry: `Resolve` throws `InvalidOperationException` naming the member, so the missing template is discovered at the first dispatch.
- The proactive dispatch raised from a domain service throws: the domain service catches it, logs at error level and still returns success, because the status change is already committed and must not be reported as failed.

## Anti-Scope

- No external scheduler is introduced: the job host is the project's own `BackgroundService`. [verify: grep-absent `Hangfire` `src/`]
- No Quartz package or integration is introduced. [verify: grep-absent `Quartz` `src/`]
- No new API endpoint is added for proactive notifications; the existing inbox remains the surface. [verify: file-absent `src/Ciclo.Api/Controllers/ProactiveNotificationController.cs`]
- No separate persisted schedule entity is created; the due set is derived from the existing entities plus the delivery record. [verify: file-absent `src/Ciclo.Core/Entities/ScheduledNotification.cs`]
- No second e-mail abstraction is added on top of `IEmailSender`. [verify: file-absent `src/Ciclo.Infrastructure/Email/EmailProviderAbstraction.cs`]

## Dependencies

- Spec 70 (notificações in-app) — supplies `Notification`, `UserNotification` and `INotificationService`, reused unchanged.
- Spec 80 (e-mail dispatch) — supplies `IEmailSender`, `ConsoleEmailSender` and `IEmailDispatcher`; this spec adds the SMTP implementation behind the same interface.
- `management/arch.md` — the background-job decision (own `BackgroundService`, no external scheduler) is taken from there, not re-decided.
- Existing entities `Enrollment`, `EnrollmentPeriod`, `Document` and `StudentParent` — supply the due set and the recipients.

## Out of Scope

- Push notifications, SMS and WhatsApp channels.
- Per-tenant template editing, template storage in the database, or an admin screen for templates.
- A retry/backoff policy for failed e-mails beyond the next scheduled tick re-evaluating the same candidate.
- Additional locales beyond the `pt-BR` catalog with fallback.
- Changes to the existing endpoints, DTOs or the inbox UI of spec 70.
- A new ADR entry for the scheduling decision: the decision is recorded in this spec's `design.md` and in `management/arch.md` as it already stands.

## Test Scope

- `tests/Ciclo.Api.Tests/ProactiveNotificationTests.cs` — proves every acceptance criterion marked `project-validation`: the five event triggers, the three scheduled triggers and their window boundaries, template resolution and fallback, tenant isolation, idempotent suppression of duplicates, and delivery-state recording with a failing `IEmailSender` double.
