---
name: observabilidade-operacional
status: verified
determinism: D
references: [V13]
---

# Spec 130: Observabilidade Operacional — Requirements

## Determinism Classification

**Class:** D — Deterministic

Tudo o que esta spec entrega é observável por asserção direta: um endpoint de saúde responde um status conhecido para um estado de dependência conhecido; um identificador de correlação aparece no cabeçalho, no `HttpContext.Items`, no `ProblemDetails` e no evento de log; uma métrica expõe um valor que o teste controla (a fila recebe N mensagens e a profundidade é N). Não há conteúdo gerado por modelo, nem heurística de alerta avaliada em tempo de execução. As únicas grandezas variáveis são o identificador gerado quando o cliente não envia um e o instante da coleta — ambos tratados por função pura (`AuditCorrelation.Resolve`) ou por leitura direta do estado. Por isso a classe é D.

## Value Delivery

V13 — Observabilidade Operacional: “Saúde real do sistema: probes de liveness e readiness cobrindo banco, armazenamento e fila de e-mail; identificador de correlação propagado da requisição até o log e o ProblemDetails; métricas de requisição, erro e profundidade da fila; alertas para falha de dependência. Substitui os probes manuais atuais e torna incidentes rastreáveis a partir do relato do usuário.”

Entrega desta spec: liveness e readiness com verificações reais, correlação de ponta a ponta, métricas de requisição/erro/fila expostas para scrape, e os limiares de alerta documentados. Duas decisões de escopo são registradas e podem ser vetadas pelo dono: (a) o destino das métricas é **scrape Prometheus** (sem collector), porque o projeto é acadêmico e não há infraestrutura de observabilidade; (b) o canal de e-mail **não** entra no readiness — ele é inerte hoje (`ConsoleEmailSender`) e bloquearia todo deploy; a fila é exposta como métrica.

## Functional Requirements

### FR-001: Liveness independente de dependências

**What:** O sistema MUST expor `GET /health/live` respondendo `200` com corpo `{"status":"Healthy"}` sempre que o processo estiver capaz de atender requisições HTTP. O endpoint MUST NOT consultar banco, disco ou fila: uma indisponibilidade de dependência MUST NOT reiniciar o processo pelo orquestrador.
**Why:** liveness e readiness têm significados distintos. Se o liveness falhar quando o banco cai, o orquestrador reinicia um processo saudável em laço — e o incidente piora. A separação é o que permite reiniciar de verdade um processo travado sem mascarar a causa.
**Acceptance Criteria:**
- [ ] O endpoint de liveness é mapeado no pipeline. [verify: grep-present `health/live` `src/Ciclo.Api/Program.cs`]
- [ ] O liveness é mapeado com predicado que não executa nenhuma verificação registrada. [verify: grep-present `Predicate = _ => false` `src/Ciclo.Api/Program.cs`]
**Failure modes:**
- Requisição durante a inicialização: o pipeline HTTP ainda não está montado; a conexão é recusada (comportamento esperado, não é falha do endpoint).
- Banco indisponível: o liveness continua respondendo `200`, porque não resolve `AppDbContext`.

### FR-002: Readiness com banco e armazenamento

**What:** O sistema MUST expor `GET /health/ready` que executa duas verificações: (a) `db` — `AppDbContext.Database.CanConnectAsync()` respondendo em até 2 segundos; (b) `storage` — gravação e remoção de um arquivo-sonda de 1 byte dentro de `FileStorage:RootPath`. A resposta MUST ser `200` quando ambas passam e `503` quando qualquer uma falha, com corpo `{"status":"Healthy|Unhealthy","checks":{"db":"Healthy|Unhealthy","storage":"Healthy|Unhealthy"}}` e `Content-Type: application/json`. O resultado de cada verificação MUST ser registrado em log de nível `Warning` quando falha, com a chave `check`.
**Why:** hoje só existe `/Health/db`; disco cheio ou sem permissão derruba upload e download sem sinal. A sonda de armazenamento detecta exatamente isso, e a de banco cobre o caso mais comum de indisponibilidade.
**Acceptance Criteria:**
- [ ] O endpoint de readiness é mapeado no pipeline. [verify: grep-present `health/ready` `src/Ciclo.Api/Program.cs`]
- [ ] A verificação de banco usa a conexão real do contexto, com orçamento de tempo. [verify: grep-present `CanConnectAsync` `src/Ciclo.Api/Health/DatabaseHealthCheck.cs`]
- [ ] A verificação de armazenamento escreve e remove um arquivo-sonda. [verify: grep-present `health-probe` `src/Ciclo.Api/Health/StorageHealthCheck.cs`]
- [ ] O corpo de resposta é escrito por um writer dedicado que serializa o relatório e o status agregado. [verify: file-exists `src/Ciclo.Api/Health/HealthResponseWriter.cs`]
**Failure modes:**
- Banco lento: a verificação é cancelada em 2 segundos e o check é reportado `Unhealthy`; o endpoint responde 503 sem esperar o timeout do provider.
- `RootPath` inexistente: o diretório é criado antes da sonda; se a criação falhar (permissão), o check falha.
- Arquivo-sonda remanescente de execução anterior: é sobrescrito e removido; nunca é lido como dado de aplicação.

### FR-003: Correlação resolvida uma única vez na borda

**What:** Todo request MUST receber um identificador de correlação resolvido por `AuditCorrelation.Resolve(cabeçalho, TraceIdentifier)`: usa `X-Correlation-Id` quando presente e válido (até 64 caracteres, apenas `[A-Za-z0-9._:-]`), caso contrário usa o `TraceIdentifier`. O valor MUST ser gravado em `HttpContext.Items[AuditCorrelation.ItemsKey]`, ecoado no cabeçalho de resposta `X-Correlation-Id` e reutilizado por todos os consumidores — auditoria, log e `ProblemDetails` MUST NOT resolver um valor próprio.
**Why:** hoje a resolução existe só no `AuditContextMiddleware` e o valor não chega ao log nem ao corpo de erro. Resolver em um ponto único evita três identificadores diferentes para o mesmo incidente — que é o que torna o relato do usuário rastreável.
**Acceptance Criteria:**
- [ ] A chave de item é declarada no dono da correlação. [verify: grep-present `ItemsKey` `src/Ciclo.Core/Auditing/AuditCorrelation.cs`]
- [ ] O middleware de borda grava o valor em `HttpContext.Items` e no cabeçalho de resposta. [verify: grep-present `AuditCorrelation.ItemsKey` `src/Ciclo.Api/Middleware/CorrelationMiddleware.cs`]
- [ ] O middleware de auditoria reutiliza o valor em vez de resolver outro. [verify: grep-present `AuditCorrelation.ItemsKey` `src/Ciclo.Api/Middleware/CorrelationMiddleware.cs`]
- [ ] O comportamento de resolução por request é coberto por teste automatizado. [verify: file-exists `tests/Ciclo.Api.Tests/CorrelationTests.cs`]
**Failure modes:**
- Cabeçalho com valor inválido ou com 200 caracteres: é descartado e o `TraceIdentifier` é usado; a resposta informa o valor efetivo.
- Requisição fora do pipeline (teste unitário direto do middleware de auditoria): o consumidor que não encontra o item usa `AuditCorrelation.Resolve` como fallback, sem quebrar.

### FR-004: Correlação no log

**What:** O log MUST carregar o identificador de correlação em toda mensagem emitida durante o request. O `CorrelationMiddleware` MUST empurrar `LogContext.PushProperty("CorrelationId", valor)` e o `UseSerilogRequestLogging` MUST enriquecer o evento de conclusão com o valor lido de `HttpContext.Items`. O template do sink de console MUST exibir `{CorrelationId}`.
**Why:** sem isso o operador tem o identificador na resposta ao usuário e não consegue encontrar a linha correspondente no log — que é justamente o uso descrito em V13.
**Acceptance Criteria:**
- [ ] O middleware empurra a propriedade de log. [verify: grep-present `PushProperty` `src/Ciclo.Api/Middleware/CorrelationMiddleware.cs`]
- [ ] O request logging é enriquecido com a correlação. [verify: grep-present `EnrichDiagnosticContext` `src/Ciclo.Api/Program.cs`]
- [ ] O template do console exibe a correlação. [verify: grep-present `{CorrelationId}` `src/Ciclo.Api/Program.cs`]
**Failure modes:**
- Log emitido fora de um request (startup, job em background): a propriedade não existe e o campo aparece vazio; nenhuma exceção é lançada e o template continua válido.

### FR-005: Correlação no ProblemDetails

**What:** Toda resposta de erro no formato `application/problem+json` MUST conter a extensão `correlationId` com o valor do request. Isso MUST valer para: respostas produzidas por controllers via `ProblemResponse.FromCode`, respostas escritas por middleware via `ProblemResponse.WriteAsync` (incluindo `unauthorized` e `forbidden` do framework) e respostas do `ExceptionMiddleware`. A implementação MUST ser um filtro global que inspeciona `ObjectResult` cujo valor é `ProblemDetails`, mais a escrita direta nos middlewares — os 43 pontos de chamada de `FromCode` MUST NOT ser alterados.
**Why:** o usuário relata “deu erro” e envia um print; sem o identificador no corpo ele precisa reproduzir o erro para que o suporte encontre o log. Com a extensão, o print basta.
**Acceptance Criteria:**
- [ ] O filtro global de correlação em ProblemDetails existe. [verify: file-exists `src/Ciclo.Api/Filters/ProblemDetailsCorrelationFilter.cs`]
- [ ] O filtro adiciona a extensão ao corpo de erro. [verify: grep-present `correlationId` `src/Ciclo.Api/Filters/ProblemDetailsCorrelationFilter.cs`]
- [ ] A escrita direta de erro inclui a correlação. [verify: grep-present `CorrelationExtension` `src/Ciclo.Api/Middleware/ProblemResponse.cs`]
- [ ] O filtro é registrado globalmente no MVC. [verify: grep-present `ProblemDetailsCorrelationFilter` `src/Ciclo.Api/Program.cs`]
- [ ] O filtro é coberto por teste com e sem item de correlação. [verify: file-exists `tests/Ciclo.Api.Tests/CorrelationTests.cs`]
**Failure modes:**
- `ProblemDetails` produzido sem `Items` (teste isolado): a extensão é omitida em vez de gravar valor vazio.
- Resposta de erro que não é `ProblemDetails` (ex.: 404 de rota inexistente): não é alterada; o cabeçalho de correlação continua presente.

### FR-006: Métricas HTTP de requisição e erro

**What:** O sistema MUST expor `GET /metrics` no formato de texto do Prometheus, gerado por `prometheus-net.AspNetCore`, com as métricas padrão de requisição (`http_requests_received_total` com rótulos de método, rota e código) e de duração (`http_request_duration_seconds`). O endpoint MUST ficar fora de `/api`, MUST ser anônimo e MUST NOT expor rótulo com tenant, usuário, e-mail ou qualquer identificador de pessoa.
**Why:** é a base de V13 para “métricas de requisição, erro e profundidade da fila”. O modelo de scrape foi escolhido porque não exige collector: qualquer host acadêmico consegue raspar `/metrics`.
**Acceptance Criteria:**
- [ ] O middleware de métricas HTTP é registrado no pipeline. [verify: grep-present `UseHttpMetrics` `src/Ciclo.Api/Program.cs`]
- [ ] O endpoint de métricas é mapeado no nível da aplicação. [verify: grep-present `MapMetrics` `src/Ciclo.Api/Program.cs`]
- [ ] Os endpoints de observabilidade são cobertos por teste de integração HTTP. [verify: file-exists `tests/Ciclo.Api.Tests/ObservabilityEndpointTests.cs`]
**Failure modes:**
- Scrape durante indisponibilidade do banco: `/metrics` continua respondendo, porque não consulta dependências.
- Acesso anônimo: retorna 200; não há dado de negócio no corpo.

### FR-007: Profundidade da fila de e-mail

**What:** O sistema MUST expor a métrica `ciclo_email_queue_depth` (gauge) com o número de mensagens aguardando envio e `ciclo_email_queue_capacity` (gauge) com a capacidade configurada do canal. A leitura MUST vir de uma porta `IEmailQueueStatus` implementada por `ChannelEmailDispatcher`, consultada no momento da coleta — sem contador paralelo que possa divergir. A porta MUST expor `int Depth` e `int Capacity`.
**Why:** a fila é limitada a 1000 e o enfileiramento usa `TryWrite`; quando cheia, a mensagem é descartada com log de aviso. Sem a profundidade, uma fila saturada se manifesta como e-mails que não chegam, sem causa visível.
**Acceptance Criteria:**
- [ ] A porta de status da fila existe no núcleo. [verify: file-exists `src/Ciclo.Core/Interfaces/IEmailQueueStatus.cs`]
- [ ] O dispatcher implementa a porta de status. [verify: grep-present `IEmailQueueStatus` `src/Ciclo.Infrastructure/Email/EmailDispatcher.cs`]
- [ ] A métrica de profundidade é registrada no registro do Prometheus. [verify: grep-present `ciclo_email_queue_depth` `src/Ciclo.Infrastructure/Email/EmailQueueMetrics.cs`]
- [ ] A coleta lê o estado atual do canal. [verify: grep-present `Reader.Count` `src/Ciclo.Infrastructure/Email/EmailDispatcher.cs`]
- [ ] A profundidade e a capacidade são cobertas por teste com fila construída explicitamente. [verify: file-exists `tests/Ciclo.Api.Tests/EmailQueueStatusTests.cs`]
**Failure modes:**
- Canal vazio: profundidade 0.
- Canal cheio: profundidade igual à capacidade e a mensagem é descartada com `Warning` (comportamento existente, agora observável).
- Coleta concorrente com escrita: o valor é um instantâneo; não há garantia de linearidade e isso é aceitável para um gauge.

### FR-008: Limiares de alerta documentados

**What:** O projeto MUST documentar, em `README.md`, os limiares operacionais que justificam alerta: readiness `Unhealthy` por mais de 2 minutos; taxa de `5xx` acima de 1% em 5 minutos; `p95` de latência acima de 1 segundo em 5 minutos; profundidade da fila acima de 80% da capacidade por 5 minutos. Cada limiar MUST indicar a métrica ou endpoint de origem e a ação recomendada.
**Why:** sem destino de alerta definido no projeto acadêmico, o que pode ser entregue é a regra objetiva — e ela evita que cada operador invente um critério.
**Acceptance Criteria:**
- [ ] O README contém a seção de limiares. [verify: grep-present `Limiares de alerta` `README.md`]
- [ ] Os limiares citam a métrica de requisições como origem do sinal de erro. [verify: grep-present `http_requests_received_total` `README.md`]
- [ ] Os limiares citam a profundidade da fila como origem do sinal de saturação. [verify: grep-present `ciclo_email_queue_depth` `README.md`]
**Failure modes:**
- Ausência de coletor: os limiares continuam válidos como procedimento manual; nenhum código depende deles.

### FR-009: Nenhum dado sensível em métricas e logs

**What:** Métricas e logs produzidos por esta spec MUST NOT conter e-mail, CPF, nome de aluno, token, senha ou corpo de requisição. Os rótulos de métrica MUST ser limitados a método HTTP, rota normalizada e código de status. O log de correlação MUST registrar apenas o identificador.
**Why:** observabilidade não pode virar vazamento. O projeto já trata dados pessoais de menores; um rótulo com e-mail seria um incidente de LGPD criado pela própria instrumentação.
**Acceptance Criteria:**
- [ ] A instrumentação do pipeline não adiciona rótulo de usuário ou tenant. [verify: grep-absent `WithLabels` `src/Ciclo.Api/Program.cs`]
- [ ] O template de log não inclui corpo de requisição. [verify: grep-absent `RequestBody` `src/Ciclo.Api/Program.cs`]
- [ ] O gauge da fila não adiciona rótulo próprio. [verify: grep-absent `WithLabels` `src/Ciclo.Infrastructure/Email/EmailQueueMetrics.cs`]
**Failure modes:**
- Rota com identificador no caminho: o rótulo usa o padrão da rota, nunca o valor — é o comportamento do `UseHttpMetrics` e é verificado no teste de normalização.
- Exceção registrada em log: apenas tipo, mensagem e correlação; nenhum dado pessoal é anexado pelo código desta spec.

### FR-010: Compatibilidade com os endpoints de saúde existentes

**What:** `GET /Health` e `GET /Health/db` MUST continuar respondendo como hoje (`200` com `{"status":"Healthy"}`), sem mudança de contrato. Os novos endpoints são adicionais e o controlador existente não é alterado.
**Why:** o host já usa `/health` como probe; quebrar esse caminho durante a migração derrubaria o deploy antes de o novo probe existir.
**Acceptance Criteria:**
- [ ] O controlador de saúde existente permanece. [verify: file-exists `src/Ciclo.Api/Controllers/HealthController.cs`]
- [ ] A ação de banco do controlador existente permanece. [verify: grep-present `GetDb` `src/Ciclo.Api/Controllers/HealthController.cs`]
- [ ] O contrato antigo continua coberto pelo teste já existente do controlador. [verify: file-exists `tests/Ciclo.Api.Tests/HealthControllerTests.cs`]
**Failure modes:**
- Cliente antigo apontando para `/Health`: continua funcionando; a recomendação de migrar para `/health/live` e `/health/ready` fica no README.

## Non-Functional Requirements

### NFR-001: Readiness com orçamento de tempo

**What:** O endpoint de readiness MUST responder em até 3 segundos no pior caso: cada verificação tem timeout de 2 segundos e as duas rodam em paralelo pelo executor de health checks do framework.
**Acceptance Criteria:**
- [ ] A verificação de banco declara orçamento de tempo próprio. [verify: grep-present `FromSeconds` `src/Ciclo.Api/Health/DatabaseHealthCheck.cs`]
- [ ] As verificações são registradas com o nome e a tag usados pelo predicado de readiness. [verify: grep-present `AddCheck` `src/Ciclo.Api/Program.cs`]
- [ ] O orçamento é coberto por teste automatizado dos health checks. [verify: file-exists `tests/Ciclo.Api.Tests/HealthCheckTests.cs`]

### NFR-002: Overhead de instrumentação

**What:** O middleware de métricas MUST NOT bufferizar corpo de requisição ou resposta, e a coleta de métricas MUST ser O(1) por scrape.
**Acceptance Criteria:**
- [ ] O middleware de métricas do pacote é registrado sem configuração de captura de corpo. [verify: grep-present `UseHttpMetrics` `src/Ciclo.Api/Program.cs`]
- [ ] O build e a suíte do projeto permanecem verdes com a instrumentação. [verify: project-validation]

### NFR-003: Uma única dependência nova

**What:** A implementação MUST adicionar no máximo um pacote (`prometheus-net.AspNetCore`). Verificações de saúde MUST usar `Microsoft.Extensions.Diagnostics.HealthChecks`, já incluso no framework.
**Acceptance Criteria:**
- [ ] O pacote de métricas é declarado no projeto da API. [verify: grep-present `prometheus-net.AspNetCore` `src/Ciclo.Api/Ciclo.Api.csproj`]
- [ ] Nenhum pacote de telemetria alternativo é adicionado. [verify: grep-absent `OpenTelemetry` `src/Ciclo.Api/Ciclo.Api.csproj`]

### NFR-004: Testes determinísticos

**What:** Os testes MUST NOT usar `Thread.Sleep` nem depender de relógio real para asserção; estados de fila e de armazenamento são construídos explicitamente.
**Acceptance Criteria:**
- [ ] Nenhum teste da suíte usa espera por tempo real. [verify: grep-absent `Thread.Sleep` `tests/Ciclo.Api.Tests/`]
- [ ] O teste da fila constrói o dispatcher e enfileira sem depender de worker. [verify: file-exists `tests/Ciclo.Api.Tests/EmailQueueStatusTests.cs`]

## Constraints

- O endpoint `/metrics` fica **fora** de `/api`, como `/health`, e é mapeado no nível da aplicação. [verify: grep-present `MapMetrics` `src/Ciclo.Api/Program.cs`]
- Nenhuma alteração nos contratos de erro existentes além da extensão aditiva `correlationId`; `FromCode` mantém a assinatura atual. [verify: grep-present `FromCode` `src/Ciclo.Api/Middleware/ProblemResponse.cs`]
- O formato de `X-Correlation-Id` já documentado não muda: o nome do cabeçalho continua em `AuditCorrelation.HeaderName`. [verify: grep-present `HeaderName` `src/Ciclo.Core/Auditing/AuditCorrelation.cs`]
- O formato de erro continua definido em um único arquivo, `ProblemResponse`. [verify: file-exists `src/Ciclo.Api/Middleware/ProblemResponse.cs`]
- A sonda de armazenamento lê a configuração existente de armazenamento, sem chave de configuração nova. [verify: grep-present `FileStorageOptions` `src/Ciclo.Api/Health/StorageHealthCheck.cs`]
- Nenhuma migração de banco é criada por esta spec. [verify: grep-absent `Migration` `src/Ciclo.Api/Health/`]
- Full project validation passes. [verify: project-validation]

## Edge Cases & Error States

- Banco indisponível no startup e readiness chamado antes da primeira conexão: `503`, sem exceção não tratada; o health check converte a exceção em `Unhealthy`.
- `RootPath` em volume somente-leitura: o check `storage` falha, o log registra a chave `storage` e o endpoint responde `503`.
- Cabeçalho de correlação com 200 caracteres: é descartado e o `TraceIdentifier` é usado.
- Cabeçalho de correlação ausente: o valor gerado é o `TraceIdentifier` e ele aparece nos três destinos (cabeçalho, log e `ProblemDetails`).
- Duas requisições simultâneas: cada uma tem seu identificador; não há estado compartilhado mutável entre requests.
- `/metrics` chamado por cliente não autenticado: `200`, sem dado de negócio no corpo.
- Processo encerrando durante um scrape: a conexão é encerrada e o coletor registra falha de scrape (comportamento esperado do coletor).
- Fila cheia: a mensagem é descartada com `Warning` (comportamento existente) e `ciclo_email_queue_depth` atinge a capacidade.
- Exceção não tratada em um controller: o `ExceptionMiddleware` responde `ProblemDetails` já com `correlationId`, porque a escrita direta lê o item do request.

## Anti-Scope

- Nenhum exportador OTLP, collector ou backend de telemetria é criado; a entrega é scrape em texto do Prometheus. [verify: file-absent `src/Ciclo.Api/Observability/OtlpExporterSetup.cs`]
- Nenhum código de entrega de alerta (Slack, e-mail, Alertmanager) é criado; os limiares ficam no README. [verify: file-absent `src/Ciclo.Api/Alerting/AlertRules.cs`]
- O canal de e-mail não entra no readiness: nenhum health check de e-mail é criado. [verify: grep-absent `EmailHealthCheck` `src/Ciclo.Api/`]
- Nenhum controller resolve ou escreve o identificador de correlação: os pontos de chamada de `FromCode` permanecem intactos. [verify: grep-absent `ItemsKey` `src/Ciclo.Api/Controllers/`]
- Nenhum contador paralelo da fila é mantido em memória; a leitura acontece na coleta. [verify: grep-absent `Interlocked` `src/Ciclo.Infrastructure/Email/EmailQueueMetrics.cs`]

## Dependencies

- **Spec 10 (bootstrap):** host, Serilog, `Program.cs` e `/Health` existentes.
- **Spec 110 (auditoria):** `AuditCorrelation.Resolve`, `AuditActor` e `AuditContextMiddleware`, que passam a consumir o item resolvido na borda.
- **Spec 80 (notificações):** `ChannelEmailDispatcher` e `IEmailDispatcher`, que passam a implementar `IEmailQueueStatus`.
- **Pacote externo:** `prometheus-net.AspNetCore` 8.2.1, a única dependência nova.

## Out of Scope

- Dashboards, coletor Prometheus e provisionamento de infraestrutura de observabilidade.
- Traces distribuídos (OpenTelemetry) e amostragem.
- Integração de alerta com qualquer destino externo.
- Alteração de schema, migração de dados ou mudança de contrato dos endpoints existentes.

## Test Scope

- `tests/Ciclo.Api.Tests/CorrelationTests.cs` — resolução da correlação (cabeçalho válido, ausente e com 200 caracteres) e filtro de `ProblemDetails` com e sem item.
- `tests/Ciclo.Api.Tests/EmailQueueStatusTests.cs` — `Depth` e `Capacity` do dispatcher com fila construída explicitamente, sem worker.
- `tests/Ciclo.Api.Tests/HealthCheckTests.cs` — storage saudável (diretório temporário), storage inválido e banco no provider em memória.
- `tests/Ciclo.Api.Tests/ObservabilityEndpointTests.cs` — `/health/live`, `/health/ready`, `/metrics`, `/Health` e `/Health/db` via `ApiTestHost`.

## Validation

| Verificação | Como |
|---|---|
| Liveness | `curl -i localhost:5264/health/live` → 200 |
| Readiness | `curl -i localhost:5264/health/ready` → 200; com `FileStorage:RootPath` inválido → 503 |
| Correlação | `curl -H "X-Correlation-Id: teste-123" localhost:5264/health/live` e inspeção de log e de corpo de erro |
| Métricas | `curl localhost:5264/metrics` → `http_requests_received_total` e `ciclo_email_queue_depth` |
| Regressão | suíte completa + `/Health` e `/Health/db` |
