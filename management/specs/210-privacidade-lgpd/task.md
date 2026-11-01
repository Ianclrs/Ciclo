<!-- PIPELINE-CONTRACT: Task completion is determined by "**Status:** [x] done" at the end of each "### Task N" section.
     Checkboxes "- [ ]" under "**Validation:**" are human verification guides — the pipeline ignores them completely.
     To mark a task as done, add "**Status:** [x] done". To mark it as pending, use "**Status:** [ ] pending". -->
# Spec 210: Privacidade e LGPD — Implementation Tasks

## Planned Scaffolds

- `src/Ciclo.Core/Entities/ConsentRecord.cs`
- `src/Ciclo.Core/Entities/PrivacyRequest.cs`
- `src/Ciclo.Infrastructure/Contracts/PrivacyDtos.cs`
- `src/Ciclo.Infrastructure/Services/PrivacyException.cs`
- `src/Ciclo.Infrastructure/Services/ConsentService.cs`
- `src/Ciclo.Infrastructure/Services/AnonymizationService.cs`
- `src/Ciclo.Infrastructure/Services/PrivacyExportService.cs`
- `src/Ciclo.Infrastructure/Services/PrivacyRequestService.cs`
- `src/Ciclo.Api/Controllers/PrivacyController.cs`
- `frontend/src/pages/admin/PrivacyRequests.tsx`
- `frontend/src/pages/parent/PrivacyCenter.tsx`
- `tests/Ciclo.Api.Tests/PrivacyConsentTests.cs`
- `tests/Ciclo.Api.Tests/PrivacyExportTests.cs`
- `tests/Ciclo.Api.Tests/PrivacyAnonymizationTests.cs`
- `tests/Ciclo.Api.Tests/PrivacyRequestApiTests.cs`

## Task Order

Tasks MUST be executed sequentially in numerical order. Each task depends on the completion of all preceding tasks.

## Tasks

### Task 1: Domain model, audit vocabulary and schema

**Objective:** Create the two new entities, extend the student row, extend the audit vocabulary and the access rule constants, register both entities in the DbContext, and deliver the EF migration.
**Requirements Covered:** FR-001, FR-003, FR-004, FR-005, NFR-002
**Design References:** Component: ConsentRecord entity; Component: PrivacyRequest entity; Component: Student identity fields; Component: AccessPolicy extension; Architecture Decisions AD-001, AD-003, AD-006, AD-007; Data Structures.
**Actions:**
1. Create `src/Ciclo.Core/Entities/ConsentRecord.cs` with the `ConsentRecord : ITenantScoped` class, the `ConsentSubjectType` enum (`Student = 0`, `Guardian = 1`) and the `ConsentStatus` enum (`Granted = 0`, `Revoked = 1`), exactly as declared in the design component block, including the `Tenant Tenant` navigation used by every other tenant entity.
2. Create `src/Ciclo.Core/Entities/PrivacyRequest.cs` with the `PrivacyRequest : ITenantScoped` class, the `PrivacyRequestType` enum (`Export = 0`, `Anonymization = 1`, `ConsentRevocation = 2`) and the `PrivacyRequestStatus` enum (`Submitted = 0`, `Approved = 1`, `Rejected = 2`, `Completed = 3`).
3. Add `public DateTime? AnonymizedAt { get; set; }` to `src/Ciclo.Core/Entities/Student.cs`, next to `UpdatedAt`, with a short comment stating that the value is written only by the anonymization service and that a null value means the row still holds the real identity.
4. Add to `src/Ciclo.Core/Auditing/AuditActions.cs`: `ConsentGranted = "privacy.consent.granted"`, `ConsentRevoked = "privacy.consent.revoked"`, `PrivacyExported = "privacy.exported"`, `PrivacyAnonymized = "privacy.anonymized"`, `PrivacyRequestDecided = "privacy.request.decided"`.
5. Add to `src/Ciclo.Core/Auditing/AuditTargetTypes.cs`: `Student = "Student"` and `PrivacyRequest = "PrivacyRequest"`.
6. Add to `AccessPolicy.Roles` in `src/Ciclo.Infrastructure/Security/AccessPolicy.cs`: `PrivacyRead = "Admin,Staff,Parent"`, `PrivacyManage = "Admin"`, `PrivacyConsentWrite = "Admin,Staff"`, each with a one-line comment naming this spec.
7. In `src/Ciclo.Infrastructure/Data/AppDbContext.cs`, add `public DbSet<ConsentRecord> ConsentRecords { get; set; } = null!;` and `public DbSet<PrivacyRequest> PrivacyRequests { get; set; } = null!;`, and add their `OnModelCreating` configuration: `Purpose` and `LegalBasis` required with max length 100 plus the unique index on `(TenantId, SubjectType, SubjectId, Purpose)`; `Reason` and `Resolution` with max length 500; `SubjectStudentId` restricted-delete foreign key to `Students.Id`; the index on `(TenantId, Status)`; and `LegalHold` with default value false.
8. Create `src/Ciclo.Infrastructure/Services/PrivacyException.cs` with `PrivacyException` and `PrivacyLegalHoldException` exactly as declared in the design component block.
9. Generate the migration with `dotnet ef migrations add AddPrivacyLgpd --project src/Ciclo.Infrastructure --startup-project src/Ciclo.Api` and confirm it only creates `ConsentRecords`, `PrivacyRequests`, their two indexes and the nullable `Students.AnonymizedAt` column.
**Expected Files:**
- `src/Ciclo.Core/Entities/ConsentRecord.cs`
- `src/Ciclo.Core/Entities/PrivacyRequest.cs`
- `src/Ciclo.Core/Entities/Student.cs`
- `src/Ciclo.Core/Auditing/AuditActions.cs`
- `src/Ciclo.Core/Auditing/AuditTargetTypes.cs`
- `src/Ciclo.Infrastructure/Security/AccessPolicy.cs`
- `src/Ciclo.Infrastructure/Data/AppDbContext.cs`
- `src/Ciclo.Infrastructure/Services/PrivacyException.cs`
- `src/Ciclo.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
**Validation:** `dotnet build` succeeds; `grep` confirms `AnonymizedAt` in `src/Ciclo.Core/Entities/Student.cs`, `PrivacyAnonymized` in `src/Ciclo.Core/Auditing/AuditActions.cs`, `PrivacyRequest` in `src/Ciclo.Core/Auditing/AuditTargetTypes.cs`, `ConsentRecord` in `src/Ciclo.Infrastructure/Data/AppDbContext.cs` and `PrivacyManage` in `src/Ciclo.Infrastructure/Security/AccessPolicy.cs`; the generated migration contains no drop or rename operation.
**Status:** [x] done

### Task 2: Privacy contracts and the consent service

**Objective:** Fix the wire shapes of the whole spec and implement the consent lifecycle with its audit entry.
**Requirements Covered:** FR-001, FR-004
**Design References:** Component: PrivacyDtos; Component: ConsentService; Data Structures.
**Actions:**
1. Create `src/Ciclo.Infrastructure/Contracts/PrivacyDtos.cs` with every record declared in the design component block, in that order, using `sealed record` and the exact member names and types.
2. Create `src/Ciclo.Infrastructure/Services/ConsentService.cs` with `IConsentService` and `ConsentService`, constructor receiving `AppDbContext db` and `IAuditTrail audit`.
3. Implement `GrantAsync` following the design steps: trim and reject an empty `purpose` or `legalBasis` with a `PrivacyException` whose code is `invalid_request` and whose status is 400; resolve the subject inside the tenant (`Students` for `Student`, `Users` for `Guardian`) and throw `KeyNotFoundException` when it is absent; find the existing row by `(TenantId, SubjectType, SubjectId, Purpose)` and update it in place when found (new `LegalBasis`, `Status = Granted`, `RevokedAt = null`, `Id` and `GrantedAt` preserved) or insert a new row otherwise; `SaveChangesAsync`; then `_audit.Record` with `AuditActions.ConsentGranted`, the target type matching the subject (`AuditTargetTypes.Student` or the literal `Guardian`), the subject id and the produced `ConsentDto` in `After`.
4. Implement `RevokeAsync` returning the current DTO without saving and without an audit call when the row is already `Revoked`, returning null when the row does not exist in the tenant, and otherwise stamping `Status = Revoked` and `RevokedAt = DateTime.UtcNow`, saving, and recording `AuditActions.ConsentRevoked`.
5. Implement `ListBySubjectAsync` filtered by tenant, subject type and subject id, ordered by `Purpose`, projected to `ConsentDto` with the enum members sent as their string names.
**Expected Files:**
- `src/Ciclo.Infrastructure/Contracts/PrivacyDtos.cs`
- `src/Ciclo.Infrastructure/Services/ConsentService.cs`
**Validation:** `dotnet build` succeeds; the file declares `GrantAsync`, `RevokeAsync` and `ListBySubjectAsync`; `grep` confirms `AuditActions.ConsentGranted` and `AuditActions.ConsentRevoked` are recorded and that no `AuditEvents` DbSet write is performed in the file.
**Status:** [x] done

### Task 3: Export service

**Objective:** Assemble the data subject document with the seven invariant sections, reading only from the tenant.
**Requirements Covered:** FR-002, FR-004, NFR-003
**Design References:** Component: PrivacyExportService; Component: PrivacyDtos; Data Flow — Export; Architecture Decision AD-004.
**Actions:**
1. Create `src/Ciclo.Infrastructure/Services/PrivacyExportService.cs` with `IPrivacyExportService` and `PrivacyExportService`, constructor receiving `AppDbContext db`.
2. Implement `ExportStudentAsync` following the design steps: load the student by `Id` and `TenantId` and throw `KeyNotFoundException` when absent; load the guardians through `StudentParents` joined to `Users` projecting only `Id`, `Name`, `Email`, `Role`, `IsActive` and `CreatedAt`; load the enrollments, the grades joined to `Assessment` for `Title`, the documents and the consents of the student subject; load the requests of the student.
3. Order every collection deterministically (`OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)` or the equivalent creation field per entity) and project each one to a `List<T>`, so an empty section is an empty list and never null.
4. Project documents to `DocumentExportDto` without `CaminhoArquivo` and without `HashSha256`; project the student to `StudentExportDto` including `AnonymizedAt`.
5. Return `new PrivacyExportDto(DateTime.UtcNow, student, guardians, enrollments, grades, documents, consents, requests)` in exactly that positional order.
**Expected Files:**
- `src/Ciclo.Infrastructure/Services/PrivacyExportService.cs`
**Validation:** `dotnet build` succeeds; the file declares `ExportStudentAsync`; `grep` confirms the file contains no `SaveChangesAsync`, no `HttpClient` and no `CaminhoArquivo` projection.
**Status:** [x] done

### Task 4: Anonymization service

**Objective:** Overwrite the identity of one student inside a transaction, honouring legal hold and idempotency, and record the marker-only audit entry.
**Requirements Covered:** FR-003, FR-004, NFR-001
**Design References:** Component: AnonymizationService; Architecture Decisions AD-001, AD-002, AD-005; Data Flow — Anonymization.
**Actions:**
1. Create `src/Ciclo.Infrastructure/Services/AnonymizationService.cs` with `IAnonymizationService` and `AnonymizationService`, constructor receiving `AppDbContext db` and `IAuditTrail audit`.
2. Open the transaction first with `await _db.Database.BeginTransactionAsync(ct)`, then load the student by `Id` and `TenantId`, throwing `KeyNotFoundException` when absent.
3. Return the current `StudentExportDto` without saving and without an audit call when `AnonymizedAt` is not null.
4. Query `PrivacyRequests` for `SubjectStudentId == studentId` with `LegalHold` true and a status other than `Rejected` and `Completed`, and throw `PrivacyLegalHoldException` before any assignment when a row exists.
5. Assign `Nome = "Titular anonimizado"`, `Cpf = null`, `Foto = null`, `Observacoes = null`, `DataNascimento = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)`, `AnonymizedAt = DateTime.UtcNow` and `UpdatedAt = DateTime.UtcNow`; keep `Id`, `TenantId`, `Status`, `AcademicYearId` and `SchoolClassId` untouched.
6. Call `SaveChangesAsync`, record `AuditActions.PrivacyAnonymized` with `AuditTargetTypes.Student`, `Before` null and an `After` object carrying only `identity = "anonymized"` and the `anonymizedAt` instant, then commit the transaction.
**Expected Files:**
- `src/Ciclo.Infrastructure/Services/AnonymizationService.cs`
**Validation:** `dotnet build` succeeds; the file declares `AnonymizeStudentAsync`; `grep` confirms `PrivacyLegalHoldException` is thrown before any field assignment, that the file contains no `Remove` call on the students set, and that the recorded entry carries no `Nome`, `Cpf`, `Foto` or `Observacoes` value.
**Status:** [x] done

### Task 5: Request service, controller and dependency registration

**Objective:** Own the request state machine, delegate execution to the other three services, expose the eight routes and register everything.
**Requirements Covered:** FR-005, FR-004, NFR-001, NFR-002
**Design References:** Component: PrivacyRequestService; Component: PrivacyController; Data Flow — Request submission, Decision, Completion; Architecture Decision AD-005.
**Actions:**
1. Create `src/Ciclo.Infrastructure/Services/PrivacyRequestService.cs` with `IPrivacyRequestService` and `PrivacyRequestService`, constructor receiving `AppDbContext db`, `IAuditTrail audit`, `IPrivacyExportService export`, `IAnonymizationService anonymization` and `IConsentService consents`.
2. Implement `SubmitAsync`, `ListAsync`, `DecideAsync` and `CompleteAsync` following the four design steps: parse the enum names, enforce the guardian linkage through `StudentParents` when `AccessPolicy.Allows(AccessPolicy.Roles.PrivacyManage, role)` is false, answer `invalid_state` 409 on a wrong source status, refuse `Anonymization` with `LegalHold` through `PrivacyLegalHoldException`, delegate the execution to the owning service, then stamp `Status = Completed` and `CompletedAt` and record one `AuditActions.PrivacyRequestDecided` entry.
3. Create `src/Ciclo.Api/Controllers/PrivacyController.cs` with the class attribute set declared in the design, the eight actions with their exact routes and role attributes, the tenant and caller resolution copied from the `StudentController` helpers, and the `try`/`catch (PrivacyException ex) { return ProblemResponse.FromCode(ex.Message, ex.StatusCode); }` wrapper on each action.
4. Serialize the export document in the `Export` action with `JsonSerializer.SerializeToUtf8Bytes`, wrap it in a `MemoryStream` and return `File(stream, "application/json", $"privacy-export-{studentId}.json")`.
5. Register the four services in `src/Ciclo.Api/Program.cs` next to the existing `AddScoped` registrations: `IConsentService` to `ConsentService`, `IAnonymizationService` to `AnonymizationService`, `IPrivacyExportService` to `PrivacyExportService`, `IPrivacyRequestService` to `PrivacyRequestService`.
**Expected Files:**
- `src/Ciclo.Infrastructure/Services/PrivacyRequestService.cs`
- `src/Ciclo.Api/Controllers/PrivacyController.cs`
- `src/Ciclo.Api/Program.cs`
**Validation:** `dotnet build` succeeds; the controller declares the routes `api/privacy/requests`, `requests/{id:guid}/decision`, `requests/{id:guid}/complete`, `students/{studentId:guid}/export`, `consents` and `consents/{id:guid}/revoke`; `grep` confirms `IConsentService` in `src/Ciclo.Api/Program.cs`; the existing `ApiRouteConventionTests` and `ApiAuthorizationTests` still pass.
**Status:** [x] done

### Task 6: Tests for the consent lifecycle and the export document

**Objective:** Prove the consent single-row rule and the export completeness contract with executable tests.
**Requirements Covered:** FR-001, FR-002, NFR-003
**Design References:** Testing Strategy — PrivacyConsentTests and PrivacyExportTests.
**Actions:**
1. Create `tests/Ciclo.Api.Tests/PrivacyConsentTests.cs` following the arrangement of the existing test classes in the project: a second grant on the same subject and purpose leaves the row count at one and replaces `LegalBasis`; revoking stamps `Status` and `RevokedAt` and preserves `GrantedAt` and `LegalBasis`; revoking an already revoked consent writes no second audit entry; an empty `purpose` throws `PrivacyException` with code `invalid_request`; an unknown subject throws `KeyNotFoundException`.
2. Create `tests/Ciclo.Api.Tests/PrivacyExportTests.cs`: a fully populated student yields non-empty `guardians`, `enrollments`, `grades`, `documents` and `consents`; a student with no related rows yields the seven sections with empty collections; the serialized JSON contains no `PasswordHash` and no `CaminhoArquivo`; an anonymized student exports the sentinel values and a non-null `AnonymizedAt`; two consecutive exports of unchanged data serialize identically apart from `generatedAt`.
3. Assert the section order of the serialized document against the literal `generatedAt`, `student`, `guardians`, `enrollments`, `grades`, `documents`, `consents`, `requests`.
**Expected Files:**
- `tests/Ciclo.Api.Tests/PrivacyConsentTests.cs`
- `tests/Ciclo.Api.Tests/PrivacyExportTests.cs`
**Validation:** `dotnet test tests/Ciclo.Api.Tests/Ciclo.Api.Tests.csproj --filter FullyQualifiedName~PrivacyConsent` passes; `dotnet test tests/Ciclo.Api.Tests/Ciclo.Api.Tests.csproj --filter FullyQualifiedName~PrivacyExport` passes.
**Status:** [x] done

### Task 7: Tests for anonymization irreversibility and the request API

**Objective:** Prove that anonymization is irreversible and respects legal hold, and that the request lifecycle, the authorization and the audit trail behave as specified.
**Requirements Covered:** FR-003, FR-004, FR-005, NFR-001, NFR-002
**Design References:** Testing Strategy — PrivacyAnonymizationTests and PrivacyRequestApiTests; Architecture Decisions AD-001, AD-002, AD-005.
**Actions:**
1. Create `tests/Ciclo.Api.Tests/PrivacyAnonymizationTests.cs`: after the call no original `Nome`, `Cpf`, `Foto`, `Observacoes` or `DataNascimento` value survives and the sentinel values are present; `StudentParents`, `Enrollment`, `Grade` and `Document` rows are unchanged and still reference the same `Student.Id`; an open legal hold throws `PrivacyLegalHoldException` and leaves the row identical; a second call leaves `AnonymizedAt` unchanged and records no second audit entry; the recorded audit entry contains none of the original identity values.
2. Create `tests/Ciclo.Api.Tests/PrivacyRequestApiTests.cs` exercising the routes through the existing `ApiTestHost`: `Submitted` to `Approved` and to `Rejected` stamp `DecidedAt`; a second decision answers 409; completing a non-approved request answers 409; a guardian submitting for an unlinked student answers 403; a guardian posting to the decision, completion and export routes answers 403; a row of another tenant answers 404 and leaks no field; completing each of the three types reaches `Completed` and records exactly one `PrivacyRequestDecided` entry.
3. Use the existing `NoopAuditTrail` or a capturing double from `TestDoubles` for the audit assertions, following the arrangement the existing audit tests already use.
**Expected Files:**
- `tests/Ciclo.Api.Tests/PrivacyAnonymizationTests.cs`
- `tests/Ciclo.Api.Tests/PrivacyRequestApiTests.cs`
**Validation:** `dotnet test tests/Ciclo.Api.Tests/Ciclo.Api.Tests.csproj --filter FullyQualifiedName~PrivacyAnonymization` passes; `dotnet test tests/Ciclo.Api.Tests/Ciclo.Api.Tests.csproj --filter FullyQualifiedName~PrivacyRequestApi` passes.
**Status:** [x] done

### Task 8: Admin request screen and guardian privacy center

**Objective:** Give the admin a request list with decisions and the guardian a consent view with request submission and export download.
**Requirements Covered:** FR-006
**Design References:** Component: Privacy admin and guardian pages; File / Module Layout — frontend.
**Actions:**
1. Create `frontend/src/pages/admin/PrivacyRequests.tsx` rendering the request list fetched from `GET /api/privacy/requests` with type, status, subject, `LegalHold` and `RetainUntil`, using the existing `Card`, `PageHeader`, `Table`, `Badge`, `Button` and `EmptyState` components, and wiring the approve, reject and complete actions to the decision and completion routes followed by a list reload.
2. Create `frontend/src/pages/parent/PrivacyCenter.tsx` rendering, per linked child, the consents fetched from `GET /api/privacy/consents` with their status, a submit action for an export or anonymization request that is disabled when an open request of the same type already exists, and an export download built from a `Blob` of the export route.
3. Register both pages in `frontend/src/App.tsx` with the routes `/admin/privacy` and `/parent/privacy`, inside the existing admin and parent route groups, following the pattern of the sibling pages.
4. On a non-2xx answer, read the problem `detail` or `code` from the body, surface it through the existing notification mechanism and keep the previous list rendered.
**Expected Files:**
- `frontend/src/pages/admin/PrivacyRequests.tsx`
- `frontend/src/pages/parent/PrivacyCenter.tsx`
- `frontend/src/App.tsx`
**Validation:** `npm --prefix frontend run build` succeeds; `grep` confirms `PrivacyCenter` and `PrivacyRequests` are referenced in `frontend/src/App.tsx`.
**Status:** [x] done
