<!-- PIPELINE-CONTRACT: Task completion is determined by "**Status:** [x] done" at the end of each "### Task N" section.
     Checkboxes "- [ ]" under "**Validation:**" are human verification guides — the pipeline ignores them completely.
     To mark a task as done, add "**Status:** [x] done". To mark it as pending, use "**Status:** [ ] pending". -->
# Spec 160: Rematrícula em Lote — Implementation Tasks

## Planned Scaffolds

- `src/Ciclo.Infrastructure/Services/EnrollmentRenewalService.cs`
- `tests/Ciclo.Api.Tests/EnrollmentRenewalTests.cs`
- `frontend/src/domain/renewalBatch.ts`
- `frontend/src/pages/admin/EnrollmentRenewal.tsx`
- `frontend/tests/renewalBatch.test.ts`

## Task Order

Tasks MUST be executed sequentially in numerical order. Each task depends on the completion of all preceding tasks.

## Tasks

### Task 1: Serviço de renovação e contratos de lote

**Objective:** Entregar os contratos de prévia e lote e o serviço que decide elegibilidade, idempotência e desfecho por aluno, delegando a escrita aos métodos existentes de `IEnrollmentService`.
**Requirements Covered:** FR-001, FR-002, FR-003, FR-004, FR-005, NFR-001
**Design References:** Design Approach; Component: EnrollmentRenewalService (contrato); Component: EnrollmentRenewalService (implementação); Component: Contratos de renovação; Data Structures.
**Actions:**
1. Em `src/Ciclo.Infrastructure/Contracts/EnrollmentDtos.cs`, adicionar, ao lado dos contratos existentes e sem alterar nenhum deles, os registros `RenewalPreviewDto`, `RenewalPreviewItemDto`, `RenewalBatchRequest`, `RenewalBatchItemResultDto`, `RenewalBatchResultDto` e o enum `RenewalOutcome`, com os campos e a ordem declarados na seção Data Structures do design.
2. Criar `src/Ciclo.Infrastructure/Services/EnrollmentRenewalService.cs` no espaço `Ciclo.Infrastructure.Services`, declarando `IEnrollmentRenewalService` com as assinaturas exatas do design e `EnrollmentRenewalService(AppDbContext db, IEnrollmentService enrollments)`.
3. Implementar `GetPreviewAsync` conforme os sete passos do design: `AccessPolicy.Require(AccessPolicy.Roles.EnrollmentPeriodRead, role)`, período por `periodId`, conjunto de alunos já matriculados no período em uma consulta, candidatos com `AnoLetivo` diferente do período ordenados por `Nome`, avaliação por `DocumentCompliance.EvaluateAsync`, projeção em `CarriedDocumentDto`, precedência fixa de elegibilidade e retorno de `RenewalPreviewDto`.
4. Implementar `RenewAsync` conforme os oito passos do design: `AccessPolicy.Require(AccessPolicy.Roles.EnrollmentWrite, role)`, validação de `StudentIds` vazio com `EnrollmentException("student_ids_required", 400)`, período por `EnrollmentPeriodId`, deduplicação preservando a ordem, laço por aluno com os cinco desfechos e a sequência `CreateEnrollmentAsync`, `SubmitAsync`, `ApproveAsync`, captura de `EnrollmentException` e de `Exception` convertendo cada falha em item `Failed` sem interromper o lote, e contadores calculados a partir dos itens.
5. Não alterar `src/Ciclo.Infrastructure/Services/EnrollmentService.cs`, `src/Ciclo.Infrastructure/Services/DocumentCompliance.cs` nem `src/Ciclo.Infrastructure/Security/AccessPolicy.cs`.
**Expected Files:**
- `src/Ciclo.Infrastructure/Contracts/EnrollmentDtos.cs`
- `src/Ciclo.Infrastructure/Services/EnrollmentRenewalService.cs`
**Validation:** `dotnet build src/Ciclo.Infrastructure/Ciclo.Infrastructure.csproj` conclui sem erro; `EnrollmentRenewalService.cs` contém `AccessPolicy.Require`, `DocumentCompliance.EvaluateAsync` e as três chamadas a `IEnrollmentService`; nenhum `catch` vazio existe no arquivo novo.
**Status:** [x] done

### Task 2: Endpoints de prévia e lote e registro de dependência

**Objective:** Publicar `GET /api/enrollments/renewal-preview` e `POST /api/enrollments/renewal-batch` com autorização por `AccessPolicy` e registrar o serviço no contêiner.
**Requirements Covered:** FR-001, FR-002, NFR-001
**Design References:** Component: Endpoints de renovação; Component: Registro de dependência.
**Actions:**
1. Em `src/Ciclo.Api/Controllers/EnrollmentController.cs`, adicionar ao `EnrollmentController` a ação `GetRenewalPreview` com `[HttpGet("renewal-preview")]` e `[Authorize(Roles = AccessPolicy.Roles.EnrollmentPeriodRead)]`, recebendo `[FromQuery] Guid periodId` e `CancellationToken`, chamando `GetPreviewAsync` com `GetTenantId()` e `GetRole()` e devolvendo `Ok(result)`.
2. No mesmo arquivo e controller, adicionar a ação `RenewBatch` com `[HttpPost("renewal-batch")]` e `[Authorize(Roles = AccessPolicy.Roles.EnrollmentWrite)]`, recebendo `[FromBody] RenewalBatchRequest request` e `CancellationToken`, obtendo `(userId, role)` por `GetUserInfo()` e `tenantId` por `GetTenantId()`, chamando `RenewAsync` e devolvendo `Ok(result)`.
3. Nas duas ações, capturar `EnrollmentException` e devolver `ProblemResponse.FromCode(ex.Message, ex.StatusCode)`, exatamente como as ações existentes do mesmo arquivo; adicionar o campo `IEnrollmentRenewalService` ao construtor do controller.
4. Em `src/Ciclo.Api/Program.cs`, adicionar `builder.Services.AddScoped<IEnrollmentRenewalService, EnrollmentRenewalService>();` ao lado do registro de `IEnrollmentService`.
5. Confirmar que nenhum endpoint passa a permitir acesso anônimo: as duas ações declaram `[Authorize]` com lista de papéis de `AccessPolicy`.
**Expected Files:**
- `src/Ciclo.Api/Controllers/EnrollmentController.cs`
- `src/Ciclo.Api/Program.cs`
- `tests/Ciclo.Api.Tests/ApiRouteConventionTests.cs`
- `tests/Ciclo.Api.Tests/ApiAuthorizationTests.cs`
- `tests/Ciclo.Api.Tests/ApiRateLimitingTests.cs`
**Validation:** `dotnet build src/Ciclo.Api/Ciclo.Api.csproj` conclui sem erro; `dotnet test tests/Ciclo.Api.Tests --filter "FullyQualifiedName~ApiRouteConventionTests|FullyQualifiedName~ApiAuthorizationTests|FullyQualifiedName~ApiRateLimitingTests"` passa sem exigir edição dos três contratos de inventário de endpoints — se algum deles falhar, o ajuste é na declaração do endpoint novo, nunca na expectativa do teste.
**Status:** [x] done

### Task 3: Testes do backend de rematrícula em lote

**Objective:** Provar prévia, idempotência, falha parcial, promoção de ano letivo e auditoria por aluno com um contexto in-memory próprio.
**Requirements Covered:** FR-001, FR-002, FR-003, FR-004, FR-005, NFR-001, NFR-002
**Design References:** Testing Strategy.
**Actions:**
1. Criar `tests/Ciclo.Api.Tests/EnrollmentRenewalTests.cs` em xUnit, com um `AppDbContext` in-memory derivado de `AppDbContext` e um `ITenantContext` de tenant fixo declarados no próprio arquivo, no mesmo formato de `AuditOperationCoverageTests`, para que o cache de modelo do EF não fixe o tenant de outro teste.
2. Semeiar `Tenant`, `Student`, `EnrollmentPeriod`, `DocumentType` e `Document` por helpers locais e instanciar `new EnrollmentRenewalService(db, new EnrollmentService(db, trail))`, usando `NoopAuditTrail.Instance` para os cenários sem verificação de trilha.
3. Escrever os cenários listados em Testing Strategy: classificação de documento reaproveitável e vencido, tipos pendentes, ordem por nome, aluno omitido por `AnoLetivo` igual ao do período, `EnrollmentAlreadyExists`, precedência `student_inactive` sobre `pending_required_documents`, `period_not_found`, renovação com `Aprovado` mais `ApprovedAt` mais `Student.AnoLetivo` promovido mais um evento `AuditActions.EnrollmentApproved`, duas execuções com `SkippedAlreadyExists`, lote misto com `BlockedPendingDocuments` e `Renewed`, `student_ids_required`, `studentId` repetido, `student_not_found`, aluno bloqueado mantendo `AnoLetivo` e trilha que lança impedindo a aprovação.
4. Para o cenário de falha de auditoria, usar o dublê de trilha que lança já disponível na suíte e, após a exceção convertida em item `Failed`, chamar `ChangeTracker.Clear()` antes de reler o contexto e afirmar que a matrícula não está aprovada.
**Expected Files:**
- `tests/Ciclo.Api.Tests/EnrollmentRenewalTests.cs`
**Validation:** `dotnet test tests/Ciclo.Api.Tests --filter "FullyQualifiedName~EnrollmentRenewalTests"` conclui com todos os testes passando.
**Status:** [x] done

### Task 4: Tipos, cliente de API e regras puras do frontend

**Objective:** Expor a prévia e o lote no cliente HTTP e isolar a seleção de alunos e o rótulo de desfecho em funções puras testáveis.
**Requirements Covered:** FR-003, FR-006
**Design References:** Component: Cliente de API do frontend; Component: Regras puras da tela; Data Structures.
**Actions:**
1. Em `frontend/src/types/index.ts`, adicionar as interfaces `RenewalPreview`, `RenewalPreviewItem`, `RenewalBatchRequest`, `RenewalBatchItemResult`, `RenewalBatchResult` e a união literal `RenewalOutcome`, com os campos camelCase declarados na seção Data Structures do design.
2. Em `frontend/src/api/enrollments.ts`, adicionar `getRenewalPreview(periodId: string)` chamando `client.get('/enrollments/renewal-preview', { params: { periodId } })` e `renewEnrollmentBatch(request)` chamando `client.post('/enrollments/renewal-batch', request)`, ambas devolvendo `response.data` no mesmo padrão dos métodos já existentes no módulo.
3. Criar `frontend/src/domain/renewalBatch.ts` com `selectableStudentIds(items: RenewalPreviewItem[]): string[]`, que devolve `item.studentId` apenas para `item.eligible === true`, e `describeOutcome(outcome: RenewalOutcome): string`, que devolve `Renovado`, `Já existia`, `Documentação pendente` e `Falhou` para os quatro valores do enum.
4. Não capturar erro dentro das funções puras nem dentro das funções do cliente; o erro sobe para a página que o apresenta.
**Expected Files:**
- `frontend/src/types/index.ts`
- `frontend/src/api/enrollments.ts`
- `frontend/src/domain/renewalBatch.ts`
**Validation:** `npm --prefix frontend run build` conclui sem erro de tipo; `frontend/src/api/enrollments.ts` contém `getRenewalPreview` e `renewEnrollmentBatch`; `frontend/src/domain/renewalBatch.ts` não possui `catch`.
**Status:** [x] done

### Task 5: Página administrativa de prévia e confirmação

**Objective:** Entregar a tela que carrega a prévia, permite marcar alunos elegíveis e mostra o desfecho por aluno depois da confirmação.
**Requirements Covered:** FR-006
**Design References:** Component: Página administrativa; Data Flow.
**Actions:**
1. Criar `frontend/src/pages/admin/EnrollmentRenewal.tsx` exportando `EnrollmentRenewal`, com os estados `periodId`, `preview`, `selected`, `result`, `loading` e `error`, seguindo a estrutura visual das páginas administrativas existentes.
2. Buscar os períodos por `getEnrollmentPeriods` na montagem e, ao escolher um período, chamar `getRenewalPreview(periodId)` e guardar o resultado em `preview`.
3. Renderizar uma linha por `preview.items` com `studentName`, a lista `carriedDocuments` rotulando `reaproveitado` quando `isExpired` é falso e `vencido` quando verdadeiro, `missingDocumentTypes` e `blockReason`; desabilitar o checkbox de seleção quando `eligible` for falso.
4. Na confirmação, não disparar requisição quando `selected` estiver vazio e informar a ausência de seleção; com seleção, chamar `renewEnrollmentBatch({ enrollmentPeriodId: periodId, studentIds: selectableStudentIds(items selecionados) })`, guardar o retorno em `result` e renderizar por item `describeOutcome(item.outcome)` e `item.message`.
5. Em `frontend/src/App.tsx`, registrar a rota `/admin/enrollment-renewal` apontando para `EnrollmentRenewal`, no mesmo bloco de rotas administrativas de `/admin/enrollments`.
**Expected Files:**
- `frontend/src/pages/admin/EnrollmentRenewal.tsx`
- `frontend/src/App.tsx`
**Validation:** `npm --prefix frontend run build` conclui sem erro de tipo; a página chama `getRenewalPreview` e `renewEnrollmentBatch` e nenhum `catch` vazio existe no arquivo novo.
**Status:** [x] done

### Task 6: Testes do frontend das regras puras

**Objective:** Provar a seleção restrita a alunos elegíveis e o rótulo de cada desfecho.
**Requirements Covered:** FR-006
**Design References:** Testing Strategy.
**Actions:**
1. Criar `frontend/tests/renewalBatch.test.ts` usando `node:test` e `node:assert/strict`, no formato dos testes existentes em `frontend/tests`.
2. Cobrir `selectableStudentIds` com uma lista contendo itens elegíveis e não elegíveis, afirmando que só os elegíveis aparecem no retorno, e com lista vazia, afirmando retorno vazio.
3. Cobrir `describeOutcome` para os quatro valores de `RenewalOutcome`, afirmando que cada um devolve um rótulo não vazio e distinto dos demais.
**Expected Files:**
- `frontend/tests/renewalBatch.test.ts`
**Validation:** `npm --prefix frontend test` conclui com todos os testes passando, incluindo `renewalBatch.test.ts`.
**Status:** [x] done
