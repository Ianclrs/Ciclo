<!-- PIPELINE-CONTRACT: Task completion is determined by "**Status:** [x] done" at the end of each "### Task N" section.
     Checkboxes "- [ ]" under "**Validation:**" are human verification guides — the pipeline ignores them completely.
     To mark a task as done, add "**Status:** [x] done". To mark it as pending, use "**Status:** [ ] pending". -->
# Spec 140: comunicacao-proativa — Implementation Tasks

## Planned Scaffolds

- `src/Ciclo.Core/Notifications/NotificationTriggerEvent.cs`
- `src/Ciclo.Core/Entities/NotificationDelivery.cs`
- `src/Ciclo.Infrastructure/Notifications/NotificationTemplateCatalog.cs`
- `src/Ciclo.Infrastructure/Notifications/ProactiveNotificationService.cs`
- `src/Ciclo.Infrastructure/Notifications/ScheduledNotificationJob.cs`
- `src/Ciclo.Infrastructure/Email/SmtpEmailSender.cs`
- `tests/Ciclo.Api.Tests/ProactiveNotificationTests.cs`

## Task Order

Tasks MUST be executed sequentially in numerical order. Each task depends on the completion of all preceding tasks.

## Tasks

### Task 1: Contrato de gatilho, entidade de entrega e schema

**Objective:** Criar o contrato de evento e a entidade de entrega, registrar o `DbSet` e gerar a migração aditiva da tabela `NotificationDeliveries`.
**Requirements Covered:** FR-005, FR-006, NFR-001
**Design References:** Component `NotificationTriggerEvent`, Component `NotificationDelivery`, `Data Structures`, `File / Module Layout`.
**Actions:**
1. Criar `src/Ciclo.Core/Notifications/NotificationTriggerEvent.cs` com `public sealed record NotificationTriggerEvent(NotificationTriggerKind Kind, Guid TenantId, Guid StudentId, Guid ReferenceId, DateTime OccurredAt)` e o enum `NotificationTriggerKind` com exatamente os oito membros do design, na ordem declarada.
2. Criar `src/Ciclo.Core/Entities/NotificationDelivery.cs` com a classe `NotificationDelivery : ITenantScoped` e os enums `NotificationDeliveryChannel` (`InApp`, `Email`) e `NotificationDeliveryState` (`Pendente`, `Enviado`, `Falhou`, `Suprimida`), exatamente como no design.
3. Em `src/Ciclo.Infrastructure/Data/AppDbContext.cs`, declarar `public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();` e configurar na construção do modelo: `IdempotencyKey` com `HasMaxLength(200).IsRequired()`, `Erro` com `HasMaxLength(500)`, conversão para `int` dos três enums, índice único `IX_NotificationDeliveries_Idempotency` em `(TenantId, IdempotencyKey, RecipientUserId, Channel)` e índice `IX_NotificationDeliveries_Recipient` em `(RecipientUserId, CreatedAt)`.
4. Replicar a configuração de `ITenantScoped` que as demais entidades tenant-scoped do `AppDbContext` já usam, sem alterar nenhuma configuração existente.
5. Gerar a migração aditiva com `dotnet ef migrations add AddNotificationDeliveries --project src/Ciclo.Infrastructure --startup-project src/Ciclo.Api`; se a ferramenta `dotnet ef` não estiver instalada no ambiente, criar manualmente a classe de migração que adiciona apenas a tabela `NotificationDeliveries` e seus dois índices, com timestamp sequencial posterior à última migração existente.
6. Confirmar que nenhuma tabela, coluna ou índice existente foi alterado pela migração.
**Expected Files:**
- `src/Ciclo.Core/Notifications/NotificationTriggerEvent.cs`
- `src/Ciclo.Core/Entities/NotificationDelivery.cs`
- `src/Ciclo.Infrastructure/Data/AppDbContext.cs`
- `src/Ciclo.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
**Validation:**
- [ ] `dotnet build` conclui sem erros.
- [ ] A migração gerada contém somente `CreateTable` de `NotificationDeliveries` e `CreateIndex` dos dois índices declarados.
- [ ] `grep` encontra `IdempotencyKey` e `NotificationDeliveryState` em `src/Ciclo.Core/Entities/NotificationDelivery.cs`.
**Status:** [x] done

### Task 2: Catálogo de templates por tipo e locale

**Objective:** Entregar o catálogo estático que resolve assunto e corpo por `NotificationTriggerKind` com fallback para `pt-BR`.
**Requirements Covered:** FR-003
**Design References:** Component `NotificationTemplateCatalog`.
**Actions:**
1. Criar `src/Ciclo.Infrastructure/Notifications/NotificationTemplateCatalog.cs` com `public sealed record NotificationTemplate(string Assunto, string Corpo)`.
2. Declarar `public const string DefaultLocale = "pt-BR";` e o dicionário estático `IReadOnlyDictionary<NotificationTriggerKind, NotificationTemplate>` com uma entrada para cada um dos oito membros do enum, em português, com os placeholders `{aluno}` e `{referencia}` onde fizer sentido.
3. Implementar `public static NotificationTemplate Resolve(NotificationTriggerKind kind, string? locale = null)`: normalizar `locale` com `Trim()` e comparação case-insensitive, cair para `DefaultLocale` quando o valor for nulo, vazio ou desconhecido, e lançar `InvalidOperationException($"notification_template_missing:{kind}")` quando o `kind` não tiver entrada.
4. Não adicionar nenhum outro locale além de `pt-BR`.
**Expected Files:**
- `src/Ciclo.Infrastructure/Notifications/NotificationTemplateCatalog.cs`
**Validation:**
- [ ] `dotnet build` conclui sem erros.
- [ ] `grep` encontra o literal `pt-BR` e os oito membros do enum cobertos no dicionário.
- [ ] Nenhuma leitura de arquivo ou de banco acontece dentro de `Resolve`.
**Status:** [x] done

### Task 3: Dispatcher de notificações proativas

**Objective:** Implementar `IProactiveNotificationService` com o caminho de evento, o caminho agendado, a idempotência insert-first e o registro de entrega.
**Requirements Covered:** FR-001, FR-002, FR-005, FR-006, NFR-001, NFR-002
**Design References:** Component `ProactiveNotificationService`, `Data Flow`, `Data Structures`.
**Actions:**
1. Criar `src/Ciclo.Infrastructure/Notifications/ProactiveNotificationService.cs` com a interface `IProactiveNotificationService` (`HandleAsync`, `RunScheduledAsync` retornando `Task<int>`) e a classe `ProactiveNotificationService` com as constantes `JanelaVencimentoDias = 30`, `JanelaPrazoRematriculaDias = 7`, `LimitePorCiclo = 500`.
2. Implementar `ResolveRecipientsAsync`: para `RematriculaAberta` e `RematriculaPrazoFinal`, os responsáveis distintos dos alunos com `Enrollment.EnrollmentPeriodId == trigger.ReferenceId`; para os demais tipos, os responsáveis de `trigger.StudentId`; sempre via `StudentParent` ligado a `User` e filtrado por `TenantId`, projetando `record Recipient(Guid UserId, string Nome, string? Email)`.
3. Implementar `DispatchAsync` com a chave de idempotência do design (`<kind>:<referenceId>` para os cinco tipos de evento, com sufixo `:<yyyy-MM-dd>` para os três tipos agendados, usando `CultureInfo.InvariantCulture`), inserindo e salvando a linha `Pendente` antes de chamar o canal e tratando `DbUpdateException` como supressão de duplicata.
4. Implementar os canais: `InApp` insere `Notification` (`Titulo` = assunto, `Mensagem` = corpo, `Tipo` mapeado — `Matricula*`/`Rematricula*` → `NotificationType.Matricula`, `Documento*` → `NotificationType.DocumentoPendente`, `ReferenceId`) mais `UserNotification`; `Email` chama `IEmailSender.SendAsync` com o endereço do responsável. Sucesso grava `Enviado` e `SentAt`; exceção grava `Falhou` com a mensagem truncada em 500 caracteres e não interrompe o laço.
5. Implementar `RunScheduledAsync(DateTime nowUtc, CancellationToken ct)`: listar os tenants de `db.Tenants`, chamar `ITenantContext.SetTenant` por tenant, consultar documentos `Aprovado` com `DataValidade` na janela de 30 dias (`.OrderBy(d => d.DataValidade).Take(LimitePorCiclo)`) e períodos ativos na data de abertura e na janela de 7 dias do prazo, emitindo os três tipos agendados via `HandleAsync` e retornando a contagem de gatilhos emitidos.
6. Substituir os placeholders `{aluno}` e `{referencia}` antes de persistir, usando `Student.Nome` e o nome do período ou do tipo de documento; placeholder sem valor vira string vazia.
7. Não chamar `INotificationService.SendByStudentAsync` em nenhum ponto deste arquivo.
**Expected Files:**
- `src/Ciclo.Infrastructure/Notifications/ProactiveNotificationService.cs`
**Validation:**
- [ ] `dotnet build` conclui sem erros.
- [ ] `grep` encontra `IdempotencyKey` e `PeriodicTimer` ausente neste arquivo; `grep` encontra `SetTenant` e `JanelaVencimentoDias`.
- [ ] Nenhum `catch` vazio: toda exceção capturada grava log ou estado `Falhou`.
**Status:** [x] done

### Task 4: Host do agendador

**Objective:** Entregar o `BackgroundService` que executa o ciclo agendado a cada hora dentro do processo.
**Requirements Covered:** FR-002, NFR-002
**Design References:** Component `ScheduledNotificationJob`.
**Actions:**
1. Criar `src/Ciclo.Infrastructure/Notifications/ScheduledNotificationJob.cs` com `public sealed class ScheduledNotificationJob : BackgroundService`, `public static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);` e construtor que recebe `IServiceScopeFactory` e `ILogger<ScheduledNotificationJob>`.
2. Implementar `ExecuteAsync` com `PeriodicTimer`, criando um escopo por tick, resolvendo `IProactiveNotificationService`, chamando `RunScheduledAsync(DateTime.UtcNow, stoppingToken)` e registrando em log a contagem retornada.
3. Envolver o corpo do tick em `try/catch`: `OperationCanceledException` encerra o laço; qualquer outra exceção é logada em nível de erro e o laço segue para o próximo intervalo.
**Expected Files:**
- `src/Ciclo.Infrastructure/Notifications/ScheduledNotificationJob.cs`
**Validation:**
- [ ] `dotnet build` conclui sem erros.
- [ ] `grep` encontra `PeriodicTimer` e `ExecuteAsync` no arquivo.
- [ ] Nenhum `catch` vazio no arquivo.
**Status:** [x] done

### Task 5: Provedor SMTP fail-closed e composição

**Objective:** Entregar o `SmtpEmailSender` sobre o contrato existente e a seleção de provedor no `Program.cs`.
**Requirements Covered:** FR-004
**Design References:** Component `SmtpEmailSender`, Component `Registration (Program.cs)`.
**Actions:**
1. Criar `src/Ciclo.Infrastructure/Email/SmtpEmailSender.cs` implementando `IEmailSender` sobre `System.Net.Mail.SmtpClient`, com `public const string MissingConfigurationCode = "email_configuration_missing";`.
2. No construtor, ler `Email:Smtp:Host`, `Email:Smtp:Porta`, `Email:Smtp:Remetente`, `Email:Smtp:Usuario` e `Email:Smtp:Senha`; lançar `InvalidOperationException` cuja mensagem começa com `email_configuration_missing` e nomeia todas as chaves obrigatórias ausentes quando `Host`, `Porta` (inteiro positivo) ou `Remetente` faltarem.
3. Implementar `SendAsync(string toEmail, string toName, string subject, string body)` montando `MailMessage` e `SmtpClient` a partir da configuração, usando credenciais apenas quando `Usuario` estiver presente, e liberando os dois recursos ao final; deixar exceções de SMTP propagarem sem captura.
4. Em `src/Ciclo.Api/Program.cs`, substituir a linha `builder.Services.AddScoped<IEmailSender, ConsoleEmailSender>();` pelo bloco do design: registrar `IProactiveNotificationService`, `AddHostedService<ScheduledNotificationJob>()`, e escolher entre `SmtpEmailSender` (quando `Email:Provedor` for `Smtp`, instanciando-o na inicialização para falhar cedo) e `ConsoleEmailSender` (caso contrário).
5. Manter `builder.Services.AddCicloEmailDispatch()` e a ordem de registro do restante do arquivo inalteradas.
**Expected Files:**
- `src/Ciclo.Infrastructure/Email/SmtpEmailSender.cs`
- `src/Ciclo.Api/Program.cs`
**Validation:**
- [ ] `dotnet build` conclui sem erros.
- [ ] `grep` encontra `email_configuration_missing` em `SmtpEmailSender.cs` e `ScheduledNotificationJob` em `src/Ciclo.Api/Program.cs`.
- [ ] `grep` confirma que `src/Ciclo.Infrastructure/Email/EmailSender.cs` não foi alterado.
**Status:** [x] done

### Task 6: Gatilhos nos serviços de domínio

**Objective:** Injetar o dispatcher em `EnrollmentService` e `DocumentService` e disparar o evento após a transição de status já persistida.
**Requirements Covered:** FR-001
**Design References:** Component `Domain wiring in EnrollmentService and DocumentService`.
**Actions:**
1. Em `src/Ciclo.Infrastructure/Services/EnrollmentService.cs`, adicionar `IProactiveNotificationService proactive` e `ILogger<EnrollmentService> logger` ao construtor (sem remover nenhum parâmetro existente) e, após o `SaveChangesAsync` que persiste o novo status, disparar `MatriculaSubmetida` em `SubmitAsync`, `MatriculaAprovada` em `ApproveAsync` e `MatriculaRejeitada` em `RejectAsync`, com `TenantId`, `StudentId`, `ReferenceId = enrollment.Id` e `OccurredAt = DateTime.UtcNow`.
2. Em `src/Ciclo.Infrastructure/Services/DocumentService.cs`, adicionar `IProactiveNotificationService proactive` ao construtor e, em `VerifyAsync`, disparar `DocumentoVerificado` quando o status resultante for `DocumentStatus.Aprovado` e `DocumentoRejeitado` quando for `DocumentStatus.Rejeitado`, com `ReferenceId = document.Id`.
3. Envolver cada disparo em `try/catch` que loga em nível de erro e rethrowa apenas `OperationCanceledException`, para que a operação de domínio já commitada continue retornando sucesso.
4. Atualizar apenas os argumentos de construção nos testes existentes que instanciam esses serviços: `tests/Ciclo.Api.Tests/AccessPolicyTests.cs`, `tests/Ciclo.Api.Tests/AuditInstrumentationTests.cs`, `tests/Ciclo.Api.Tests/AuditOperationCoverageTests.cs` e `tests/Ciclo.Api.Tests/StudentAccessTests.cs`, passando um duplo de `IProactiveNotificationService` e `NullLogger` onde a nova assinatura exigir. Nenhuma asserção existente pode ser alterada.
**Expected Files:**
- `src/Ciclo.Infrastructure/Services/EnrollmentService.cs`
- `src/Ciclo.Infrastructure/Services/DocumentService.cs`
- `tests/Ciclo.Api.Tests/AccessPolicyTests.cs`
- `tests/Ciclo.Api.Tests/AuditInstrumentationTests.cs`
- `tests/Ciclo.Api.Tests/AuditOperationCoverageTests.cs`
- `tests/Ciclo.Api.Tests/StudentAccessTests.cs`
**Validation:**
- [ ] `dotnet build` conclui sem erros.
- [ ] `dotnet test` executa a suíte existente sem novas falhas em relação ao estado anterior à spec.
- [ ] `grep` encontra `IProactiveNotificationService` nos dois serviços e nenhum `catch` vazio.
**Status:** [x] done

### Task 7: Suíte de testes da spec

**Objective:** Provar deterministicamente todos os critérios marcados `project-validation` em `requirements.md`.
**Requirements Covered:** FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, NFR-001, NFR-002
**Design References:** `Testing Strategy`.
**Actions:**
1. Criar `tests/Ciclo.Api.Tests/ProactiveNotificationTests.cs` no mesmo padrão de `tests/Ciclo.Api.Tests/EmailDispatchTests.cs`: xUnit, `AppDbContext` InMemory, duplos de `IEmailSender` e de `ITenantContext` declarados no próprio arquivo.
2. Cobrir os cenários nomeados em `Testing Strategy`, um teste por cenário, incluindo as fronteiras de janela (30 dias inclusivo e 31 dias), tenant duplo, supressão de duplicata e falha de e-mail em um de três destinatários.
3. Cobrir o `SmtpEmailSender` com `ConfigurationBuilder` em memória: host ausente lança `InvalidOperationException` contendo `email_configuration_missing`; configuração completa constrói sem lançar.
4. Assertar estado de banco (`NotificationDelivery`, `UserNotification`) e a contagem de gatilhos devolvida por `RunScheduledAsync`; nenhum teste pode depender de relógio real para as janelas — sempre passar `nowUtc` explícito.
**Expected Files:**
- `tests/Ciclo.Api.Tests/ProactiveNotificationTests.cs`
**Validation:**
- [ ] `dotnet test tests/Ciclo.Api.Tests/Ciclo.Api.Tests.csproj --filter FullyQualifiedName~ProactiveNotificationTests` passa com todos os testes verdes.
- [ ] Cada critério `project-validation` de `requirements.md` tem um teste correspondente no arquivo.
- [ ] `dotnet test` completo não apresenta novas falhas.
**Status:** [x] done
