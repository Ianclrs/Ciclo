<!-- PIPELINE-CONTRACT: Task completion is determined by "**Status:** [x] done" at the end of each "### Task N" section.
     Checkboxes "- [ ]" under "**Validation:**" are human verification guides — the pipeline ignores them completely.
     To mark a task as done, add "**Status:** [x] done". To mark it as pending, use "**Status:** [ ] pending". -->
# Spec 130: Observabilidade Operacional — Implementation Tasks

## Planned Scaffolds

Os caminhos abaixo são criados por esta spec durante o implement e não existem hoje:

- `src/Ciclo.Api/Middleware/CorrelationMiddleware.cs`
- `src/Ciclo.Api/Filters/ProblemDetailsCorrelationFilter.cs`
- `src/Ciclo.Api/Health/DatabaseHealthCheck.cs`
- `src/Ciclo.Api/Health/StorageHealthCheck.cs`
- `src/Ciclo.Api/Health/HealthResponseWriter.cs`
- `src/Ciclo.Core/Interfaces/IEmailQueueStatus.cs`
- `src/Ciclo.Infrastructure/Email/EmailQueueMetrics.cs`
- `tests/Ciclo.Api.Tests/CorrelationTests.cs`
- `tests/Ciclo.Api.Tests/EmailQueueStatusTests.cs`
- `tests/Ciclo.Api.Tests/HealthCheckTests.cs`
- `tests/Ciclo.Api.Tests/ObservabilityEndpointTests.cs`

Nenhum fixture efêmero é citado por este spec. Todo caminho `src/**` referenciado no design é criado ou modificado pelas tarefas abaixo.

## Task Order

Tasks MUST be executed sequentially in numerical order. Each task depends on the completion of all preceding tasks.

## Tasks

### Task 1: Pacote de métricas, métricas HTTP e endpoint `/metrics`

**Objective:** Adicionar a única dependência nova da spec e expor as métricas HTTP para scrape.
**Requirements Covered:** FR-006, NFR-002, NFR-003
**Design References:** AD-130-01, AD-130-07, componente `Program` (pacote e pipeline)
**Actions:**
1. Adicionar a `src/Ciclo.Api/Ciclo.Api.csproj` a referência `prometheus-net.AspNetCore` versão `8.2.1`.
2. Restaurar com o caminho do L1 (`MSBuild.exe -t:Restore` ou `nuget.exe restore`), porque `dotnet restore` está quebrado nesta máquina.
3. Em `src/Ciclo.Api/Program.cs`, registrar `app.UseHttpMetrics();` antes de `app.MapControllers();`.
4. Em `src/Ciclo.Api/Program.cs`, mapear `app.MapMetrics("/metrics");` antes de `app.MapControllers();`, no nível da aplicação (fora de `/api`).
5. Não adicionar rótulo customizado: usar apenas os rótulos padrão do `UseHttpMetrics` (método, rota, código).
6. Encerrar processos `Ciclo.Api` órfãos antes de compilar, para não bloquear a cópia da DLL.
**Expected Files:**
- `src/Ciclo.Api/Ciclo.Api.csproj`
- `src/Ciclo.Api/Program.cs`
- `tests/Ciclo.Api.Tests/ApiTestHost.cs`
**Validation:** `dotnet build Ciclo.sln --no-restore` termina com 0 erros e 0 avisos; a suíte existente continua verde; `curl localhost:5264/metrics` contém `http_requests_received_total`.
**Status:** [x] done

### Task 2: Correlação resolvida na borda

**Objective:** Resolver o identificador uma única vez por request e disponibilizá-lo em `HttpContext.Items`, no cabeçalho de resposta e no `LogContext`.
**Requirements Covered:** FR-003, NFR-004
**Design References:** AD-130-04, componentes `AuditCorrelation`, `CorrelationMiddleware` e `AuditContextMiddleware`
**Actions:**
1. Acrescentar `public const string ItemsKey = "CorrelationId";` a `src/Ciclo.Core/Auditing/AuditCorrelation.cs`, sem alterar `HeaderName`, `MaxLength` nem `Resolve`.
2. Criar `src/Ciclo.Api/Middleware/CorrelationMiddleware.cs` exatamente como no design, com `using Serilog.Context;`, gravando `Items`, o cabeçalho de resposta e o `LogContext`.
3. Alterar `src/Ciclo.Api/Middleware/AuditContextMiddleware.cs`: ler `context.Items[AuditCorrelation.ItemsKey] as string`, usar `AuditCorrelation.Resolve` como fallback e remover a escrita do cabeçalho de resposta.
4. Em `src/Ciclo.Api/Program.cs`, registrar `app.UseMiddleware<CorrelationMiddleware>();` imediatamente após `app.UseSerilogRequestLogging(...)` e antes de `app.UseRateLimiter()`.
5. Não criar um segundo resolvedor em nenhum outro ponto.
**Expected Files:**
- `src/Ciclo.Core/Auditing/AuditCorrelation.cs`
- `src/Ciclo.Api/Middleware/CorrelationMiddleware.cs`
- `src/Ciclo.Api/Middleware/AuditContextMiddleware.cs`
- `src/Ciclo.Api/Program.cs`
- `tests/Ciclo.Api.Tests/AuditInstrumentationTests.cs`
- `tests/Ciclo.Api.Tests/AuditTrailTests.cs`
**Validation:** Build 0/0; requisição com `X-Correlation-Id` ecoa o mesmo valor no cabeçalho de resposta; o `AuditEvent` gravado continua com `CorrelationId` preenchido (suíte da Spec 110 verde).
**Status:** [x] done

### Task 3: Correlação no log

**Objective:** Fazer o identificador aparecer em todo log do request, inclusive no evento de conclusão do Serilog.
**Requirements Covered:** FR-004
**Design References:** AD-130-04, componente `Program` (Serilog e pipeline)
**Actions:**
1. Em `src/Ciclo.Api/Program.cs`, configurar `UseSerilogRequestLogging` com `EnrichDiagnosticContext` lendo `AuditCorrelation.ItemsKey` e chamando `diagnostic.Set("CorrelationId", valor)`.
2. Trocar o sink de console para o template `"[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"`, mantendo `formatProvider: CultureInfo.InvariantCulture`.
3. Não incluir corpo de requisição no template.
**Expected Files:**
- `src/Ciclo.Api/Program.cs`
**Validation:** Build 0/0; log de uma requisição com `X-Correlation-Id: teste-123` contém `[teste-123]`; uma mensagem de startup (fora de request) não quebra o template.
**Status:** [x] done

### Task 4: Correlação no ProblemDetails

**Objective:** Incluir `correlationId` em toda resposta `application/problem+json`, sem alterar os chamadores de `FromCode`.
**Requirements Covered:** FR-005
**Design References:** AD-130-05, componentes `ProblemResponse` e `ProblemDetailsCorrelationFilter`
**Actions:**
1. Em `src/Ciclo.Api/Middleware/ProblemResponse.cs`, acrescentar `internal const string CorrelationExtension = "correlationId";`.
2. Em `WriteAsync`, adicionar a extensão quando `context.Items[AuditCorrelation.ItemsKey]` for string não vazia.
3. Criar `src/Ciclo.Api/Filters/ProblemDetailsCorrelationFilter.cs` exatamente como no design.
4. Em `src/Ciclo.Api/Program.cs`, registrar `builder.Services.AddControllers(options => options.Filters.Add<ProblemDetailsCorrelationFilter>());`.
5. Manter `FromCode` com a assinatura atual e não alterar nenhum controller.
**Expected Files:**
- `src/Ciclo.Api/Middleware/ProblemResponse.cs`
- `src/Ciclo.Api/Filters/ProblemDetailsCorrelationFilter.cs`
- `src/Ciclo.Api/Program.cs`
- `tests/Ciclo.Api.Tests/ProblemResponseTests.cs`
**Validation:** Build 0/0; login inválido (`401 invalid_credentials`) retorna `correlationId` no corpo; requisição sem token (`401 unauthorized`) retorna `correlationId` no corpo; nenhum controller foi alterado.
**Status:** [x] done

### Task 5: Porta de status da fila de e-mail

**Objective:** Expor profundidade e capacidade da fila sem criar contador paralelo.
**Requirements Covered:** FR-007
**Design References:** AD-130-06, componentes `IEmailQueueStatus` e `ChannelEmailDispatcher`
**Actions:**
1. Criar `src/Ciclo.Core/Interfaces/IEmailQueueStatus.cs` com `int Depth { get; }` e `int Capacity { get; }`.
2. Em `src/Ciclo.Infrastructure/Email/EmailDispatcher.cs`, implementar `IEmailQueueStatus` em `ChannelEmailDispatcher`.
3. Renomear a constante `Capacity` para `QueueCapacity` e expor `public int Capacity => QueueCapacity;` e `public int Depth => _channel.Reader.Count;`.
4. Em `src/Ciclo.Infrastructure/Email/EmailDispatchSetup.cs`, registrar `services.AddSingleton<IEmailQueueStatus>(sp => (IEmailQueueStatus)sp.GetRequiredService<IEmailDispatcher>());`.
5. Manter o comportamento de `TryWrite` e o log de descarte inalterados.
**Expected Files:**
- `src/Ciclo.Core/Interfaces/IEmailQueueStatus.cs`
- `src/Ciclo.Infrastructure/Email/EmailDispatcher.cs`
- `src/Ciclo.Infrastructure/Email/EmailDispatchSetup.cs`
- `tests/Ciclo.Api.Tests/EmailDispatchTests.cs`
**Validation:** Build 0/0; teste manual: enfileirar 3 mensagens sem worker → `Depth` é 3; `Capacity` continua 1000.
**Status:** [x] done

### Task 6: Gauge da fila no Prometheus

**Objective:** Publicar `ciclo_email_queue_depth` e `ciclo_email_queue_capacity` lidos na coleta.
**Requirements Covered:** FR-007, FR-009
**Design References:** AD-130-06, componente `EmailQueueMetrics`
**Actions:**
1. Criar `src/Ciclo.Infrastructure/Email/EmailQueueMetrics.cs` exatamente como no design, registrando o callback de coleta em `Metrics.DefaultRegistry.AddBeforeCollectCallback(Collect)`.
2. Em `src/Ciclo.Infrastructure/Email/EmailDispatchSetup.cs`, registrar `services.AddSingleton<EmailQueueMetrics>();` depois de `IEmailQueueStatus`.
3. Não adicionar rótulo com e-mail, usuário ou tenant.
**Expected Files:**
- `src/Ciclo.Infrastructure/Email/EmailQueueMetrics.cs`
- `src/Ciclo.Infrastructure/Email/EmailDispatchSetup.cs`
**Validation:** Build 0/0; `curl localhost:5264/metrics` contém `ciclo_email_queue_depth` e `ciclo_email_queue_capacity`; o valor de capacidade é 1000.
**Status:** [x] done

### Task 7: Health checks de banco e armazenamento

**Objective:** Entregar liveness e readiness com verificações reais, preservando os endpoints antigos.
**Requirements Covered:** FR-001, FR-002, FR-010, NFR-001
**Design References:** AD-130-02, AD-130-03, componentes `DatabaseHealthCheck`, `StorageHealthCheck` e `HealthResponseWriter`
**Actions:**
1. Criar `src/Ciclo.Api/Health/DatabaseHealthCheck.cs` com timeout de 2 segundos e escopo próprio, conforme o design.
2. Criar `src/Ciclo.Api/Health/StorageHealthCheck.cs` gravando e removendo `health-probe.tmp` em `FileStorage:RootPath`.
3. Criar `src/Ciclo.Api/Health/HealthResponseWriter.cs` com o JSON do design.
4. Em `src/Ciclo.Api/Program.cs`, registrar `AddHealthChecks().AddCheck<DatabaseHealthCheck>("db", tags: ["ready"]).AddCheck<StorageHealthCheck>("storage", tags: ["ready"])`.
5. Em `src/Ciclo.Api/Program.cs`, mapear `/health/live` com `Predicate = _ => false` e `/health/ready` com `Predicate = registration => registration.Tags.Contains("ready")`, ambos com o writer.
6. Não alterar `src/Ciclo.Api/Controllers/HealthController.cs`.
**Expected Files:**
- `src/Ciclo.Api/Health/DatabaseHealthCheck.cs`
- `src/Ciclo.Api/Health/StorageHealthCheck.cs`
- `src/Ciclo.Api/Health/HealthResponseWriter.cs`
- `src/Ciclo.Api/Program.cs`
**Validation:** Build 0/0; `curl -i localhost:5264/health/live` → 200 e `{"status":"Healthy"}`; `curl -i localhost:5264/health/ready` → 200 com `db` e `storage` saudáveis; `curl -i localhost:5264/Health` e `/Health/db` continuam 200.
**Status:** [x] done

### Task 8: Documentação operacional e ADR

**Objective:** Registrar os limiares de alerta, os endpoints de observabilidade e a decisão do modelo de métricas.
**Requirements Covered:** FR-008
**Design References:** AD-130-01, AD-130-02, AD-130-07
**Actions:**
1. Acrescentar ao `README.md` uma seção “Operação e observabilidade” com os endpoints `/health/live`, `/health/ready` e `/metrics`, e uma subseção “Limiares de alerta” com: readiness `Unhealthy` por mais de 2 minutos; `5xx` acima de 1% em 5 minutos; `p95` acima de 1 segundo em 5 minutos; profundidade da fila acima de 80% da capacidade por 5 minutos.
2. Em cada limiar, citar a origem (`/health/ready` ou `http_requests_received_total` ou `ciclo_email_queue_depth`) e a ação recomendada.
3. Registrar no README que `/Health` e `/Health/db` continuam válidos e que a migração para os novos probes é configuração de host.
4. Acrescentar ao final de `management/adr.md` o `ADR-005: Exposição de Métricas por Scrape Prometheus`, com Status, Contexto, Decisão e Consequências.
5. Não prometer integração de alerta que não existe.
**Expected Files:**
- `README.md`
- `management/adr.md`
**Validation:** README contém “Limiares de alerta” com `http_requests_received_total` e `ciclo_email_queue_depth`; `management/adr.md` contém `ADR-005`.
**Status:** [x] done

### Task 9: Testes automatizados da observabilidade

**Objective:** Cobrir correlação, ProblemDetails, health checks e profundidade da fila com testes determinísticos.
**Requirements Covered:** FR-001, FR-002, FR-003, FR-005, FR-006, FR-007, FR-010, NFR-004
**Design References:** seção `Testing Strategy`
**Actions:**
1. Criar `tests/Ciclo.Api.Tests/CorrelationTests.cs` cobrindo cabeçalho válido, ausente e com 200 caracteres, além do filtro com e sem item e de `ProblemResponse.WriteAsync` com item.
2. Criar `tests/Ciclo.Api.Tests/EmailQueueStatusTests.cs` construindo `ChannelEmailDispatcher` e enfileirando 3 mensagens sem iniciar o worker.
3. Criar `tests/Ciclo.Api.Tests/HealthCheckTests.cs` cobrindo storage saudável (diretório temporário), storage inválido (raiz apontando para um arquivo) e banco InMemory.
4. Criar `tests/Ciclo.Api.Tests/ObservabilityEndpointTests.cs` usando `IClassFixture<ApiTestHost>` para `/health/live`, `/health/ready`, `/metrics`, `/Health` e `/Health/db`; configurar `FileStorage:RootPath` com diretório temporário e limpar no dispose.
5. Não usar `Thread.Sleep`.
**Expected Files:**
- `tests/Ciclo.Api.Tests/CorrelationTests.cs`
- `tests/Ciclo.Api.Tests/EmailQueueStatusTests.cs`
- `tests/Ciclo.Api.Tests/HealthCheckTests.cs`
- `tests/Ciclo.Api.Tests/ObservabilityEndpointTests.cs`
**Validation:** Build 0/0; suíte completa verde com os novos testes; `/metrics` no teste contém `ciclo_email_queue_depth`.
**Status:** [x] done

### Task 10: Controles negativos, smoke real e fechamento dos gates

**Objective:** Provar que os testes detectam as falhas que dizem detectar e fechar a entrega.
**Requirements Covered:** FR-001, FR-002, FR-003, FR-005, FR-007, FR-008
**Design References:** seção `Testing Strategy` e `Migration / Rollback`
**Actions:**
1. Controle 1: remover a leitura de `Items` do `ProblemDetailsCorrelationFilter` e confirmar que o teste de correlação falha; restaurar.
2. Controle 2: fazer `StorageHealthCheck` devolver `Healthy` sem tocar no disco e confirmar que o teste de storage inválido falha; restaurar.
3. Controle 3: substituir `Depth` por `0` em `ChannelEmailDispatcher` e confirmar que o teste de profundidade falha; restaurar.
4. Subir a API em Development e executar: `curl -i /health/live`, `curl -i /health/ready`, `curl -i /metrics`, requisição com `X-Correlation-Id: teste-123` e login inválido conferindo `correlationId` no corpo e `[teste-123]` no log.
5. Encerrar a API, compilar com `--no-restore` e rodar a suíte completa.
6. Verificar que não restaram arquivos temporários, sondas ou `TODO` introduzidos pela spec.
**Expected Files:**
- `src/Ciclo.Api/Filters/ProblemDetailsCorrelationFilter.cs`
- `src/Ciclo.Api/Health/StorageHealthCheck.cs`
- `src/Ciclo.Infrastructure/Email/EmailDispatcher.cs`
**Validation:** Os três controles falharam antes da restauração e a suíte voltou a verde depois; smoke real confirmou os três endpoints e a correlação no log e no corpo de erro; build 0 erros / 0 avisos; suíte completa verde; `git status` mostra apenas as mudanças intencionais do spec.
**Status:** [x] done
