# Pending Items

> Pendências abertas fora do roadmap. Formato: ID, estado, dono, impacto, evidência e ação recomendada.
> Este arquivo é a memória durável de bloqueios que não pertencem a uma spec específica.

---

## P-001 — Runtime: guard `task-ledger-divergence` é insatisfazível (bloqueia a spec 110)

**Estado:** aberto
**Descoberto:** 2026-10-27
**Dono:** runtime do DsCode (defeito de ferramenta, não do projeto)
**Bloqueia:** `/spec-implement 110` e, por consequência, qualquer spec com mais de uma task

### Comportamento observado

O guard `phase 2A` compara um hash calculado no momento da execução com `taskSourceHash`, gravado apenas quando o estado do ledger é inicializado. Se os dois divergirem, a execução falha com `task-ledger-divergence` e nenhuma task avança.

Medições na spec 110, com o mesmo `task.md` (byte-idêntico ao HEAD, CRLF, 338 linhas, sem BOM):

| Momento | Conteúdo do `task.md` | Ledger | Hash calculado |
|---|---|---|---|
| Inicialização (19:52:39Z) | 18 tasks `pending` | 0 done | `102b8446…` |
| Após a execução `79233c07` | 3 tasks `done` | 3 done | `7f276a3a…` |
| Após a reconciliação (20:03:16Z) | 18 tasks `pending` | 1 done | `22ab361d…` |

Conteúdo idêntico ao da inicialização produziu hash **diferente**. Logo, o hash não é função pura do `task.md`: ele incorpora o estado do ledger. Como `taskSourceHash` nunca é atualizado depois da inicialização, o guard **só pode passar na primeira execução de um estado**.

### Consequência medida

O log da execução `79233c07` mostra o ciclo: task 2 conclui → `execution-finished` com `task-completed-next-task-pending` → o runtime inicia sozinho a execução seguinte → guard → `failed`. Com 18 tasks, o fluxo não fecha.

### Tentativas já feitas (todas autorizadas pelo dono)

1. Corrigir o `requirements.md` (termo vago em FR-007) → gate de documentos passou a `ok`.
2. Corrigir o status legado `created` → `verified` no roadmap → pré-condição liberada.
3. Restaurar o `task.md` ao conteúdo pré-execução e reabrir as tasks 2 e 18 no ledger, preservando a task 1 → hash passou de `7f276a3a…` para `22ab361d…`, sem casar com `102b8446…`.

Repetir a reconciliação apenas produz um novo hash. Não existe edição de arquivo que satisfaça um valor que o engine não recalcula de forma determinística.

### Ação recomendada

Corrigir no runtime: recalcular e persistir `taskSourceHash` ao fim de cada projeção de status, ou comparar uma representação canônica do **conjunto de tasks** (id, título, ordem) em vez de um hash sensível ao estado. Depois disso, retomar `/spec-implement 110`.

### O que NÃO fazer

- Não apagar `state.json`, lock ou artefatos de execução.
- Não editar o `task.md` para "caber" em um hash.
- Não gravar `taskSourceHash` à mão: silencia o guard sem provar consistência.

---

## P-002 — Runtime: selo de task aceita arquivo inalterado (conclusões falsas)

**Estado:** aberto
**Descoberto:** 2026-10-27
**Dono:** runtime do DsCode
**Risco:** uma spec pode chegar a `implemented` com deliverables inexistentes — o mesmo padrão que produziu as specs 10–100 "auditadas" com regras ausentes no código

### Evidência

Os `sealedFiles` do ledger são `sha256` do conteúdo **normalizado para LF** do arquivo como está — não um delta. Comprovado nos três casos:

| Task | Declarado | Realidade |
|---|---|---|
| 1 | 8 arquivos criados | ✅ real (arquivos existem, build verde) |
| 2 | `AppDbContext.cs` alterado | ⚠️ **+2 linhas** (dois `using`). Sem `DbSet<AuditEvent>`, sem configuração em `OnModelCreating`, sem `GuardAuditImmutability` |
| 18 | `adr.md` alterado, `dotnet test` passed | ❌ `adr.md` **intocado desde o HEAD**; `ADR-004` inexistente; "concluída" às `19:52:39.637`, **51 ms antes** de a task 1 começar (`19:52:39.688`), sem `startedAt` |

### Ação recomendada

O verificador de task deveria exigir evidência de mudança nos `expectedFiles` (hash anterior ≠ posterior) e executar os `validationCommands` declarados, em vez de selar o estado atual do arquivo.

---

## P-003 — Spec 110 implementada manualmente; certificação pendente (P-001)

**Estado:** implementação concluída, certificação bloqueada
**Dono:** projeto + runtime

- **Roadmap:** spec 110 permanece em `verified`. A transição para `implemented`/`audited` é do runtime (`spec-implement`/`spec-test`) e não foi fabricada.
- **Implementado (tasks 1 a 18) e verificado:**
  - `DbSet<AuditEvent>`, configuração em `OnModelCreating` (jsonb, 3 índices, FK Restrict) e `GuardAuditImmutability` nas duas `SaveChangesAsync`;
  - `AuditActor` (scoped), `AuditTrail` (escrita transacional, sem `SaveChanges` e sem `catch`), `AuditSettings` (default 365, faixa 90–3650);
  - migration `20261027201113_AddAuditEvents` com função `audit_events_append_only_guard` e gatilhos `audit_events_no_update`/`audit_events_no_delete`;
  - `AuditContextMiddleware` (ator, IP do socket, correlação `X-Correlation-Id`) registrado após o `TenantMiddleware`;
  - instrumentação de matrícula (approve/reject/cancel), documento (verify) e sessão (revoke, incluindo reuso detectado);
  - `AuditQueryService` (normalização de intervalo, paginação, projeção única), `AuditCsvWriter` (RFC 4180 + BOM), `AuditController` Admin-only e `AuditRetentionPurger`/`AuditRetentionService` (expurgo 24h);
  - ADR-004 registrado em `management/adr.md`.
- **Evidência:** build 0 erros/0 avisos (MSBuild do VS); suíte **144/144**; migration aplicada em schema limpo no PostgreSQL 18 real com os 3 índices e os gatilhos comprovados (UPDATE recente e DELETE recente falham; DELETE com 100 dias funciona).
- **Cobertura de testes:** 25 testes novos — 11 do núcleo (escrita, imutabilidade nas duas camadas, correlação, CSV, consulta, exportação), 9 de instrumentação (aprovar/rejeitar/cancelar matrícula, aprovar/rejeitar documento, revogar sessão e três controles negativos) e 5 de persistência/desempenho (isolamento entre tenants, expurgo por retenção, limite de 10.000 linhas na exportação, cada filtro da consulta e orçamento de tempo do middleware). Suíte total **144/144**.
- **NFR-001 medido (2026-10-27):** middleware **0,018 ms** por requisição (500 iterações, alvo ≤1 ms). No PostgreSQL, com **10.000 eventos** e o schema real: página de 50 registros **0,105 ms** usando `Index Scan Backward` no índice `IX_AuditEvents_TenantId_OccurredAt`; contagem do tenant **1,788 ms** (Seq Scan — 50% das linhas eram do mesmo tenant; com seletividade real o índice é usado). Ambos dentro do alvo de 5 ms.
- **Defeito de atomicidade encontrado e corrigido (2026-10-27):** o design mandava registrar a auditoria **depois** do bloco de notificação de rejeição, mas o `NotificationService` real executa `SaveChangesAsync` internamente — na ordem prescrita, o status `Rejeitado` seria confirmado antes do evento, e uma falha na trilha deixaria a rejeição sem auditoria (violação de FR-003). `AuditTrail.Record` passou a ser chamado **antes** do envio. Regressão coberta por `VerifyRejected_WhenTheTrailFails_DoesNotPersistTheStatusChange`, cujo controle negativo foi executado: com a ordem antiga o teste falha; com a correção, passa.
- **Marcadores no `task.md`:** a última tentativa do runtime marcou `done` nas tasks 1, 2 e 18 — as três são realmente entregues. As demais 15 estão entregues no código mas permanecem `pending` no arquivo, porque só o `spec-implement` escreve essa projeção.
- **Desvio declarado:** `RevokeTokenAsync` passou a resolver o tenant pelo dono do refresh token quando o contexto não está resolvido (o logout é anônimo). Sem isso a trilha lançaria `TenantNotResolvedException` e quebraria o logout.
- **Banco de dev recriado (2026-10-27, autorizado pelo dono):** o role `ciclo` não tem `CREATEDB`, então o reset foi por `DROP SCHEMA public CASCADE; CREATE SCHEMA public` — equivalente a banco limpo. Migrations aplicadas na inicialização (**19 tabelas**: 18 de domínio + histórico) e seed completo (2 tenants, 4 usuários, 6 alunos, 6 matrículas, 5 documentos).
- **Validação de fumaça com a API no ar (`Development`, porta 5264):**
  - `GET /health` → 200 com `X-Correlation-Id` gerado na borda;
  - `GET /api/audit-events` sem token → **401** (deny-by-default preservado);
  - login Admin → 200; `GET /api/audit-events` com token → 200;
  - aprovar documento com `X-Correlation-Id: smoke-correlation-0001` → evento persistido `document.verified`, alvo `Document`, ator `Admin`, IP `::1`, correlação preservada, `BeforeJson` `{"status":"Pendente"}` e `AfterJson` `{"status":"Aprovado"}` em **jsonb**;
  - `UPDATE` e `DELETE` diretos no banco real → bloqueados pelos gatilhos (`audit_events_are_append_only` / `audit_events_within_the_retention_floor_cannot_be_deleted`);
  - `GET /api/audit-events/export` → 200, CSV com **BOM UTF-8**, cabeçalho fixo e terminador CRLF.

---

## P-004 — Documentação do roadmap usa o fluxo antigo

**Estado:** aberto
**Dono:** projeto
**Impacto:** baixo (não bloqueia), mas induz a erro

`management/roadmap.md` ainda descreve `created`, `/spec-verify` e `/spec-audit` — status e comandos que o pipeline atual não reconhece, e define `audited` como "Feature is live", o que não é verdade (o próprio plano de correções registra specs auditadas com regras ausentes). Corrigir junto com o item **D2** de `management/cross-session/plano-correcoes.md`.

---

## Reporte pronto — defeitos do runtime (P-001 e P-002)

> **Versão pronta para colar como issue:** `management/cross-session/runtime-issue-task-ledger.md`

**Ambiente:** Windows 10 (build 19045) · .NET SDK 10.0.302 · PostgreSQL 18.3 · projeto `Ciclo` · spec 110 com 18 tasks · descoberto em 2026-10-27.

---

### Defeito 1 — `task-ledger-divergence` bloqueia qualquer spec com mais de uma task

**Severidade:** bloqueador (impede a certificação de qualquer spec multi-task)

**Passos para reproduzir**
1. Ter uma spec em `verified` com 2 ou mais tasks e o ledger ainda não inicializado.
2. Rodar `/spec-implement <spec>`.
3. Observar que o runtime executa a task 1, executa a task 2, encerra a execução (`task-completed-next-task-pending`) e **reinicia sozinho** para a task 3.
4. Na reinicialização: `[spec-implement task-controller:ledger-guard] ... action=block zeroDrift=true ... kind=UNVERIFIABLE` → execução `failed`; a task 3 nunca roda.

**Observado** — três medições, todas com o `task.md` byte-idêntico ao HEAD (CRLF, 338 linhas, sem BOM):

| Momento | Conteúdo de `task.md` | Ledger | Hash calculado |
|---|---|---|---|
| Inicialização (19:52:39Z) | 18 tasks `pending` | 0 done | `102b8446…` |
| Após a execução `79233c07` | 3 tasks `done` | 3 done | `7f276a3a…` |
| Após reconciliação (20:03:16Z) | 18 tasks `pending` | 1 done | `22ab361d…` |

Conteúdo igual ao da inicialização produziu hash **diferente** → o hash não é função apenas do `task.md`.

**Esperado:** a execução seguinte continuar a partir da task pendente depois de o próprio engine gravar os marcadores de status.

**Causa confirmada no bundle do runtime** (extraído do binário; módulos `src/sdd/sdd-task-parser` e `src/common/sdd-task-ledger-divergence.ts`):

```js
function computeTaskSourceHash(taskMarkdown) {
  return createHash21("sha256").update(`${TASK_PARSER_REVISION} ${taskMarkdown}`, "utf8").digest("hex");
}
```

- `TASK_PARSER_REVISION = "2026-10-23-component-identity-not-path"`.
- `initializeSddTaskExecutionState` grava `taskSourceHash` **uma única vez**, na inicialização.
- Quando o engine escreve os marcadores `**Status:** [x] done`, o hash do arquivo muda, mas o valor persistido não é atualizado.
- `findStatusCandidate` tenta provar equivalência reconstruindo a versão original: exige **exatamente uma linha de status por task** (`EXACT_STATUS_LINE = /^([ 	]*)\*\*Status:\*\*[ 	]*\[([ xX])\][ 	]*(done|pending)[ 	]*(
?)$/i`) e varre `doneCount` de `tasks.length - 1` até `0`.
- Como o laço **nunca considera `doneCount = tasks.length`**, uma spec com todas as tasks concluídas jamais encontra candidato.
- Qualquer alteração no `task.md` além dos marcadores de status (ou o estado "tudo concluído") torna a equivalência indetectável → `equivalence=none`, `action=block`.

O hash gravado (`102b8446…`) também não foi reproduzido a partir do arquivo atual sob nenhuma normalização testada (CRLF, LF, trim, deduplicação de fim de arquivo) — ou seja, o valor persistido não é recomputável de forma determinística a partir do conteúdo, o que impede reconciliação manual confiável.

**Impacto:** toda spec com 2+ tasks fica presa em `verified` mesmo com o código pronto. O trabalho já executado não pode ser certificado, e reconciliar o arquivo não resolve (o hash muda a cada tentativa).

**Sugestões**
- (a) recalcular e persistir `taskSourceHash` ao fim de cada projeção de status; ou
- (b) comparar uma forma canônica do conjunto de tasks (id, título, ordem) que ignore os marcadores de status; ou
- (c) expor um comando explícito de reconciliação que recalcule o hash a partir do estado atual, com registro do que foi reconciliado.

---

### Defeito 2 — o selo de task aceita arquivo inalterado

**Severidade:** crítico (integridade do ledger)

**Passos para reproduzir**
1. Rodar `/spec-implement` numa spec cuja primeira task altere apenas um arquivo existente e cuja última task seja de documentação.
2. Comparar os `sealedFiles` do ledger com o conteúdo real dos arquivos e com o diff produzido.

**Observado** — os `sealedFiles` são `sha256` do conteúdo **normalizado para LF** do arquivo *como está*, não um delta:
- **Task 2** — `filesChanged = [src/Ciclo.Infrastructure/Data/AppDbContext.cs]`; o diff real foi de **2 linhas** (dois `using`), sem o `DbSet`, sem a configuração de modelo e sem a guarda que a task exigia. Marcada `done`.
- **Task 18** — `completedAt = 2026-10-27T19:52:39.637Z`, **51 ms antes** de a task 1 começar (`19:52:39.688Z`), com `startedAt = null`; `filesChanged = [management/adr.md]`, cujo hash selado **é igual** ao do arquivo no HEAD (nunca foi modificado); `validations = [{ command: "dotnet test --no-build", exitCode: 0, status: "passed" }]` sem execução correspondente.

**Esperado:** `done` somente com evidência de mudança nos `expectedFiles` (hash anterior ≠ posterior) e com os `validationCommands` efetivamente executados.

**Sugestões**
- guardar o hash de cada `expectedFile` **antes** da task e recusar `done` quando nenhum mudou, salvo em tasks declaradas de verificação;
- executar e registrar a saída dos `validationCommands`, em vez de aceitar validação declarada;
- impedir que a inicialização marque qualquer task como concluída.

---

### Contornos aplicados neste repositório (para referência)

- Reconciliação autorizada pelo dono: `task.md` restaurado ao conteúdo pré-execução e tasks 2 e 18 reabertas no ledger, preservando a task 1 e sua evidência.
- Implementação manual da spec 110, com o roadmap mantido em `verified` e o ledger intocado — nenhuma conclusão foi fabricada.
- Evidência da entrega: build 0 erros/0 avisos, suíte **144/144**, migration e gatilhos validados em PostgreSQL real.

### Tentativa de contorno por reinicialização do ledger (2026-10-27, autorizada pelo dono)

Hipótese: com o `state.json` ausente, o engine recalcularia `taskSourceHash` a partir do `task.md` atual e o guard passaria.

**Resultado: falhou, e revelou onde o ledger realmente mora.** O `state.json` foi movido para backup e o comando foi executado. A execução recriou o `state.json` com o **mesmo** `taskSourceHash` (`102b8446…`) e as **mesmas 3 tasks** concluídas — e ainda reescreveu os marcadores no `task.md`. O registro de execução tem `checkpointReference: None` e as mesmas 3 tasks reapareceram, o que indica que o ledger é restaurado de um armazenamento próprio do runtime (`.dscode/state.db`), não apenas do `state.json`.

**Conclusão:** o defeito não é contornável por edição de arquivos do projeto. O ledger é reidratado pelo runtime, e cada tentativa reescreve o `task.md` antes de falhar. A correção precisa vir do mantenedor — o código exato está identificado acima.

### O que não deve ser feito como contorno

- Apagar `state.json`, lock ou artefatos de execução.
- Gravar `taskSourceHash` à mão (silencia o guard sem provar consistência).
- Editar `task.md` para "caber" em um hash.
- Marcar tasks por texto, sem execução.
