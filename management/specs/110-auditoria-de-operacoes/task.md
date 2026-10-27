<!-- PIPELINE-CONTRACT: Task completion is determined by "**Status:** [x] done" at the end of each "### Task N" section.
     Checkboxes "- [ ]" under "**Validation:**" are human verification guides — the pipeline ignores them completely.
     To mark a task as done, add "**Status:** [x] done". To mark it as pending, use "**Status:** [ ] pending". -->
# Spec 110: Auditoria de Operações — Implementation Tasks

## Planned Scaffolds

Nenhum fixture efêmero é citado por este spec. Todos os caminhos `src/**` referenciados no design são criados ou modificados pelas tarefas abaixo.

## Task Order

Tasks MUST be executed sequentially in numerical order. Each task depends on the completion of all preceding tasks.

## Tasks

### Task 1: Contratos e vocabulário de auditoria no domínio

**Objective:** Criar a entidade, o vocabulário, os contratos e a exceção de imutabilidade em `Ciclo.Core`, sem dependência de ASP.NET Core ou EF Core.
**Requirements Covered:** FR-001, FR-003, FR-004 (resolvedor de correlação), FR-002 (exceção), NFR-004
**Design References:** Component `AuditEvent`, `AuditActions`/`AuditTargetTypes`, `AuditEntry`/`IAuditTrail`/`IAuditActor`, `AuditCorrelation`, `AuditImmutabilityException`
**Actions:**
1. Criar `src/Ciclo.Core/Entities/AuditEvent.cs` exatamente como no design (implementa `ITenantScoped`, `Id` inicializado com `Guid.NewGuid()`, `OccurredAt` com `DateTime.UtcNow`, `using Ciclo.Core.Interfaces;`).
2. Criar `src/Ciclo.Core/Auditing/AuditActions.cs` com as seis constantes: `enrollment.approved`, `enrollment.rejected`, `enrollment.cancelled`, `document.verified`, `document.rejected`, `session.revoked`.
3. Criar `src/Ciclo.Core/Auditing/AuditTargetTypes.cs` com `Enrollment`, `Document`, `Session`.
4. Criar `src/Ciclo.Core/Auditing/AuditEntry.cs` com o record posicional do design (`object? Before = null`, `object? After = null`).
5. Criar `src/Ciclo.Core/Interfaces/IAuditTrail.cs` com o único membro `void Record(AuditEntry entry);` — nenhum método de leitura, remoção ou alteração.
6. Criar `src/Ciclo.Core/Interfaces/IAuditActor.cs` com as cinco propriedades somente-leitura do design.
7. Criar `src/Ciclo.Core/Auditing/AuditCorrelation.cs` como classe `static partial` com `[GeneratedRegex]` (evita SYSLIB1045 sob `TreatWarningsAsErrors`), `HeaderName = "X-Correlation-Id"`, `MaxLength = 64` e `Resolve(string? candidate, string traceIdentifier)`.
8. Criar `src/Ciclo.Core/Exceptions/AuditImmutabilityException.cs` herdando `InvalidOperationException` com construtor de mensagem.
9. Não adicionar nenhuma dependência de pacote nova.

**Validation:**
- [ ] Compilação sem erros e sem avisos (build com `MSBuild.exe`, conforme L1; encerrar processos `Ciclo.Api` órfãos antes).
- [ ] `grep -c "public const string" src/Ciclo.Core/Auditing/AuditActions.cs` retorna 6.
- [ ] `IAuditTrail` contém apenas `Record`.
**Status:** [x] done

### Task 2: Modelo de persistência e guarda de imutabilidade no `AppDbContext`

**Objective:** Persistir `AuditEvent` com índices e tipo `jsonb`, e impedir alteração/remoção indevida na camada de aplicação.
**Requirements Covered:** FR-001, FR-002, FR-011, NFR-003
**Design References:** Component `AppDbContext` (bloco de configuração, guarda, substituições de `SaveChangesAsync`)
**Actions:**
1. Em `src/Ciclo.Infrastructure/Data/AppDbContext.cs`, adicionar `using Ciclo.Infrastructure.Auditing;` e `public DbSet<AuditEvent> AuditEvents { get; set; } = null!;` junto aos demais `DbSet`.
2. Inserir o bloco de configuração `builder.Entity<AuditEvent>` do design em `OnModelCreating`, antes do laço que aplica o filtro global de tenant (linhas 280-293 do arquivo atual).
3. Adicionar o método privado `GuardAuditImmutability()` exatamente como no design.
4. Chamar `GuardAuditImmutability();` nas duas substituições existentes de `SaveChangesAsync` (após `AutoSetTenantId()`).
5. Não criar interceptor, não substituir `SaveChanges()` síncrono (não há chamadas síncronas no projeto) e não alterar o filtro global existente.

**Validation:**
- [ ] Build 0 erros / 0 avisos.
- [ ] `TenantScopedEntitiesTests` continua passando (a entidade nova implementa `ITenantScoped`).
- [ ] `AutoSetTenantIdTests` continua passando (a guarda não interfere em `Added`).
**Status:** [x] done

### Task 3: Configuração, contexto do ator e porta de escrita

**Objective:** Implementar `AuditSettings`, `AuditActor` e `AuditTrail`.
**Requirements Covered:** FR-003, FR-004 (normalização), FR-010 (configuração), NFR-005, NFR-006
**Design References:** Component `AuditSettings`, `AuditActor`, `AuditTrail`
**Actions:**
1. Criar `src/Ciclo.Infrastructure/Auditing/AuditSettings.cs` com `Read(IConfiguration)` validando `90 ≤ RetentionDays ≤ 3650`, default 365 e mensagens que citam a faixa.
2. Criar `src/Ciclo.Infrastructure/Auditing/AuditActor.cs` com `Capture` e `SetCorrelationId`, truncando `Role` em 32, `IpAddress` em 64 e `UserAgent` em 512; valores vazios viram `null`.
3. Criar `src/Ciclo.Infrastructure/Auditing/AuditTrail.cs` exatamente como no design: validações, `TenantNotResolvedException` quando `!IsResolved`, serialização camelCase, limite `MaxPayloadCharacters = 8192`, `_db.AuditEvents.Add` e nenhuma chamada a `SaveChangesAsync`.
4. Não adicionar `try/catch` em `AuditTrail` (exigência verificada por `grep-absent`).

**Validation:**
- [ ] Build 0 erros / 0 avisos.
- [ ] `grep -n "SaveChanges" src/Ciclo.Infrastructure/Auditing/AuditTrail.cs` não retorna nada.
- [ ] `grep -n "catch" src/Ciclo.Infrastructure/Auditing/AuditTrail.cs` não retorna nada.
**Status:** [ ] pending

### Task 4: Migration `AddAuditEvents` com gatilhos append-only

**Objective:** Criar a migration a partir do modelo e acrescentar os gatilhos de imutabilidade no PostgreSQL.
**Requirements Covered:** FR-002, FR-012
**Design References:** Component `AppDbContext`, seção `Migration e gatilhos`
**Actions:**
1. Gerar a migration com a receita registrada em `management/cross-session/plano-correcoes.md` §1 (CLI do EF via `.nupkg` extraído, `APPDATA` exportado, `--no-build`): `dotnet ef migrations add AddAuditEvents --project src/Ciclo.Infrastructure --startup-project src/Ciclo.Api`.
2. Confirmar que o arquivo gerado contém `CreateTable` de `AuditEvents`, os três índices e a atualização de `src/Ciclo.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`.
3. Acrescentar ao final de `Up` o comando `migrationBuilder.Sql` com a função `audit_events_append_only_guard` e os gatilhos `audit_events_no_update` e `audit_events_no_delete`, exatamente como no design (raw string literal com `$$`).
4. Acrescentar ao início de `Down` o comando `migrationBuilder.Sql` que remove os gatilhos e a função, antes do `DropTable` gerado.
5. Não editar à mão o conteúdo gerado de tabela e índices.

**Validation:**
- [ ] Build 0 erros / 0 avisos (código de migration é `generated_code`; o `.editorconfig` já o exclui de analisadores de estilo).
- [ ] `MigrationHistoryTests` passa, incluindo `HasPendingModelChanges() == false`.
- [ ] `grep -c "audit_events_no_update\|audit_events_no_delete\|audit_events_append_only_guard" src/Ciclo.Infrastructure/Migrations/*_AddAuditEvents.cs` retorna 3 ou mais.
**Status:** [ ] pending

### Task 5: Middleware de captura e wiring no `Program`

**Objective:** Capturar ator/origem por requisição, ecoar correlação e registrar todos os componentes no host.
**Requirements Covered:** FR-004, FR-010, NFR-006
**Design References:** Component `AuditContextMiddleware`, seção `Registro em Program.cs`
**Actions:**
1. Criar `src/Ciclo.Api/Middleware/AuditContextMiddleware.cs` exatamente como no design (`using System.Security.Claims;`, `using Ciclo.Core.Auditing;`, `using Ciclo.Infrastructure.Auditing;`).
2. Em `src/Ciclo.Api/Program.cs`, adicionar `using Ciclo.Core.Auditing;`, `using Ciclo.Core.Interfaces;`, `using Ciclo.Infrastructure.Auditing;` e `using Ciclo.Infrastructure.Services;` conforme necessário.
3. Registrar `AuditSettings.Read(builder.Configuration)` como singleton, `AuditActor` como scoped, `IAuditActor` resolvendo a mesma instância, `IAuditTrail`, `IAuditQueryService`, `AuditRetentionPurger` e, somente fora do ambiente `Testing`, `AddHostedService<AuditRetentionService>()`.
4. Inserir `app.UseMiddleware<AuditContextMiddleware>();` imediatamente após `app.UseMiddleware<TenantMiddleware>();` e antes de `app.UseAuthorization();`.
5. Nenhum ajuste em `Program` pode alterar a ordem existente de `ExceptionMiddleware`, `UseRateLimiter`, `UseAuthentication`, `UseAuthorization` ou `MapControllers`.

**Validation:**
- [ ] Build 0 erros / 0 avisos.
- [ ] `ApiAuthorizationPipelineTests`, `ApiRateLimitingPipelineTests` e `HealthControllerTests` passam (o host sobe com o middleware novo).
- [ ] `Health_EchoesTheCorrelationIdHeader` será criado na Task 15; manualmente, `curl -i http://localhost:5264/health` mostra `X-Correlation-Id`.
**Status:** [ ] pending

### Task 6: Instrumentar decisões de matrícula

**Objective:** Registrar auditoria em aprovação, rejeição e cancelamento de matrícula.
**Requirements Covered:** FR-005, NFR-003
**Design References:** Component `AuditTrail`, tabela de snapshots
**Actions:**
1. Em `src/Ciclo.Infrastructure/Services/EnrollmentService.cs`, adicionar `using Ciclo.Core.Auditing;`, `using Ciclo.Core.Interfaces;` e o campo `private readonly IAuditTrail _audit;`; alterar o construtor para `public EnrollmentService(AppDbContext db, IAuditTrail audit)`.
2. Em `ApproveAsync`, capturar `var previousStatus = enrollment.Status.ToString();` antes de mutar, e chamar `_audit.Record` com `AuditActions.EnrollmentApproved`, `AuditTargetTypes.Enrollment`, `enrollment.Id`, `new { Status = previousStatus }` e `new { Status = enrollment.Status.ToString(), ApprovedAt = enrollment.ApprovedAt }` imediatamente antes do `await _db.SaveChangesAsync();`.
3. Em `RejectAsync`, o mesmo padrão com `AuditActions.EnrollmentRejected` e `After` contendo `Status` e `MotivoRejeicao = request.Motivo`.
4. Em `CancelAsync`, o mesmo padrão com `AuditActions.EnrollmentCancelled` e `After` contendo apenas `Status`.
5. Não registrar evento em `CreateEnrollmentAsync`, `SubmitAsync`, `ClosePeriodAsync` ou em qualquer caminho que lance exceção antes da mutação.

**Validation:**
- [ ] Build 0 erros / 0 avisos.
- [ ] `grep -c "_audit.Record" src/Ciclo.Infrastructure/Services/EnrollmentService.cs` retorna 3.
- [ ] `grep -c "enrollment.approved" src/Ciclo.Infrastructure/Services/EnrollmentService.cs` retorna 0 (usa constante).
**Status:** [ ] pending

### Task 7: Instrumentar verificação documental

**Objective:** Registrar auditoria em aprovação e rejeição de documento.
**Requirements Covered:** FR-006, NFR-003
**Design References:** tabela de snapshots
**Actions:**
1. Em `src/Ciclo.Infrastructure/Services/DocumentService.cs`, adicionar `using Ciclo.Core.Auditing;`, `using Ciclo.Core.Interfaces;`, o campo `_audit` e o parâmetro `IAuditTrail audit` ao construtor (último parâmetro).
2. Em `VerifyAsync`, capturar `var previousStatus = doc.Status.ToString();` e `var previousValidity = doc.DataValidade;` antes de qualquer mutação.
3. Após o bloco de notificação de rejeição e antes de `await _db.SaveChangesAsync();`, chamar `_audit.Record` com ação `request.Approved ? AuditActions.DocumentVerified : AuditActions.DocumentRejected`, alvo `AuditTargetTypes.Document` e `doc.Id`, `Before` com `Status` e `DataValidade`, `After` com `Status`, `DataValidade` e `MotivoRejeicao`.
4. Não registrar evento quando `document_not_found` ou `motivo_required_10_to_500_chars` forem lançados.

**Validation:**
- [ ] Build 0 erros / 0 avisos.
- [ ] `grep -c "_audit.Record" src/Ciclo.Infrastructure/Services/DocumentService.cs` retorna 1.
**Status:** [ ] pending

### Task 8: Instrumentar revogação de sessão

**Objective:** Registrar um evento por sessão efetivamente revogada, incluindo o ramo de reuso detectado.
**Requirements Covered:** FR-007, NFR-003
**Design References:** tabela de snapshots
**Actions:**
1. Em `src/Ciclo.Infrastructure/Services/AuthService.cs`, adicionar `using Ciclo.Core.Auditing;`, `using Ciclo.Core.Interfaces;`, o campo `_audit` e o parâmetro `IAuditTrail audit` ao final do construtor.
2. No ramo de reuso, capturar `var revokedAt = DateTime.UtcNow;` e, dentro do laço sobre `allTokens`, atribuir `t.RevokedAt = revokedAt` e chamar `_audit.Record` com `AuditActions.SessionRevoked`, `AuditTargetTypes.Session`, `t.Id`, `Before = new { RevokedAt = (DateTime?)null }` e `After = new { RevokedAt = t.RevokedAt, Reason = "reuse_detected" }`.
3. No ramo simples, chamar `_audit.Record` com o mesmo formato e `After = new { RevokedAt = storedToken.RevokedAt }` (sem `Reason`).
4. Não registrar evento quando `storedToken is null`; preservar o retorno silencioso atual.

**Validation:**
- [ ] Build 0 erros / 0 avisos.
- [ ] `grep -c "_audit.Record" src/Ciclo.Infrastructure/Services/AuthService.cs` retorna 2.
- [ ] `AuthServiceTests` compila e passa após a Task 12.
**Status:** [ ] pending

### Task 9: Contratos de leitura, CSV e serviço de consulta

**Objective:** Implementar filtro, DTO, serializador CSV e serviço de consulta/exportação.
**Requirements Covered:** FR-008, FR-009, NFR-001, NFR-002 (projeção explícita)
**Design References:** Component `AuditCsvWriter`, `AuditQueryService`, `Data Structures`
**Actions:**
1. Criar `src/Ciclo.Infrastructure/Contracts/AuditDtos.cs` com `AuditEventFilter` e `AuditEventDto` exatamente como no design (namespace `Ciclo.Infrastructure.Contracts`).
2. Criar `src/Ciclo.Infrastructure/Auditing/AuditCsvWriter.cs` com `Write(IReadOnlyList<AuditEventDto> events)`, cabeçalho literal fixo, `\r\n`, ISO UTC `yyyy-MM-dd'T'HH:mm:ss.fff'Z'` com `CultureInfo.InvariantCulture` e escape RFC 4180. Escrever campo a campo (sem arrays como argumento) para não disparar CA1861.
3. Criar `src/Ciclo.Infrastructure/Services/AuditQueryService.cs` contendo `IAuditQueryService`, `AuditException` (com `StatusCode`) e `AuditQueryService` com `QueryAsync`, `ExportCsvAsync`, `NormalizeRange`, `BuildQuery` e a projeção estática.
4. Normalizar `DateTimeKind`: `Local` → `ToUniversalTime()`, `Unspecified` → `SpecifyKind(Utc)`, `Utc` inalterado.
5. `BuildQuery` NÃO deve usar `IgnoreQueryFilters`; o filtro global de tenant é obrigatório.
6. Exportação: `CountAsync() > MaxExportRows (10000)` → `AuditException("export_exceeds_limit", 400)`; retorno com BOM UTF-8 (`new UTF8Encoding(true)`, `GetPreamble()` + `GetBytes(csv)`).
7. Constantes nomeadas: `MaxPageSize = 200`, `DefaultPageSize = 50`, `DefaultRangeDays = 30`, `MaxRangeDays = 400`, `MaxExportRows = 10000`.

**Validation:**
- [ ] Build 0 erros / 0 avisos (atenção a CA1861 e SYSLIB1045).
- [ ] `grep -c "AsNoTracking" src/Ciclo.Infrastructure/Services/AuditQueryService.cs` retorna 1.
- [ ] `grep -c "IgnoreQueryFilters" src/Ciclo.Infrastructure/Services/AuditQueryService.cs` retorna 0.
**Status:** [ ] pending

### Task 10: Controlador de auditoria (consulta e exportação)

**Objective:** Expor `GET /api/audit-events` e `GET /api/audit-events/export`, Admin-only.
**Requirements Covered:** FR-008, FR-009, NFR-002
**Design References:** Component `AuditController`
**Actions:**
1. Criar `src/Ciclo.Api/Controllers/AuditController.cs` com `[ApiController]`, `[Route("api/audit-events")]` e `[Authorize(Roles = "Admin")]` na classe.
2. Implementar `GetAll` e `Export` com os parâmetros e o tratamento de `AuditException` do design (`Problem(title: ex.Message, statusCode: ex.StatusCode)`).
3. Não declarar `[Route]` nas ações; não declarar `HttpPut`, `HttpPatch` ou `HttpDelete`.
4. Exportar com nome de arquivo fixo `audit-events.csv` e content type `text/csv; charset=utf-8`.

**Validation:**
- [ ] Build 0 erros / 0 avisos.
- [ ] `ApiRouteConventionTests` passa (rota sob `api/`).
- [ ] `ApiAuthorizationTests` passa (nenhuma rota anônima nova).
- [ ] `grep -c "HttpPut\|HttpDelete\|HttpPatch" src/Ciclo.Api/Controllers/AuditController.cs` retorna 0.
**Status:** [ ] pending

### Task 11: Expurgo de retenção

**Objective:** Implementar o purgador testável e o serviço hospedado de execução diária.
**Requirements Covered:** FR-010, FR-011
**Design References:** Component `AuditRetentionPurger`, `AuditRetentionService`
**Actions:**
1. Criar `src/Ciclo.Infrastructure/Auditing/AuditRetentionPurger.cs` com `PurgeAsync(DateTime utcNow, CancellationToken cancellationToken)`, `PurgeBatchSize = 5000`, `IgnoreQueryFilters()`, `OrderBy(OccurredAt)`, `Take`, `RemoveRange`, `SaveChangesAsync` e retorno do total.
2. Criar `src/Ciclo.Infrastructure/Auditing/AuditRetentionService.cs` como `BackgroundService` com `PeriodicTimer(TimeSpan.FromHours(24))`, escopo por execução, `LogInformation` do total, `catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)` retornando, e `LogError` para as demais exceções sem derrubar o host.
3. O registro do serviço hospedado já foi feito na Task 5, condicionado a `!IsEnvironment("Testing")`; não duplicar o registro.

**Validation:**
- [ ] Build 0 erros / 0 avisos.
- [ ] `grep -c "IgnoreQueryFilters" src/Ciclo.Infrastructure/Auditing/AuditRetentionPurger.cs` retorna 1.
- [ ] O host de teste (`HealthControllerTests`) continua subindo sem tentar conectar ao banco.
**Status:** [ ] pending

### Task 12: Atualizar testes existentes para os novos construtores

**Objective:** Fazer a suíte existente compilar e passar com as dependências de auditoria injetadas.
**Requirements Covered:** NFR-003, NFR-005
**Design References:** seção `Arquivos modificados`
**Actions:**
1. Criar `tests/Ciclo.Api.Tests/AuditTestData.cs` com: `internal sealed class RecordingAuditTrail : IAuditTrail` (guarda `List<AuditEntry> Entries`), `internal sealed class ThrowingAuditTrail : IAuditTrail` (lança `InvalidOperationException`), `internal sealed class FixedTenantContext : ITenantContext` (aninhada, como em `DocumentComplianceTests`) e `internal static (AppDbContext Db, Guid TenantId) CreateContext()` usando contexto derivado de `AppDbContext`, InMemory de nome único (`Guid.NewGuid().ToString()`) e tenant fixo.
2. Em `tests/Ciclo.Api.Tests/DocumentComplianceTests.cs`, passar `new RecordingAuditTrail()` aos construtores de `EnrollmentService` e `DocumentService`.
3. Em `tests/Ciclo.Api.Tests/AuthServiceTests.cs`, registrar `IAuditActor` (instância de `AuditActor`) e `IAuditTrail` (instância de `RecordingAuditTrail`) no `ServiceCollection` usado para resolver `AuthService`.
4. Procurar outros sítios de construção direta com `grep -rn "new EnrollmentService(\|new DocumentService(\|new AuthService(" tests/` e atualizar todos.
5. Não alterar asserções existentes.

**Validation:**
- [ ] Build 0 erros / 0 avisos.
- [ ] Suíte completa passa com a contagem anterior acrescida de zero testes (somente adaptação).
**Status:** [ ] pending

### Task 13: Testes de escrita, imutabilidade, correlação, ator e configuração

**Objective:** Cobrir a porta de escrita, as duas camadas de imutabilidade (aplicação e texto da migration), a função pura de correlação, o ator e a configuração.
**Requirements Covered:** FR-001, FR-002, FR-003, FR-004, FR-010, NFR-005, NFR-006
**Design References:** `Testing Strategy`, linhas de `AuditTrailTests`, `AuditImmutabilityTests`, `AuditCorrelationTests`, `AuditActorTests`, `AuditSettingsTests`
**Actions:**
1. Criar `tests/Ciclo.Api.Tests/AuditTrailTests.cs` com os cinco métodos previstos; `Record_DoesNotSaveChanges` verifica `ChangeTracker.Entries<AuditEvent>()` em estado `Added` e que uma consulta com `IgnoreQueryFilters` antes do `SaveChanges` não encontra o evento.
2. Criar `tests/Ciclo.Api.Tests/AuditImmutabilityTests.cs` com os quatro métodos previstos; `AuditControllerExposesNoMutationEndpoint` usa reflexão sobre `typeof(AuditController).GetMethods()` e falha se houver `HttpPutAttribute`, `HttpPatchAttribute` ou `HttpDeleteAttribute`.
3. Criar `tests/Ciclo.Api.Tests/AuditCorrelationTests.cs` e `tests/Ciclo.Api.Tests/AuditActorTests.cs`.
4. Criar `tests/Ciclo.Api.Tests/AuditSettingsTests.cs` com os cinco métodos previstos, incluindo `TheDatabaseTriggerUsesTheSameImmutabilityFloor`, que instancia `AddAuditEvents`, cria `new MigrationBuilder("Npgsql")`, invoca `Up` por reflexão (`BindingFlags.Instance | BindingFlags.NonPublic`), coleta `builder.Operations.OfType<SqlOperation>().Select(o => o.Sql)` e afirma que o texto contém `90 days`, e que `AuditSettings.MinimumRetentionDays == 90`.
5. Nenhum teste pode depender de PostgreSQL para passar.

**Validation:**
- [ ] Build 0 erros / 0 avisos.
- [ ] Suíte completa verde.
- [ ] Os testes de imutabilidade falham se `GuardAuditImmutability()` for removido (controle negativo executado na Task 17).
**Status:** [ ] pending

### Task 14: Testes de cobertura das operações sensíveis e de atomicidade

**Objective:** Provar que cada operação sensível grava exatamente o evento esperado e que falha de auditoria impede a operação.
**Requirements Covered:** FR-005, FR-006, FR-007, FR-011, NFR-003
**Design References:** `Testing Strategy`, linhas de `AuditOperationCoverageTests`
**Actions:**
1. Criar `tests/Ciclo.Api.Tests/AuditOperationCoverageTests.cs` usando `AuditTestData.CreateContext()` e `RecordingAuditTrail`.
2. Semear os dados mínimos por teste: `Tenant`, `EnrollmentPeriod`, `Student`, `Enrollment` (status compatível com a transição), `DocumentType`, `Document`, `User`/`RefreshToken` conforme a operação.
3. Implementar os oito métodos previstos no design, do `Approve_WritesEnrollmentApprovedEvent` a `Approve_WhenAuditFails_DoesNotChangeTheEnrollment`.
4. Em `Approve_WhenAuditFails_DoesNotChangeTheEnrollment`, usar `ThrowingAuditTrail` e afirmar: a exceção é lançada, o `Enrollment` recarregado com `IgnoreQueryFilters` mantém o status anterior e não existe `AuditEvent`.
5. Em `Revoke_WritesSessionRevokedEvent`, afirmar alvo `("Session", token.Id)` e `revokedAt` não nulo; em `RevokeOnReuse_RecordsTheReason`, afirmar `reason` igual a `reuse_detected`.

**Validation:**
- [ ] Build 0 erros / 0 avisos.
- [ ] Suíte completa verde.
- [ ] `FailedTransition_WritesNoEvent` falha se o registro for movido para antes da validação de transição (controle negativo na Task 17).
**Status:** [ ] pending

### Task 15: Testes de consulta, exportação, isolamento e correlação no pipeline

**Objective:** Cobrir filtros, ordenação, paginação, CSV, isolamento por tenant e eco do cabeçalho de correlação.
**Requirements Covered:** FR-008, FR-009, FR-011, FR-004
**Design References:** linhas de `AuditQueryServiceTests`, `AuditCsvWriterTests`, `AuditPipelineTests`
**Actions:**
1. Criar `tests/Ciclo.Api.Tests/AuditQueryServiceTests.cs` com os nove métodos previstos; semear eventos com `OccurredAt` explícitos e dois tenants distintos (um deles fora do filtro).
2. Criar `tests/Ciclo.Api.Tests/AuditCsvWriterTests.cs` com os três métodos previstos.
3. Criar `tests/Ciclo.Api.Tests/AuditPipelineTests.cs` usando `ApiTestHost`: `GET /health` com `X-Correlation-Id` válido no request e afirmar que a resposta devolve o mesmo valor; um segundo caso envia valor inválido (`"ab"`) e afirma que a resposta devolve um identificador diferente e não vazio.
4. Em `Query_DoesNotReturnEventsFromAnotherTenant`, criar dois contextos derivados distintos (tipos derivados diferentes ou bancos distintos) para que o filtro de tenant seja efetivo; afirmar `Total == 0` para o tenant sem eventos.
5. Nenhum teste pode depender de rede externa.

**Validation:**
- [ ] Build 0 erros / 0 avisos.
- [ ] Suíte completa verde.
- [ ] `Query_DoesNotReturnEventsFromAnotherTenant` falha se o `Where` de tenant for removido do modelo (controle negativo na Task 17).
**Status:** [ ] pending

### Task 16: Testes de retenção

**Objective:** Provar que o expurgo remove apenas o que está fora da retenção e respeita o lote.
**Requirements Covered:** FR-010
**Design References:** linhas de `AuditRetentionTests`
**Actions:**
1. Criar `tests/Ciclo.Api.Tests/AuditRetentionTests.cs` com os três métodos previstos, usando `AuditSettings(365)` e um `utcNow` fixo (`new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)`).
2. Semear eventos com `TenantId` explícito em um contexto com tenant não resolvido (mock de `ITenantContext` com `IsResolved = false`), para provar que `IgnoreQueryFilters()` é necessário e suficiente.
3. Afirmar que eventos com 400 dias são removidos e eventos com 10 dias permanecem.
4. Adicionar um caso com mais de `PurgeBatchSize` eventos apenas se o custo de tempo for aceitável (5001 registros em InMemory); caso contrário, cobrir o laço com 3 eventos e documentar no comentário do teste.

**Validation:**
- [ ] Build 0 erros / 0 avisos.
- [ ] Suíte completa verde.
- [ ] `Purge_DeletesOnlyEventsOlderThanRetention` falha se `IgnoreQueryFilters()` for removido (controle negativo na Task 17).
**Status:** [ ] pending

### Task 17: Controles negativos e evidência

**Objective:** Provar que os testes novos detectam as falhas que afirmam detectar.
**Requirements Covered:** NFR-003, NFR-005
**Design References:** seção `Testing Strategy`, parágrafo de controle negativo
**Actions:**
1. Remover temporariamente `GuardAuditImmutability()` do `AppDbContext`, executar a suíte e registrar as falhas; restaurar e reconfirmar verde.
2. Mover temporariamente o `_audit.Record` de `ApproveAsync` para antes da validação de transição, executar a suíte, registrar as falhas; restaurar.
3. Remover temporariamente o laço de filtro global do `AppDbContext` (ou o `Where` de tenant) e registrar a falha de `Query_DoesNotReturnEventsFromAnotherTenant`; restaurar.
4. Remover temporariamente `IgnoreQueryFilters()` do purgador e registrar a falha de `Purge_DeletesOnlyEventsOlderThanRetention`; restaurar.
5. Registrar cada controle (comando, teste que falhou, mensagem) em `management/cross-session/plano-correcoes.md`, na seção de registro de controles negativos.

**Validation:**
- [ ] Os quatro controles falharam antes da restauração e a suíte voltou a verde depois.
- [ ] Nenhuma alteração temporária permaneceu na árvore (`git diff` apenas com as mudanças intencionais do spec).
**Status:** [ ] pending

### Task 18: ADR-004, validação com PostgreSQL real e gates finais

**Objective:** Registrar a decisão de retenção/base legal, validar a migration e os gatilhos contra PostgreSQL real e fechar os gates.
**Requirements Covered:** FR-013, FR-012, FR-002
**Design References:** AD-110-06, seção `Migration / Rollback`
**Actions:**
1. Acrescentar ao final de `management/adr.md` o `ADR-004: Retenção e Imutabilidade da Trilha de Auditoria`, com Status, Contexto, Decisão (365 dias padrão, faixa 90–3650, piso 90 dias, exportação Admin-only) e Consequências (base legal LGPD Art. 7º II e IX e Art. 16; eventos fora do piso podem ser expurgados; alteração de evento é sempre bloqueada).
2. Subir o PostgreSQL local (`docker compose up -d postgres` com `.env` presente, ou instância já em execução) e aplicar a migration com `dotnet ef database update`.
3. Executar no banco: `UPDATE "AuditEvents" SET "Action" = 'x'` em evento recente (deve falhar), `DELETE FROM "AuditEvents"` em evento recente (deve falhar), inserir evento com `"OccurredAt" = now() - interval '100 days'` e removê-lo (deve funcionar).
4. Confirmar os três índices com `\d "AuditEvents"`.
5. Encerrar processos `Ciclo.Api` órfãos, compilar com `MSBuild.exe` e rodar a suíte completa com `dotnet test --no-build`.
6. Verificar que não restaram arquivos temporários, sondas ou `TODO` introduzidos pela spec (`git status` e `grep -rn "TODO" src/`).

**Validation:**
- [ ] `ADR-004` presente em `management/adr.md`.
- [ ] Build 0 erros / 0 avisos; suíte completa verde.
- [ ] Os três comandos SQL de bloqueio falharam com a mensagem dos gatilhos; o `DELETE` fora do piso funcionou.
- [ ] `\d "AuditEvents"` mostra `IX_AuditEvents_TenantId_OccurredAt`, `IX_AuditEvents_TenantId_Action_OccurredAt` e `IX_AuditEvents_TenantId_TargetType_TargetId`.
- [ ] Nenhum arquivo temporário; nenhuma alteração fora do escopo do spec.
**Status:** [x] done
