---
name: privacidade-lgpd
status: verified
determinism: D
references: [V21]
---

# Spec 210: Privacidade e LGPD — Requirements

## Determinism Classification

**Class:** D — Deterministic

Every behavior this spec adds is decided by the persisted data and by the request payload. Given the same database state and the same command, the export document has the same sections and the same field set, the anonymization writes the same sentinel values, and the consent and request state machines move through the same transitions. There is no model output, no probabilistic ranking and no generated prose anywhere in the flow, so the acceptance criteria are checked with direct assertions over entity state and over the serialized export document. The spec does not modify the SDD pipeline, so it is not Class I.

## Value Delivery

**V21: Privacidade e LGPD** — Direitos do titular e ciclo de vida de dados pessoais: registro de consentimento, exportação dos dados do aluno e do responsável, eliminação ou anonimização com retenção definida, e trilha de quem acessou dados sensíveis. Necessário para tratar dados de menores em conformidade com a LGPD.

## Functional Requirements

### FR-001: Registro de consentimento por responsável e aluno

**What:** Adds the entity `ConsentRecord` in `src/Ciclo.Core/Entities/ConsentRecord.cs` — tenant-scoped through `ITenantScoped` — carrying `Id`, `TenantId`, `SubjectType` (`ConsentSubjectType`: `Student = 0`, `Guardian = 1`), `SubjectId` (`Student.Id` for `Student`, `User.Id` for `Guardian`), `Purpose`, `LegalBasis`, `Status` (`ConsentStatus`: `Granted = 0`, `Revoked = 1`), `GrantedAt`, `RevokedAt`, `GrantedByUserId`, `CreatedAt`. Adds `ConsentService` in `src/Ciclo.Infrastructure/Services/ConsentService.cs` so a consent is recorded once per `(TenantId, SubjectType, SubjectId, Purpose)` and its state changes only through the service.
**Why:** V21 names the consent record per guardian and per student as the first right-of-the-data-subject capability; without a persisted consent row there is no way to prove the school holds a lawful basis for the minor's data.
**Acceptance Criteria:**
- [ ] The `ConsentRecord` entity declares `ITenantScoped` and exposes `SubjectType`, `Purpose`, `LegalBasis`, `Status`, `GrantedAt` and `RevokedAt`. [verify: grep-present `ITenantScoped` `src/Ciclo.Core/Entities/ConsentRecord.cs`]
- [ ] `ConsentService` exposes `GrantAsync`, `RevokeAsync` and `ListBySubjectAsync`. [verify: function-exists `RevokeAsync` `src/Ciclo.Infrastructure/Services/ConsentService.cs`]
- [ ] Granting the same `(SubjectType, SubjectId, Purpose)` twice updates the single existing row with the new `LegalBasis` and `Status` back to `Granted`, clears `RevokedAt`, and never inserts a second row. [verify: project-validation]
- [ ] Revoking sets `Status` to `Revoked` and stamps `RevokedAt`, while `GrantedAt` and `LegalBasis` keep their original values. [verify: project-validation]
- [ ] Revoking an already revoked consent changes no field and writes no second audit entry. [verify: project-validation]
**Failure modes:**
- Unknown consent id on revoke: the service returns null and the caller answers 404; no row is mutated.
- `SubjectId` that does not resolve to a `Student` when `SubjectType` is `Student`, or to a `User` of the same tenant when `Guardian`: the service throws `KeyNotFoundException` before any write.
- Empty or whitespace `Purpose` or `LegalBasis`: the service throws `ArgumentException` and nothing is persisted.
- A consent row of another tenant is invisible: the tenant filter applies to every read in `ConsentService`.

### FR-002: Exportação dos dados do titular em arquivo estruturado

**What:** Adds `PrivacyExportService` in `src/Ciclo.Infrastructure/Services/PrivacyExportService.cs` with `ExportStudentAsync(Guid tenantId, Guid studentId, CancellationToken ct)` returning `PrivacyExportDto`, plus the DTOs in `src/Ciclo.Infrastructure/Contracts/PrivacyDtos.cs`. The exported document always carries the seven top-level sections `student`, `guardians`, `enrollments`, `grades`, `documents`, `consents` and `requests`, each populated from the entities already persisted for that student.
**Why:** V21 names the export of the data subject data in a structured file; the guardian of a minor must be able to receive everything the school stores about that minor in one machine-readable artifact.
**Acceptance Criteria:**
- [ ] `PrivacyExportService` exposes `ExportStudentAsync`. [verify: function-exists `ExportStudentAsync` `src/Ciclo.Infrastructure/Services/PrivacyExportService.cs`]
- [ ] The serialized export always contains the seven sections `student`, `guardians`, `enrollments`, `grades`, `documents`, `consents` and `requests`. [verify: project-validation]
- [ ] A student with no enrollments, grades, documents, consents or requests produces the seven sections with empty arrays, never null and never a missing key. [verify: project-validation]
- [ ] `guardians` is projected without `PasswordHash`, `SecurityStamp`, `ConcurrencyStamp` or refresh-token data. [verify: project-validation]
- [ ] `documents` carries only document metadata, never file bytes or storage keys. [verify: project-validation]
- [ ] An unknown or other-tenant `studentId` produces no document and the caller answers 404. [verify: project-validation]
**Failure modes:**
- Student of another tenant: the tenant filter makes the lookup empty and no data from that tenant appears in the response.
- An anonymized student: the export returns the anonymized field values currently persisted, never a cached original.
- Serialization failure: the exception propagates and no partial or truncated file reaches the caller.

### FR-003: Eliminação por anonimização com retenção e bloqueio legal

**What:** Adds `Student.AnonymizedAt` (`DateTime?`) to `src/Ciclo.Core/Entities/Student.cs` and `AnonymizationService` in `src/Ciclo.Infrastructure/Services/AnonymizationService.cs` with `AnonymizeStudentAsync(Guid tenantId, Guid studentId, CancellationToken ct)`. The operation overwrites the identifying fields of the existing `Student` row — `Nome` to the literal `Titular anonimizado`, `Cpf` to null, `Foto` to null, `Observacoes` to null, `DataNascimento` to `1900-01-01` — keeps `Id`, `TenantId` and the academic links, and stamps `AnonymizedAt` with the UTC instant of the operation.
**Why:** V21 names elimination or anonymization with a retention window and a legal block; anonymization keeps referential integrity of enrollments, grades and documents while removing the personal identity of the minor.
**Acceptance Criteria:**
- [ ] `Student` declares `AnonymizedAt`. [verify: grep-present `AnonymizedAt` `src/Ciclo.Core/Entities/Student.cs`]
- [ ] `AnonymizationService` exposes `AnonymizeStudentAsync`. [verify: function-exists `AnonymizeStudentAsync` `src/Ciclo.Infrastructure/Services/AnonymizationService.cs`]
- [ ] After the operation the persisted row holds none of the original `Nome`, `Cpf`, `Foto`, `Observacoes` or `DataNascimento` values and holds a non-null `AnonymizedAt`. [verify: project-validation]
- [ ] The `Student` row keeps its `Id`, `TenantId`, `Status`, `AcademicYearId` and `SchoolClassId`, and the `StudentParents`, `Enrollment`, `Grade` and `Document` rows are untouched. [verify: project-validation]
- [ ] An open `PrivacyRequest` with `LegalHold` true for the same student blocks the operation: the service throws `PrivacyLegalHoldException`, the transaction rolls back and every `Student` field keeps its pre-call value. [verify: project-validation]
- [ ] Repeating the operation on an already anonymized student leaves `AnonymizedAt` and every field unchanged and writes no second audit entry. [verify: project-validation]
- [ ] The retention deadline held in `RetainUntil` is still persisted on the `PrivacyRequest` row after completion, so the retention window is recorded with the operation. [verify: project-validation]
**Failure modes:**
- Legal hold active: refusal is atomic, nothing is written, and the API answers 409 with the legal hold reason.
- Already anonymized: the call is a no-op that returns the current student and writes no audit row.
- Unknown or other-tenant student id: `KeyNotFoundException`, no write, 404 at the API.
- Persistence failure mid-operation: the surrounding transaction rolls back and the original identity values remain persisted.

### FR-004: Trilha de acesso a dados sensíveis reutilizando a spec 110

**What:** Extends `src/Ciclo.Core/Auditing/AuditActions.cs` with `ConsentGranted` valued `privacy.consent.granted`, `ConsentRevoked` valued `privacy.consent.revoked`, `PrivacyExported` valued `privacy.exported`, `PrivacyAnonymized` valued `privacy.anonymized` and `PrivacyRequestDecided` valued `privacy.request.decided`; extends `src/Ciclo.Core/Auditing/AuditTargetTypes.cs` with `Student` and `PrivacyRequest`. Every privacy operation records exactly one `AuditEntry` through the existing `IAuditTrail` — no new audit table and no parallel trail.
**Why:** V21 names the trail of who accessed sensitive data, to be reused from spec 110; reusing the existing audit owner keeps one authoritative trail instead of a second divergent one.
**Acceptance Criteria:**
- [ ] `AuditActions` declares `PrivacyExported`, `PrivacyAnonymized`, `ConsentGranted`, `ConsentRevoked` and `PrivacyRequestDecided`. [verify: grep-present `PrivacyAnonymized` `src/Ciclo.Core/Auditing/AuditActions.cs`]
- [ ] `AuditTargetTypes` declares `Student` and `PrivacyRequest`. [verify: grep-present `PrivacyRequest` `src/Ciclo.Core/Auditing/AuditTargetTypes.cs`]
- [ ] A successful export writes one audit entry with action `PrivacyExported`, target type `Student` and the acting user id. [verify: project-validation]
- [ ] A successful anonymization writes one audit entry with action `PrivacyAnonymized` and target type `Student`, and a refused anonymization writes none. [verify: project-validation]
- [ ] A consent grant writes `ConsentGranted` and a consent revocation writes `ConsentRevoked`, each targeting `Student` or `Guardian` by `SubjectType`. [verify: project-validation]
- [ ] The privacy services obtain the trail through `IAuditTrail`; none of them writes an `AuditEvent` row by a direct `DbSet` call. [verify: project-validation]
**Failure modes:**
- Audit write failure: the privacy operation fails with it, so no sensitive read or identity overwrite happens without its trail.
- Acting user not resolvable: the trail records the persisted `AuditActor` value rather than a fabricated identity.

### FR-005: Fluxo de solicitação do titular e superfície de API

**What:** Adds the tenant-scoped entity `PrivacyRequest` in `src/Ciclo.Core/Entities/PrivacyRequest.cs` with `Id`, `TenantId`, `Type` (`PrivacyRequestType`: `Export = 0`, `Anonymization = 1`, `ConsentRevocation = 2`), `Status` (`PrivacyRequestStatus`: `Submitted = 0`, `Approved = 1`, `Rejected = 2`, `Completed = 3`), `SubjectStudentId`, `RequestedByUserId`, `Reason`, `Resolution`, `LegalHold`, `RetainUntil`, `SubmittedAt`, `DecidedAt`, `CompletedAt`. Adds `PrivacyRequestService` in `src/Ciclo.Infrastructure/Services/PrivacyRequestService.cs` and `PrivacyController` in `src/Ciclo.Api/Controllers/PrivacyController.cs` exposing `/api/privacy/requests` (POST and GET), `/api/privacy/requests/{id}/decision` (POST), `/api/privacy/requests/{id}/complete` (POST), `/api/privacy/students/{studentId}/export` (GET), `/api/privacy/consents` (POST and GET) and `/api/privacy/consents/{id}/revoke` (POST).
**Why:** V21 names the API and the guardian request flow; the guardian of a minor opens a request and the school decides and executes it under the same audited trail.
**Acceptance Criteria:**
- [ ] `PrivacyRequestService` exposes `SubmitAsync`, `DecideAsync` and `CompleteAsync`. [verify: function-exists `SubmitAsync` `src/Ciclo.Infrastructure/Services/PrivacyRequestService.cs`]
- [ ] `PrivacyController` declares the `requests`, `decision`, `complete`, `export`, `consents` and `revoke` routes. [verify: grep-present `consents/{id:guid}/revoke` `src/Ciclo.Api/Controllers/PrivacyController.cs`]
- [ ] A guardian can submit a request only for a student linked through `StudentParent`; any other student id answers 403. [verify: project-validation]
- [ ] `decision` moves `Submitted` to `Approved` or `Rejected` and stamps `DecidedAt`, while any other source status answers 409 and leaves the row unchanged. [verify: project-validation]
- [ ] `complete` on an `Approved` request runs the operation for its type, stamps `CompletedAt`, sets `Status` to `Completed` and writes one `PrivacyRequestDecided` audit entry. [verify: project-validation]
- [ ] `complete` on a request that is not `Approved` answers 409 and changes nothing. [verify: project-validation]
- [ ] Admin-only routes answer 403 for a guardian token and 200 for an admin token on the same payload. [verify: project-validation]
- [ ] Every row returned by the request list route belongs to the caller tenant. [verify: project-validation]
**Failure modes:**
- Unauthenticated caller: 401 on every route.
- `Type` outside the enum or missing `SubjectStudentId`: 400 with a validation problem response.
- Completion of an `Anonymization` request whose student carries an active legal hold: 409 and the request stays `Approved`.
- Unknown request id: 404.
- Row of another school: the row is invisible and the route answers 404 rather than 403, so existence is not leaked.

### FR-006: Tela administrativa e central de privacidade do responsável

**What:** Adds `frontend/src/pages/admin/PrivacyRequests.tsx` — the admin list of tenant requests with type, status, subject, `LegalHold` and retention deadline, plus decision and completion actions that call the FR-005 routes. Adds `frontend/src/pages/parent/PrivacyCenter.tsx` — the guardian view listing the consents of each linked child, submitting an export or anonymization request, and downloading the export document returned by the export route.
**Why:** V21 names the administrative screen and the guardian request flow; the capabilities of FR-001 to FR-005 become usable for the school only when the admin can decide a request and the guardian can open one.
**Acceptance Criteria:**
- [ ] `frontend/src/pages/admin/PrivacyRequests.tsx` exists and renders the request list with type, status and subject. [verify: file-exists `frontend/src/pages/admin/PrivacyRequests.tsx`]
- [ ] `frontend/src/pages/parent/PrivacyCenter.tsx` exists and renders the consent list per linked child. [verify: file-exists `frontend/src/pages/parent/PrivacyCenter.tsx`]
- [ ] Both pages are reachable through the existing router in `frontend/src/App.tsx`. [verify: grep-present `PrivacyCenter` `frontend/src/App.tsx`]
- [ ] The admin page calls the `decision` and `complete` routes and reloads the list from the server after each call. [verify: project-validation]
- [ ] The guardian page disables the submit action when the selected child already has an open request of the same type. [verify: project-validation]
**Failure modes:**
- Route rejected with 403 or 409: the page surfaces the problem detail message returned by the API and keeps the previous list on screen.
- Export download fails: the page reports the failure and creates no empty file.
- No linked child: the guardian page renders an empty state instead of an empty table.

## Non-Functional Requirements

### NFR-001: Atomicidade e isolamento de falhas das operações de privacidade

**What:** Every state-changing privacy operation runs inside a single `DbContext` transaction — consent grant and revoke, request decision, request completion and student anonymization — and either commits fully or leaves the database in its pre-call state. The export is read-only and produces no partial artifact.
**Acceptance Criteria:**
- [ ] A refused anonymization leaves every `Student` field and every audit row exactly as before the call. [verify: project-validation]
- [ ] A `decision` that fails validation writes no `PrivacyRequest` change and no audit row. [verify: project-validation]
- [ ] `PrivacyExportService` opens no transaction and mutates no entity. [verify: grep-absent `SaveChangesAsync` `src/Ciclo.Infrastructure/Services/PrivacyExportService.cs`]
- [ ] Full project validation passes. [verify: project-validation]

### NFR-002: Autorização e isolamento por tenant

**What:** Every privacy route is authenticated through the existing JWT authorization and every read is filtered by the caller tenant. The admin-only operations are export, anonymization, request decision and request completion; a guardian may only read and submit for students linked to them.
**Acceptance Criteria:**
- [ ] `PrivacyController` carries class-level authentication so no route is anonymous. [verify: grep-present `Authorize` `src/Ciclo.Api/Controllers/PrivacyController.cs`]
- [ ] A guardian token cannot reach the export, decision or completion routes: each answers 403. [verify: project-validation]
- [ ] A request id or student id of another tenant answers 404, and no field of that tenant appears in any response body. [verify: project-validation]
- [ ] Every privacy query is constrained by `TenantId` before it is materialized. [verify: project-validation]

### NFR-003: Formato determinístico do documento de exportação

**What:** For a fixed dataset the export document has a fixed section order, a fixed field set inside each section and an empty array for an empty section. The only field that varies between two runs over the same data is `generatedAt`.
**Acceptance Criteria:**
- [ ] Exporting the same student twice with unchanged data produces identical documents apart from `generatedAt`. [verify: project-validation]
- [ ] Every section is serialized as an array, and an empty section serializes as an empty array, never as null. [verify: project-validation]
- [ ] The section order in the produced JSON is `student`, `guardians`, `enrollments`, `grades`, `documents`, `consents`, `requests`. [verify: project-validation]

## Constraints

- Consent and request entities are tenant-scoped through the existing `ITenantScoped` contract, like every other tenant entity. [verify: grep-present `ITenantScoped` `src/Ciclo.Core/Entities/PrivacyRequest.cs`]
- The sensitive data trail is written through the existing `IAuditTrail` abstraction owned by spec 110; no second audit store is introduced. [verify: file-exists `src/Ciclo.Core/Interfaces/IAuditTrail.cs`]
- `PrivacyExportService` builds the document in memory; it writes no file to the local filesystem and adds no storage dependency. [verify: grep-present `MemoryStream` `src/Ciclo.Infrastructure/Services/PrivacyExportService.cs`]
- Anonymization overwrites fields on the existing `Student` row and keeps the row, so enrollments, grades, documents and guardian links keep their foreign keys. [verify: grep-present `AnonymizedAt` `src/Ciclo.Core/Entities/Student.cs`]
- New API routes live in `src/Ciclo.Api/Controllers/PrivacyController.cs` and follow the existing controller conventions. [verify: file-exists `src/Ciclo.Api/Controllers/PrivacyController.cs`]
- The two new entities are registered in `src/Ciclo.Infrastructure/Data/AppDbContext.cs` and delivered with an EF migration under `src/Ciclo.Infrastructure/Migrations/`. [verify: grep-present `ConsentRecord` `src/Ciclo.Core/Entities/ConsentRecord.cs`]
- Full project validation passes. [verify: project-validation]

## Edge Cases & Error States

- Anonymizing a student that already has an anonymization request in `Completed` state: a new request may be submitted, and completing it is a no-op that returns the already anonymized student and writes no audit row.
- Export requested for an anonymized student: the document reports the anonymized values and the `AnonymizedAt` timestamp, never the originals.
- Guardian submits a request for a student they are later unlinked from: the persisted request stays readable by the admin, and the guardian can no longer read it.
- Two decisions posted for the same request concurrently: the second one sees a status other than `Submitted` and answers 409.
- A consent revoked while an export is being assembled: the export reflects the state read inside its own read scope, never a mix of the two.
- Student with no guardian rows: `guardians` is an empty array and the rest of the sections are still present.
- `RetainUntil` already in the past at completion time: the operation still completes and the past deadline is surfaced in the export `requests` section, so the retention window stays visible rather than silently corrected.
- Legal hold flipped to true on an `Approved` anonymization request after approval: completion is refused with 409 and the request stays `Approved`.

## Anti-Scope

- Nenhum agendador de retenção em background é criado; a conclusão da solicitação registra o prazo em `RetainUntil` e nenhum job o executa. [verify: file-absent `src/Ciclo.Infrastructure/Privacy/RetentionScheduler.cs`]
- Nenhuma exclusão física de `Student` é introduzida; a anonimização sobrescreve campos da linha existente. [verify: grep-absent `Remove` `src/Ciclo.Infrastructure/Services/AnonymizationService.cs`]
- Nenhuma tabela de auditoria paralela é criada; a trilha reutiliza `AuditEvent`. [verify: file-absent `src/Ciclo.Core/Entities/PrivacyAuditEvent.cs`]
- Nenhuma chamada de rede é adicionada ao caminho de exportação. [verify: grep-absent `HttpClient` `src/Ciclo.Infrastructure/Services/PrivacyExportService.cs`]

## Dependencies

- Spec 110 (auditoria-de-operacoes) — supplies `IAuditTrail`, `AuditEntry`, `AuditEvent`, `AuditActor` and the query surface; this spec extends `AuditActions` and `AuditTargetTypes` and does not replace them.
- Spec 150 (estrutura-academica-e-desempenho) — supplies `Enrollment`, `Grade`, `AcademicYear` and `SchoolClass`, which the export projects and the anonymization must keep linked.
- Spec 170 (armazenamento-de-documentos) — supplies the `Document` metadata, which the export projects without file bytes.
- Spec 100 (Frontend Application) — supplies the React shell, the router in `frontend/src/App.tsx`, the admin and parent page conventions and the shared components used by FR-006.
- Spec 120 (ciclo-de-vida-de-identidade) — supplies `User`, `UserRole` and the guardian identity used by `ConsentRecord` and by the request author.

## Out of Scope

- Background execution of retention deadlines and automatic purge after the window.
- Data protection officer workflow, incident notification and the data mapping register.
- Cookie, analytics and marketing consent for the web application itself.
- International data transfer controls and cross-border clauses.
- Changes to encryption at rest or to the storage layer under spec 170.
- Redaction of audit rows already written by spec 110.

## Test Scope

- `tests/Ciclo.Api.Tests/PrivacyExportTests.cs` — proves export completeness, the seven invariant sections, empty-section arrays, guardian field redaction and the anonymized-student projection.
- `tests/Ciclo.Api.Tests/PrivacyAnonymizationTests.cs` — proves anonymization irreversibility, referential integrity preservation, legal hold refusal and idempotency.
- `tests/Ciclo.Api.Tests/PrivacyConsentTests.cs` — proves the single-row-per-purpose rule, revoke field stamping and the no-op revoke.
- `tests/Ciclo.Api.Tests/PrivacyRequestApiTests.cs` — proves the request state machine, the guardian linkage authorization, tenant isolation and the once-per-operation audit entries.
