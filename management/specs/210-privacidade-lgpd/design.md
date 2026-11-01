# Spec 210: Privacidade e LGPD — Design

## Design Approach

The spec extends the owners that already exist in the solution instead of introducing a privacy subsystem:

- persisted state lives in `Ciclo.Core` entities next to `Student`, `Enrollment`, `Grade` and `Document`;
- behavior lives in `Ciclo.Infrastructure/Services` next to `StudentService`, with an interface and its implementation declared in the same file, exactly like the existing services;
- transport shapes live in `Ciclo.Infrastructure/Contracts` next to `StudentDtos`;
- HTTP lives in `Ciclo.Api/Controllers` following the `[ApiController]` + `[Route("api/privacy")]` + `[Authorize(Roles = ...)]` + `ProblemResponse.FromCode` convention of `StudentController`;
- the access rule keeps its single owner, `AccessPolicy.Roles`, which already feeds both the controller attribute and the in-service check;
- the sensitive access trail keeps its single owner, `AuditActions`, `AuditTargetTypes` and `IAuditTrail`, introduced by spec 110.

Each capability gets one service with one public reason to change: `ConsentService` owns consent state, `AnonymizationService` owns the identity overwrite, `PrivacyExportService` owns the export projection and `PrivacyRequestService` owns the request state machine. The request service delegates the actual work to the other three, so the state machine never duplicates the projection or the overwrite.

Errors are raised as `PrivacyException` (stable code in `Message`, HTTP status in `StatusCode`), mirroring `StudentException`. The controller catches it and answers through `ProblemResponse.FromCode`, the existing single translator of a domain code into the canonical problem response.

## Architecture Decisions

**AD-001 — Anonymization is an in-place overwrite, never a delete.** Deleting the `Student` row would break the `Enrollment`, `Grade`, `Document` and `StudentParent` foreign keys and contradict the retention window named by V21. The row survives with its keys; only the identifying fields are replaced.

**AD-002 — The audit entry of an anonymization carries no original value.** `AuditEntry` is immutable by design under spec 110. Recording the pre-anonymization `Nome` or `Cpf` in `Before` would persist forever exactly the personal data the operation is meant to erase, so anonymity would never be reached. The entry therefore carries `null` in `Before` and a marker object with the `anonymizedAt` instant in `After`.

**AD-003 — Reuse the spec 110 trail.** Only two constant files are extended and every write goes through `IAuditTrail`. No second table, no second query surface, no parallel writer.

**AD-004 — The export is built in memory and streamed.** `PrivacyExportService` returns the DTO graph; the controller serializes it into a `MemoryStream`. There is no temp file, no storage call and no partial artifact.

**AD-005 — Legal hold is evaluated at execution time.** `LegalHold` lives on `PrivacyRequest` and `AnonymizationService` consults it immediately before the overwrite. A hold added after approval therefore still blocks, and an approval is never silently converted into an execution.

**AD-006 — Consent uniqueness is enforced twice.** The service resolves by `(TenantId, SubjectType, SubjectId, Purpose)` and updates in place; `AppDbContext.OnModelCreating` declares a unique index on the same tuple, so a concurrent insert cannot create a second row.

**AD-007 — No new role.** The rules are added as constants in `AccessPolicy.Roles` (`PrivacyRead = "Admin,Staff,Parent"`, `PrivacyManage = "Admin"`, `PrivacyConsentWrite = "Admin,Staff"`), reusing the existing `Admin`, `Staff` and `Parent` roles.

**AD-008 — Retention is recorded, not executed.** The deadline is stored in `PrivacyRequest.RetainUntil` at decision time and surfaced in the export. No scheduler exists in this spec; execution of the deadline is out of scope.

## Component / Module Breakdown

### Component: ConsentRecord entity

**Purpose:** Persist the lawful basis held for one subject and one purpose, together with its revocation history.
**Interface:** `src/Ciclo.Core/Entities/ConsentRecord.cs`
```csharp
public class ConsentRecord : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public ConsentSubjectType SubjectType { get; set; }
    public Guid SubjectId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string LegalBasis { get; set; } = string.Empty;
    public ConsentStatus Status { get; set; } = ConsentStatus.Granted;
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public Guid? GrantedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Tenant Tenant { get; set; } = null!;
}

public enum ConsentSubjectType { Student = 0, Guardian = 1 }
public enum ConsentStatus { Granted = 0, Revoked = 1 }
```
**Internal Logic:** Plain persistence model; no behavior in the entity.
**Dependencies:** `ITenantScoped`, `Tenant`.
**Error Handling:** None at the entity level; constraints are declared in `AppDbContext.OnModelCreating` — `Purpose` and `LegalBasis` required with max length 100, unique index on `(TenantId, SubjectType, SubjectId, Purpose)`.

### Component: PrivacyRequest entity

**Purpose:** Persist one guardian or admin request and its lifecycle through decision and completion.
**Interface:** `src/Ciclo.Core/Entities/PrivacyRequest.cs`
```csharp
public class PrivacyRequest : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public PrivacyRequestType Type { get; set; }
    public PrivacyRequestStatus Status { get; set; } = PrivacyRequestStatus.Submitted;
    public Guid SubjectStudentId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public string? Reason { get; set; }
    public string? Resolution { get; set; }
    public bool LegalHold { get; set; }
    public DateTime? RetainUntil { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Tenant Tenant { get; set; } = null!;
}

public enum PrivacyRequestType { Export = 0, Anonymization = 1, ConsentRevocation = 2 }
public enum PrivacyRequestStatus { Submitted = 0, Approved = 1, Rejected = 2, Completed = 3 }
```
**Internal Logic:** Plain persistence model; no behavior in the entity.
**Dependencies:** `ITenantScoped`, `Tenant`.
**Error Handling:** None at the entity level; `AppDbContext.OnModelCreating` declares `Reason` and `Resolution` max length 500, a foreign key to `Students.Id` and an index on `(TenantId, Status)`.

### Component: Student identity fields

**Purpose:** Give the anonymization a durable, queryable marker on the existing student row.
**Interface:** one new property in `src/Ciclo.Core/Entities/Student.cs`
```csharp
public DateTime? AnonymizedAt { get; set; }
```
**Internal Logic:** No behavior; the value is written only by `AnonymizationService`.
**Dependencies:** none.
**Error Handling:** A null value means the row still holds the real identity.

### Component: PrivacyException

**Purpose:** Carry a stable domain code and its HTTP status out of the privacy services, in the shape the controller already translates.
**Interface:** `src/Ciclo.Infrastructure/Services/PrivacyException.cs`
```csharp
public class PrivacyException : Exception
{
    public int StatusCode { get; }
    public PrivacyException(string code, int statusCode) : base(code) => StatusCode = statusCode;
}

public sealed class PrivacyLegalHoldException : PrivacyException
{
    public PrivacyLegalHoldException() : base("legal_hold_active", 409) { }
}
```
**Internal Logic:** Constructor only; the code travels in `Message`, so `ProblemResponse.FromCode(ex.Message, ex.StatusCode)` reproduces it verbatim.
**Dependencies:** none.
**Error Handling:** Not applicable; the type is the error channel. Codes used across the spec: `invalid_request` (400), `forbidden` (403), `not_found` (404), `invalid_state` (409), `legal_hold_active` (409).

### Component: ConsentService

**Purpose:** Record, update, list and revoke the consent of one subject for one purpose.
**Interface:** `src/Ciclo.Infrastructure/Services/ConsentService.cs`
```csharp
public interface IConsentService
{
    Task<ConsentDto> GrantAsync(Guid tenantId, ConsentSubjectType subjectType, Guid subjectId,
        string purpose, string legalBasis, Guid? grantedByUserId, CancellationToken ct = default);
    Task<ConsentDto?> RevokeAsync(Guid tenantId, Guid consentId, CancellationToken ct = default);
    Task<IReadOnlyList<ConsentDto>> ListBySubjectAsync(Guid tenantId, ConsentSubjectType subjectType,
        Guid subjectId, CancellationToken ct = default);
}

public class ConsentService : IConsentService { }
```
**Internal Logic:**
1. `GrantAsync` trims `purpose` and `legalBasis` and rejects an empty value with `PrivaceException` code `invalid_request` and status 400.
2. Resolves the subject inside the tenant — `Students` when `SubjectType` is `Student`, `Users` when `Guardian` — and throws `KeyNotFoundException` on an empty result.
3. Finds the existing row by `(TenantId, SubjectType, SubjectId, Purpose)`; when found it assigns the new `LegalBasis`, sets `Status = Granted`, sets `RevokedAt = null` and keeps `Id` and `GrantedAt`; when absent it inserts a new row with `GrantedAt = DateTime.UtcNow`.
4. Calls `SaveChangesAsync`, then `IAuditTrail.Record(new AuditEntry(AuditActions.ConsentGranted, targetType, subjectId, null, dto))`.
5. `RevokeAsync` loads by `Id` and `TenantId`; a missing row returns `null`; a row already `Revoked` returns the current DTO with no `SaveChangesAsync` and no audit call.
6. `ListBySubjectAsync` returns the rows of the subject ordered by `Purpose`, projected to `ConsentDto`.
**Dependencies:** `AppDbContext`, `IAuditTrail`, `AuditActions`, `AuditTargetTypes`.
**Error Handling:** `invalid_request` 400 for an empty field; `KeyNotFoundException` 404 for an unknown subject; unknown consent id returns `null` and the controller answers 404.

### Component: AnonymizationService

**Purpose:** Overwrite the identity of one student atomically, honouring legal hold and idempotency.
**Interface:** `src/Ciclo.Infrastructure/Services/AnonymizationService.cs`
```csharp
public interface IAnonymizationService
{
    Task<StudentExportDto> AnonymizeStudentAsync(Guid tenantId, Guid studentId, CancellationToken ct = default);
}

public class AnonymizationService : IAnonymizationService { }
```
**Internal Logic:**
1. Opens `await using var tx = await _db.Database.BeginTransactionAsync(ct);`.
2. Loads the student by `Id` and `TenantId`; a missing row throws `KeyNotFoundException`.
3. When `AnonymizedAt` is not null, returns the current projection without saving and without an audit call — the no-op path.
4. Queries `PrivacyRequests` for `SubjectStudentId == studentId` with `LegalHold` true and a status other than `Rejected` or `Completed`; a non-empty result throws `PrivacyLegalHoldException` before any assignment.
5. Assigns `Nome = "Titular anonimizado"`, `Cpf = null`, `Foto = null`, `Observacoes = null`, `DataNascimento = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)`, `AnonymizedAt = DateTime.UtcNow`, `UpdatedAt = DateTime.UtcNow`.
6. Calls `SaveChangesAsync`, records `IAuditTrail.Record(new AuditEntry(AuditActions.PrivacyAnonymized, AuditTargetTypes.Student, studentId, null, new { identity = "anonymized", anonymizedAt }))` — never the previous values, per AD-002 — and commits the transaction.
**Dependencies:** `AppDbContext`, `IAuditTrail`.
**Error Handling:** legal hold throws `PrivacyLegalHoldException` (409) with the transaction disposed uncommitted, so every field keeps its pre-call value; an unknown student throws `KeyNotFoundException` (404); a persistence failure rolls the transaction back and rethrows.

### Component: PrivacyExportService

**Purpose:** Assemble the data subject document as a read-only projection with a fixed section set.
**Interface:** `src/Ciclo.Infrastructure/Services/PrivacyExportService.cs`
```csharp
public interface IPrivacyExportService
{
    Task<PrivacyExportDto> ExportStudentAsync(Guid tenantId, Guid studentId, CancellationToken ct = default);
}

public class PrivacyExportService : IPrivacyExportService { }
```
**Internal Logic:**
1. Loads the student by `Id` and `TenantId`; a missing row throws `KeyNotFoundException`.
2. Loads guardians through `StudentParents` joined to `Users`, projecting only `Id`, `Name`, `Email`, `Role`, `IsActive` and `CreatedAt`, ordered by `Id`.
3. Loads `Enrollments`, `Grades` joined to `Assessment` for `Title`, `Documents`, the `ConsentRecords` of the student subject and the `PrivacyRequests` of the student, each filtered by `TenantId` and ordered by its creation field, so two runs over the same data produce the same order.
4. Projects every section to a `List<T>`; a section with no rows is an empty list, never null.
5. Returns `new PrivacyExportDto(DateTime.UtcNow, student, guardians, enrollments, grades, documents, consents, requests)`; `GeneratedAt` is the only field that differs between two runs over the same data.
**Dependencies:** `AppDbContext`.
**Error Handling:** an unknown or other-tenant student throws `KeyNotFoundException` (404); the component has no write path, so it has no rollback surface; a serialization failure propagates and no partial body is sent.

### Component: PrivacyRequestService

**Purpose:** Own the request state machine and delegate execution to the export, anonymization and consent owners.
**Interface:** `src/Ciclo.Infrastructure/Services/PrivacyRequestService.cs`
```csharp
public interface IPrivacyRequestService
{
    Task<PrivacyRequestDto> SubmitAsync(Guid tenantId, Guid requesterUserId, string requesterRole,
        CreatePrivacyRequestRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PrivacyRequestDto>> ListAsync(Guid tenantId, Guid requesterUserId,
        string requesterRole, CancellationToken ct = default);
    Task<PrivacyRequestDto> DecideAsync(Guid tenantId, Guid requestId,
        DecidePrivacyRequestRequest decision, CancellationToken ct = default);
    Task<PrivacyRequestCompletionDto> CompleteAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);
}

public class PrivacyRequestService : IPrivacyRequestService { }
```
**Internal Logic:**
1. `SubmitAsync` parses `request.Type` into `PrivacyRequestType` and throws `invalid_request` 400 when the name is unknown. It resolves the student in the tenant (`KeyNotFoundException` when absent) and, when `AccessPolicy.Allows(AccessPolicy.Roles.PrivacyManage, role)` is false, requires a `StudentParents` row for that caller — otherwise `PrivacyException("forbidden", 403)`. It inserts the row with `Status = Submitted` and returns the DTO.
2. `ListAsync` filters by `TenantId`, and for a caller without `PrivacyManage` additionally by `RequestedByUserId == requesterUserId`; ordered by `SubmittedAt` descending.
3. `DecideAsync` loads the row by `Id` and `TenantId` (`KeyNotFoundException` when absent); a status other than `Submitted` throws `invalid_state` 409. It parses `decision.Decision` into `Approved` or `Rejected` from the literals `approve` and `reject` (`invalid_request` 400 otherwise), assigns `Status`, `Resolution`, `LegalHold`, `RetainUntil` and `DecidedAt`, saves, then records one `PrivacyRequestDecided` audit entry.
4. `CompleteAsync` loads the row; a status other than `Approved` throws `invalid_state` 409. For `Type == Anonymization` with `LegalHold` true it throws `PrivacyLegalHoldException` and the request stays `Approved`. It then executes the delegated owner — `Export` through `PrivacyExportService`, `Anonymization` through `AnonymizationService`, `ConsentRevocation` through `ConsentService.RevokeAsync` for every granted consent of the student — assigns `Status = Completed` and `CompletedAt = DateTime.UtcNow`, saves, and records one `PrivacyRequestDecided` audit entry.
**Dependencies:** `AppDbContext`, `IAuditTrail`, `AccessPolicy`, `IPrivacyExportService`, `IAnonymizationService`, `IConsentService`.
**Error Handling:** every refusal happens before the first assignment, so a rejected call leaves the row byte-identical. Codes: `invalid_request` 400, `forbidden` 403, `not_found` 404, `invalid_state` 409, `legal_hold_active` 409.

### Component: PrivacyDtos

**Purpose:** Fix the wire shape of every privacy request and response in one place.
**Interface:** `src/Ciclo.Infrastructure/Contracts/PrivacyDtos.cs`
```csharp
public sealed record ConsentDto(Guid Id, string SubjectType, Guid SubjectId, string Purpose,
    string LegalBasis, string Status, DateTime GrantedAt, DateTime? RevokedAt);

public sealed record GrantConsentRequest(Guid SubjectId, string SubjectType, string Purpose, string LegalBasis);

public sealed record StudentExportDto(Guid Id, string Nome, DateTime DataNascimento, string? Cpf,
    string Turma, int AnoLetivo, string Status, string? Observacoes, string? Foto,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? AnonymizedAt);

public sealed record GuardianExportDto(Guid Id, string Name, string Email, string Role,
    bool IsActive, DateTime CreatedAt);

public sealed record EnrollmentExportDto(Guid Id, string Status, DateTime CreatedAt, DateTime? ApprovedAt);

public sealed record GradeExportDto(Guid Id, Guid AssessmentId, string AssessmentTitle, decimal Value,
    DateTime RecordedAt);

public sealed record DocumentExportDto(Guid Id, string NomeArquivo, string ContentType, long TamanhoBytes,
    string Status, DateTime CreatedAt);

public sealed record PrivacyExportDto(DateTime GeneratedAt, StudentExportDto Student,
    IReadOnlyList<GuardianExportDto> Guardians, IReadOnlyList<EnrollmentExportDto> Enrollments,
    IReadOnlyList<GradeExportDto> Grades, IReadOnlyList<DocumentExportDto> Documents,
    IReadOnlyList<ConsentDto> Consents, IReadOnlyList<PrivacyRequestDto> Requests);

public sealed record PrivacyRequestDto(Guid Id, string Type, string Status, Guid SubjectStudentId,
    Guid RequestedByUserId, string? Reason, string? Resolution, bool LegalHold, DateTime? RetainUntil,
    DateTime SubmittedAt, DateTime? DecidedAt, DateTime? CompletedAt);

public sealed record CreatePrivacyRequestRequest(Guid StudentId, string Type, string? Reason);

public sealed record DecidePrivacyRequestRequest(string Decision, string? Resolution, bool LegalHold,
    DateTime? RetainUntil);

public sealed record PrivacyRequestCompletionDto(PrivacyRequestDto Request, PrivacyExportDto? Export);
```
**Internal Logic:** Pure data. Enums travel as their string names, so the wire contract stays stable when a numeric value is appended.
**Dependencies:** none.
**Error Handling:** Not applicable.

### Component: AccessPolicy extension

**Purpose:** Keep the who-may-do-what rule in its single owner.
**Interface:** three constants added inside `AccessPolicy.Roles` in `src/Ciclo.Infrastructure/Security/AccessPolicy.cs`
```csharp
public const string PrivacyRead = "Admin,Staff,Parent";
public const string PrivacyManage = "Admin";
public const string PrivacyConsentWrite = "Admin,Staff";
```
**Internal Logic:** No behavior; the constants feed both `[Authorize(Roles = ...)]` and `AccessPolicy.Require(...)` inside the services.
**Dependencies:** none.
**Error Handling:** `AccessPolicy.Require` already throws `ForbiddenException`, translated to 403 by `ExceptionMiddleware`.

### Component: PrivacyController

**Purpose:** Expose the eight routes and translate domain errors into the canonical problem response.
**Interface:** `src/Ciclo.Api/Controllers/PrivacyController.cs`
```csharp
[ApiController]
[Authorize]
[Route("api/privacy")]
public class PrivacyController : ControllerBase
{
    [HttpPost("requests")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyRead)]
    public Task<IActionResult> Submit([FromBody] CreatePrivacyRequestRequest request, CancellationToken ct);

    [HttpGet("requests")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyRead)]
    public Task<IActionResult> List(CancellationToken ct);

    [HttpPost("requests/{id:guid}/decision")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyManage)]
    public Task<IActionResult> Decide(Guid id, [FromBody] DecidePrivacyRequestRequest decision, CancellationToken ct);

    [HttpPost("requests/{id:guid}/complete")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyManage)]
    public Task<IActionResult> Complete(Guid id, CancellationToken ct);

    [HttpGet("students/{studentId:guid}/export")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyManage)]
    public Task<IActionResult> Export(Guid studentId, CancellationToken ct);

    [HttpPost("consents")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyConsentWrite)]
    public Task<IActionResult> GrantConsent([FromBody] GrantConsentRequest request, CancellationToken ct);

    [HttpGet("consents")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyRead)]
    public Task<IActionResult> ListConsents([FromQuery] Guid subjectId, [FromQuery] string subjectType, CancellationToken ct);

    [HttpPost("consents/{id:guid}/revoke")]
    [Authorize(Roles = AccessPolicy.Roles.PrivacyConsentWrite)]
    public Task<IActionResult> RevokeConsent(Guid id, CancellationToken ct);
}
```
**Internal Logic:** Every action resolves the tenant from `HttpContext.Items["TenantId"]` and the caller from the `ClaimTypes.NameIdentifier` and `ClaimTypes.Role` claims, exactly as `StudentController.GetUserInfo()` and `GetTenantId()` do. `Export` serializes the DTO with `JsonSerializer.SerializeToUtf8Bytes`, wraps it in a `MemoryStream` and returns `File(stream, "application/json", $"privacy-export-{studentId}.json")`. Every action wraps its service call in `try { } catch (PrivacyException ex) { return ProblemResponse.FromCode(ex.Message, ex.StatusCode); }`.
**Dependencies:** `IConsentService`, `IPrivacyRequestService`, `IPrivacyExportService`, `ProblemResponse`.
**Error Handling:** `PrivacyException` becomes `ProblemResponse.FromCode`; `KeyNotFoundException` and any unhandled exception go to `ExceptionMiddleware` as 404 and 500; model-binding failure becomes the `[ApiController]` 400 problem response.

### Component: Privacy admin and guardian pages

**Purpose:** Make the request flow and the consent state usable in the browser.
**Interface:** `frontend/src/pages/admin/PrivacyRequests.tsx` exports `default function PrivacyRequests()`; `frontend/src/pages/parent/PrivacyCenter.tsx` exports `default function PrivacyCenter()`. Both are registered in `frontend/src/App.tsx` as `<Route path="/admin/privacy" element={<PrivacyRequests />} />` in the admin area and `<Route path="/parent/privacy" element={<PrivacyCenter />} />` in the parent area.
**Internal Logic:** `PrivacyRequests` fetches `GET /api/privacy/requests`, renders one row per request with type, status, subject, `LegalHold` and `RetainUntil`, and exposes approve, reject and complete actions that post to the decision and completion routes and re-fetch the list afterwards. `PrivacyCenter` fetches the linked children from the existing parent endpoints, fetches `GET /api/privacy/consents` per child, renders the consents with their status, disables the submit action when an open request of the same type already exists for that child, and downloads the export from a `Blob` of `GET /api/privacy/students/{studentId}/export`.
**Dependencies:** the shared components `Card`, `PageHeader`, `Table`, `Badge`, `Button`, `EmptyState`, the `useAuth` context and the existing API base convention.
**Error Handling:** a non-2xx answer reads the problem `detail` or `code` from the body, surfaces it and leaves the previously rendered list untouched; a failed export download surfaces the failure and creates no file.

## Data Flow

**Consent grant:** `POST /api/privacy/consents` → `PrivacyController.GrantConsent` resolves the tenant and the caller → `ConsentService.GrantAsync` validates the input, resolves the subject in the tenant and upserts the row → `SaveChangesAsync` → `IAuditTrail.Record(ConsentGranted)` → `ConsentDto` → `200 OK`.

**Consent revoke:** `POST /api/privacy/consents/{id}/revoke` → `ConsentService.RevokeAsync` loads by id and tenant → an already revoked row returns the DTO with no save and no audit → otherwise it stamps `Status` and `RevokedAt`, saves and records `ConsentRevoked` → `200 OK`; an unknown id returns null → `404`.

**Request submission:** `POST /api/privacy/requests` → `PrivacyRequestService.SubmitAsync` parses `Type`, resolves the student in the tenant and checks the linkage when the caller lacks `PrivacyManage` → inserts the `PrivacyRequest` with `Status = Submitted` → `PrivacyRequestDto` → `201 Created`.

**Decision:** `POST /api/privacy/requests/{id}/decision` → loads the row in the tenant → a status other than `Submitted` answers 409 → assigns `Status`, `Resolution`, `LegalHold`, `RetainUntil` and `DecidedAt` → `SaveChangesAsync` → `IAuditTrail.Record(PrivacyRequestDecided)` → `200 OK`.

**Completion:** `POST /api/privacy/requests/{id}/complete` → loads the row → a status other than `Approved` answers 409 → for `Anonymization` a legal hold answers 409 and the row stays `Approved` → the delegated owner executes → assigns `Completed` and `CompletedAt` → `SaveChangesAsync` → `IAuditTrail.Record(PrivacyRequestDecided)` → `PrivacyRequestCompletionDto`.

**Export:** `GET /api/privacy/students/{studentId}/export` → `PrivacyExportService.ExportStudentAsync` reads the student, the guardians through `StudentParents`, the enrollments, the grades, the documents, the consents and the requests, every read filtered by `TenantId` → assembles `PrivacyExportDto` → the controller serializes it to UTF-8 JSON and streams it as `application/json` → `IAuditTrail.Record(PrivacyExported)`.

**Anonymization:** `AnonymizationService.AnonymizeStudentAsync` opens a transaction, loads the student in the tenant, returns unchanged when `AnonymizedAt` is set, refuses on legal hold, overwrites the five identity fields, saves, records `PrivacyAnonymized` with the marker payload only, and commits.

**After the overwrite:** every later read — the student list, the export, the report card — reads the overwritten row, so no path can surface the original identity after the commit.

## Data Structures

**`ConsentRecord`** — `Id` uniqueidentifier primary key; `TenantId` foreign key to `Tenants.Id`; `SubjectType` integer; `SubjectId` uniqueidentifier with no foreign key, because it points at `Students` or `Users` according to `SubjectType`; `Purpose` varchar(100) not null; `LegalBasis` varchar(100) not null; `Status` integer; `GrantedAt` timestamp; `RevokedAt` timestamp null; `GrantedByUserId` uniqueidentifier null; `CreatedAt` timestamp. Unique index `IX_ConsentRecords_Tenant_Subject_Purpose` on `(TenantId, SubjectType, SubjectId, Purpose)`.

**`PrivacyRequest`** — `Id` uniqueidentifier primary key; `TenantId` foreign key to `Tenants.Id`; `Type` integer; `Status` integer; `SubjectStudentId` uniqueidentifier foreign key to `Students.Id`; `RequestedByUserId` uniqueidentifier with no foreign key, so the request survives the removal of the account that opened it, the criterion spec 110 already uses; `Reason` varchar(500) null; `Resolution` varchar(500) null; `LegalHold` boolean not null default false; `RetainUntil` timestamp null; `SubmittedAt` timestamp; `DecidedAt` timestamp null; `CompletedAt` timestamp null. Index `IX_PrivacyRequests_Tenant_Status` on `(TenantId, Status)`.

**`Student`** — gains `AnonymizedAt` timestamp null; every existing column keeps its type and nullability.

**`PrivacyExportDto`** — JSON object with the keys in this order: `generatedAt`, `student`, `guardians`, `enrollments`, `grades`, `documents`, `consents`, `requests`; the six collection keys are always arrays.

## File / Module Layout

### Created

- `src/Ciclo.Core/Entities/ConsentRecord.cs` — entity plus `ConsentSubjectType` and `ConsentStatus`.
- `src/Ciclo.Core/Entities/PrivacyRequest.cs` — entity plus `PrivacyRequestType` and `PrivacyRequestStatus`.
- `src/Ciclo.Infrastructure/Contracts/PrivacyDtos.cs` — every request and response record of this spec.
- `src/Ciclo.Infrastructure/Services/PrivacyException.cs` — `PrivacyException` and `PrivacyLegalHoldException`.
- `src/Ciclo.Infrastructure/Services/ConsentService.cs` — `IConsentService` and `ConsentService`.
- `src/Ciclo.Infrastructure/Services/AnonymizationService.cs` — `IAnonymizationService` and `AnonymizationService`.
- `src/Ciclo.Infrastructure/Services/PrivacyExportService.cs` — `IPrivacyExportService` and `PrivacyExportService`.
- `src/Ciclo.Infrastructure/Services/PrivacyRequestService.cs` — `IPrivacyRequestService` and `PrivacyRequestService`.
- `src/Ciclo.Api/Controllers/PrivacyController.cs` — the eight routes.
- `frontend/src/pages/admin/PrivacyRequests.tsx` — admin request list and actions.
- `frontend/src/pages/parent/PrivacyCenter.tsx` — guardian consent and request view.
- `tests/Ciclo.Api.Tests/PrivacyConsentTests.cs` — consent lifecycle proof.
- `tests/Ciclo.Api.Tests/PrivacyAnonymizationTests.cs` — irreversibility and legal hold proof.
- `tests/Ciclo.Api.Tests/PrivacyExportTests.cs` — export completeness proof.
- `tests/Ciclo.Api.Tests/PrivacyRequestApiTests.cs` — request lifecycle, authorization and audit proof.

### Modified

- `src/Ciclo.Core/Entities/Student.cs` — adds `AnonymizedAt`.
- `src/Ciclo.Core/Auditing/AuditActions.cs` — adds the five privacy action constants.
- `src/Ciclo.Core/Auditing/AuditTargetTypes.cs` — adds `Student` and `PrivacyRequest`.
- `src/Ciclo.Infrastructure/Security/AccessPolicy.cs` — adds `PrivacyRead`, `PrivacyManage` and `PrivacyConsentWrite`.
- `src/Ciclo.Infrastructure/Data/AppDbContext.cs` — adds the two `DbSet` properties and their model configuration.
- `src/Ciclo.Api/Program.cs` — registers the four privacy services as scoped.
- `frontend/src/App.tsx` — registers the two pages in the existing router.

### Migration

One EF migration named `AddPrivacyLgpd`, added to the `Migrations/` folder of the Ciclo.Infrastructure project, creating the two tables, the two indexes and the `Student.AnonymizedAt` column.

## Testing Strategy

All tests live in the existing xUnit project `tests/Ciclo.Api.Tests` and follow its current arrangement: an `AppDbContext` built through the existing `ApiTestHost` helper, a `NoopAuditTrail` or a capturing double from `TestDoubles`, and direct service calls plus HTTP-level calls through the test host.

**`tests/Ciclo.Api.Tests/PrivacyConsentTests.cs`** — proves the single-row rule (a second grant on the same subject and purpose leaves the row count at one and replaces `LegalBasis`), the revoke stamping (`Status` and `RevokedAt` set, `GrantedAt` and `LegalBasis` untouched), the no-op revoke (no save, no audit call), the empty-field rejection and the unknown-subject rejection.

**`tests/Ciclo.Api.Tests/PrivacyAnonymizationTests.cs`** — proves irreversibility (after the call no original `Nome`, `Cpf`, `Foto`, `Observacoes` or `DataNascimento` value survives and the sentinel values are present), referential integrity (`StudentParents`, `Enrollment`, `Grade` and `Document` rows are unchanged and still point at the same `Student.Id`), legal hold refusal (the row is identical before and after, and the exception is `PrivacyLegalHoldException`), idempotency (a second call leaves `AnonymizedAt` unchanged and records no second audit entry) and the audit payload rule (no original identity value appears anywhere in the recorded entry).

**`tests/Ciclo.Api.Tests/PrivacyExportTests.cs`** — proves export completeness (a fully populated student yields non-empty `guardians`, `enrollments`, `grades`, `documents` and `consents`), the seven invariant sections on an otherwise empty student, empty arrays instead of null, the absence of `PasswordHash` and `CaminhoArquivo` in the serialized JSON, the anonymized-student projection, and the identical-output rule apart from `GeneratedAt`.

**`tests/Ciclo.Api.Tests/PrivacyRequestApiTests.cs`** — proves the state machine (`Submitted` to `Approved` and to `Rejected`, 409 on a second decision, 409 on completing a request that is not approved), guardian linkage (403 for an unlinked student), tenant isolation (404 for a row of another tenant), role enforcement (403 for a guardian token on the admin routes), the completion path of each of the three types and the once-per-operation audit entry.

## Migration / Rollback

**Migration** — the EF migration `AddPrivacyLgpd` creates `ConsentRecords` and `PrivacyRequests`, their two indexes and the nullable `Students.AnonymizedAt` column. Every change is additive and nullable, so an existing database migrates without a backfill and without touching existing rows.

**Rollback** — the migration is reversible: updating the database to the previous migration drops the two tables and the column, and the previous application version keeps working because no existing table, column or contract was modified. An executed anonymization is not reversible by design (AD-001 and AD-002); rolling the schema back does not restore identity data, and the spec states that explicitly instead of pretending otherwise.
