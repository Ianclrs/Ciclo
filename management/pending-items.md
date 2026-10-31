# Pending Items

> Pendências abertas fora do roadmap. Formato: ID, estado, dono, impacto, evidência e ação recomendada.
> Este arquivo é a memória durável de bloqueios que não pertencem a uma spec específica.

---

## Decisão vigente (2026-10-27) — contorno manual aceito até o runtime ser corrigido

**Dono:** usuário. **Alcance:** todo o fluxo SDD deste repositório.

Enquanto **P-001** e **P-002** não forem corrigidos no runtime:

1. **Nenhuma spec nova entra em `/spec-implement` ou `/spec-test`.** O selo de task pode marcar
   concluído sem mudança real (P-002) e o guard de ledger impede o avanço (P-001) — rodar o
   pipeline hoje produziria estado falso, que é pior do que não rodar.
2. **Trabalho de produto continua sendo entregue manualmente**, como na spec 110: código,
   testes, gates e registro no plano; o roadmap **não** é alterado por shell nem por edição
   direta. A spec permanece no status que o pipeline deixou (`verified`) e a evidência fica no
   `plano-correcoes.md`.
3. **A correção pertence ao runtime**, não ao projeto: recalcular/persistir `taskSourceHash`
   (P-001) e exigir mudança real de arquivo para selar `done` (P-002).
4. Quando o runtime for atualizado, revalidar a spec 110 antes de retomar qualquer pipeline.

---

## P-003 — Runtime: leitura do `roadmap.md` é bloqueada (over-block)

**Estado:** aberto
**Descoberto:** 2026-10-27
**Dono:** runtime do DsCode (defeito de ferramenta)
**Impacto:** diagnóstico e auditoria do projeto ficam cegos; `git pull` e `git checkout` também
são recusados quando o commit de entrada toca o roadmap

### Comportamento observado

Comandos **somente de leitura** falham com a mensagem de proteção:

```
grep -E "^\| [0-9]+ \|" management/roadmap.md
→ management/roadmap.md is protected. Roadmap mutations are performed exclusively by the SDD pipeline...
```

E operações de VCS em bloco — `git pull --ff-only`, `git merge --ff-only` e até
`git checkout origin/main -- README.md` — são recusadas com:

```
The validation trust configuration (.dscode/settings.json) is protected. A model turn cannot
  grant, widen or restore the permissions under which its own output is validated.
```

Mesmo quando o único arquivo do commit de entrada que o agente não pode escrever é uma linha do
roadmap (foi o caso da integração da spec 110: `110: created → verified`).

### Por que isso é defeito, e não proteção

A regra existente (“roadmap só muda pelo pipeline”) está correta e foi respeitada. O excesso é
bloquear a **leitura** e impedir que o usuário autorize a integração de um commit legítimo por
outro caminho. Um guarda que impede trabalho legítimo deixa de proteger e passa a obstruir —
exatamente o critério de KISS do contrato do repositório.

### Ação recomendada

- permitir leitura de artefatos protegidos (o bloqueio deve valer para escrita);
- oferecer uma via explícita para o dono autorizar operações de VCS que tragam mudanças de
  artefato geradas pelo próprio pipeline (ex.: aceitar o arquivo vindo de `origin` como
  autoridade, sem escrita pelo modelo);
- manter o bloqueio de escrita direta como está.

### O que NÃO fazer

- Não ler o roadmap por caminhos alternativos para contornar o guarda.
- Não editar o roadmap para “casar” com o estado local.

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

## P-005 — Runtime: validador de sintaxe TS não resolve `typescript` e reverte toda mutação `.ts`/`.tsx`

**Estado:** ABERTO — não há contorno por arquivo do projeto; bloqueia a spec 150 (task 6 é 100% `.ts`/`.tsx`)
**Descoberto:** 2026-10-31 (execução `b0d4d484`, spec 150, task 6)
**Dono:** runtime do DsCode (resolução de módulo)
**Impacto:** qualquer `.ts`/`.tsx` novo ou alterado é recusado com `UNCHECKABLE`, e a mutação é revertida (`deleted-as-new`) — nenhuma spec com frontend avança

### Comportamento observado

```
error: Mutation validation failed immediately after file changes:
- frontend/src/api/academic.ts: syntax: UNCHECKABLE: typescript is not resolvable,
  syntax check skipped (fail-closed).
```

O arquivo foi criado e, na mesma transação, **apagado** pelo rollback.

### O que já foi tentado (e não bastou)

**1. Junction na raiz** (`node_modules/typescript` → `frontend/node_modules/typescript`, 2026-10-31 20:13Z). `require.resolve` da raiz passou a funcionar, mas a execução seguinte (`52b89b95`, 20:15Z) falhou **igual**. Duas explicações possíveis, ambas fora do alcance de um arquivo do projeto:

- o validador recusa resolução que atravessa symlink/junction (o contrato do runtime proíbe symlink traversal);
- o resultado negativo é memoizado no processo que iniciou a sessão — a primeira tentativa (`b0d4d484`, 20:12Z) aconteceu **antes** do junction, e o processo do TUI não foi reiniciado.

**2. Cópia real do pacote na raiz** (2026-10-31 20:16Z, `node_modules/typescript`, 24 MB, ignorado pelo git). Elimina a variável do symlink: `require.resolve('typescript')` da raiz devolve `C:/Users/tempu/OneDrive/SE/node_modules/typescript/lib/typescript.js` (6.0.3) e `npm run build` do frontend continua ✅.

**Falhou igual duas vezes com a cópia presente:** `77ad491d` (20:18:28Z, `academic.ts`) e `b301de57` (20:24:24Z, `types/index.ts`). Isso **refuta** a hipótese da raiz, do symlink e da memoização — a cópia foi removida em 20:35Z por não resolver nada.

### Causa atual (após refutar a raiz)

A resolução do validador não consulta o `node_modules` do projeto, qualquer que seja a base testada. Os outros caminhos de leitura do runtime tratam `node_modules` como diretório excluído (`glob`/`grep` o filtram por padrão e o `.gitignore` o ignora na linha 294), o que é consistente com um validador que enxerga o FS pela mesma camada filtrada: o pacote existe no disco, mas não existe para o resolvedor.

Bases medidas com `require.resolve` (todas irrelevantes, para registro):

| Base | Resultado |
|---|---|
| raiz, **com a cópia presente** | resolve ✅ (mas o validador falhou nas 2 execuções seguintes) |
| `frontend/` | resolve ✅ |
| `C:/Users/tempu/OneDrive/.dscode/` (diretório do `dscode.exe`) | `MODULE_NOT_FOUND` |

Também não existe chave de configuração para desligar ou relaxar o check: `.dscode/settings.json` contém apenas `model`, `modelInstances` e `trust`, e o binário não embute nenhuma chave `mutationValidation*`/`syntaxCheck*`.

### Ação recomendada no runtime

Resolver o compilador a partir do diretório do arquivo validado (ou de um `package.json` ascendente), com a raiz apenas como último recurso; aceitar resolução por symlink dentro da própria raiz do projeto; e, quando não resolver, reportar como limitação de ambiente **sem reverter a mutação** — o rollback apaga trabalho válido por um defeito do validador.

### Caminhos possíveis (decisão do dono do projeto)

1. **Atualizar/reinstalar o DsCode** e repetir `/spec-implement 150` — é o que corrige o dono do defeito.
2. **Reduzir escopo**: a task 6 é a única pendente e é toda frontend; as tasks 1–5 e 7 estão seladas. A spec fica em `verified` até o runtime aceitar `.ts`.
3. **Entrega manual da task 6** com evidência (tsc/lint/testes/build), sem selo do pipeline — mesmo regime da spec 110 sob P-001/P-002.

### O que NÃO fazer

- Criar `package.json` na raiz só para o validador: muda a raiz do build (o deploy usa `frontend` como Root Directory) e exige reinstalação.
- Copiar o pacote para a raiz **para destravar o validador de mutação**: já testado e inútil (20:13Z junction, 20:16Z cópia real; 3 execuções falharam).
- Versionar `node_modules/`.
- Instalar `typescript` ao lado do `dscode.exe`: é fora do projeto e não corrige o dono do defeito (resolução de módulo do runtime).

### Segundo sintoma: o verificador de critério também não lê `.ts` (2026-10-31 21:03Z)

A task 4 da spec 160 **não foi selada** por um motivo diferente do anterior:

```
REQ-006[2] UNCHECKABLE function-exists `selectableStudentIds`
(frontend/src/domain/renewalBatch.ts) — verification not executable for this criterion
```

O arquivo existe e a função está declarada exatamente como o contrato pede (`export function selectableStudentIds(items: RenewalPreviewItem[]): string[]`), exercitada por 4 testes verdes. O verificador de critério, porém, não consegue avaliar arquivo `.ts` — é o mesmo `typescript` não resolvido, agora na fase de *verificação* e não na de *mutação*. Ou seja: o P-005 bloqueia a certificação por dois caminhos independentes.

### Terceiro sintoma e decisão de parar (2026-10-31 21:05Z)

A tentativa seguinte nem chegou a verificar: `turn-stalled: reasoning-only` — o turno raciocinou sem agir e o runtime abortou, **antes** de qualquer escrita (árvore confirmada intacta depois).

Com duas falhas seguidas por motivos diferentes e nenhum deles de projeto, o dono decidiu **parar o ciclo da spec 160**. A spec permanece `verified` com as tasks 1–3 seladas e 4–6 entregues manualmente com evidência (ver abaixo). Retomar só depois que o runtime conseguir ler `.ts`.

**Hipótese do junction: falsificada (21:06:57Z).** A execução `7b26a795` rodou com `node_modules` na raiz (junction para `frontend/node_modules`) e falhou igual:

```
REQ-006[2] UNCHECKABLE function-exists `selectableStudentIds`
(frontend/src/domain/renewalBatch.ts) — verification not executable
REQ-006[5] FAIL grep-present `admin/enrollment-renewal` (frontend/src/App.tsx)
```

Conclusão medida: o verificador de critério **não** resolve módulo pela base do workspace — ambos os verificadores vivem dentro do binário. O junction não ajuda nenhum dos dois; remover com `rm -rf node_modules` na raiz quando não for mais necessário. Ele é ignorado pelo git (linha 294 do `.gitignore`), então nenhum arquivo versionado depende dele.

### Defeito do contrato, independente do runtime: FR-006[5] nomeia um literal impossível

O critério exige `grep-present admin/enrollment-renewal` em `frontend/src/App.tsx`. Esse literal **não pode existir** ali: as rotas administrativas são relativas (`path="enrollment-renewal"`, `App.tsx:60`) dentro de um `<Routes>` montado em `/admin/*`. O literal `/admin/enrollment-renewal` só aparece em `pages/admin/EnrollmentList.tsx:242` (o `<Link>`). O critério nomeia arquivo e literal incompatíveis com o design — nenhuma implementação pode prová-lo.

**Cura única (dono: `spec-new`, spec editável enquanto `verified`)** — trocar por verificadores de texto, que o runtime comprovadamente executa (os `grep-present` da mesma FR passaram):

| Critério | Hoje | Reescrita que preserva a exigência |
|---|---|---|
| `REQ-006[3]` | `function-exists selectableStudentIds` | `grep-present \`export function selectableStudentIds\`` em `frontend/src/domain/renewalBatch.ts` |
| `REQ-006[5]` | `grep-present admin/enrollment-renewal` em `App.tsx` | `grep-present \`enrollment-renewal\`` em `frontend/src/App.tsx` |

Nenhuma exigência comportamental muda — só a forma de provar. **Autorização concedida e reescrita aplicada (2026-10-31 21:18Z).** O dono autorizou explicitamente e as duas tags da FR-006 foram reescritas preservando a exigência. Medido após a edição: os 5 verificadores textuais da FR-006 passam (`getRenewalPreview` recursivo em `frontend/src/`, `renewEnrollmentBatch`, `export function selectableStudentIds`, `enrollment-renewal` em `App.tsx`). Os dois `function-exists` restantes da spec apontam para C# (`GetPreviewAsync`, `RenewAsync`) e já foram selados nas tasks 1–2. Próximo passo: `/spec-implement 160` deve selar as tasks 4–6.

### Entrega manual da task 6 (2026-10-31)

Decisão do dono do projeto: entregar a task 6 **fora do pipeline**, com evidência, mantendo a spec em `verified` — mesmo regime da spec 110 sob P-001/P-002. O selo continua pendente até o runtime aceitar mutação `.ts`.

**Arquivos entregues:** `frontend/src/api/academic.ts` (18 funções), `frontend/src/domain/academic.ts` (regras puras: situação, faixa 0–10, `DateOnly` sem fuso), `frontend/src/types/index.ts` (7 tipos), `pages/admin/AcademicStructure.tsx` (4 seções com criar/editar/excluir), `pages/admin/GradeEntry.tsx`, `App.tsx` (rotas + item de menu), `api/parent.ts` (`getChildReportCard`), `pages/parent/ChildDetail.tsx` (aba de notas pelo boletim), `api/errors.ts` (16 códigos acadêmicos), `tests/academic.test.ts` (7 casos), `tests/manual/academic-routes-probe.mjs` (sonda CDP).

**Gates:** `tsc` ✅ · lint **0 erros** (2 avisos pré-existentes) · frontend **92/92** · `npm run build` ✅ · backend **384/384** · `dotnet build` **0 erros**.

**Medido no Chrome real** (API em Development, `vite dev`): `/admin/academico` monta as 4 seções com os dados; `/admin/notas` mostra só o roster da turma (`2 alunos`), avaliação `Prova 1 · 10/03/2026 · peso 2`, recusa `11` com "A nota deve estar entre 0 e 10." e grava `7,5` com toast; turma sem avaliação mostra estado vazio explícito e **nenhum** `role="alert"`; boletim do responsável exibe `Matemática · 7,5 · — · 7,5 · Aprovado`.

**Defeito encontrado no caminho (corrigido):** a task 4 estava selada mas `AddScoped<IAcademicService, AcademicService>()` nunca foi adicionado a `Program.cs` — toda rota `api/academic` respondia **500**. Registro adicionado; ver **L8** em `lessons.md`.

**Nota de escopo:** a migração `AddAcademicStructure` foi aplicada no banco de dev pelo startup em Development (é o caminho normal do projeto), e a sonda criou dados de exemplo no tenant demo: componente `Matemática`, avaliação `Prova 1` em `1º Ano A` e nota `7,5` de Ana Clara.

### Entrega manual das tasks 4, 5 e 6 da spec 160 (2026-10-31)

Mesmo bloqueio (P-005), mesmo regime: as três tasks restantes da 160 são frontend e foram entregues fora do pipeline, com evidência. A spec segue em `verified` sem selo.

**Arquivos:** `frontend/src/domain/renewalBatch.ts` (regras puras), `pages/admin/EnrollmentRenewal.tsx` (prévia + confirmação + desfecho por aluno), `types/index.ts` (6 tipos), `api/enrollments.ts` (`getRenewalPreview`, `renewEnrollmentBatch`), `api/errors.ts` (`codeMessage`, agora dono único de código → texto), `App.tsx` (rota `/admin/enrollment-renewal`), `pages/admin/EnrollmentList.tsx` (entrada para a tela), `tests/renewalBatch.test.ts` (4 casos), `tests/manual/renewal-probe.mjs`.

**Defeito encontrado no caminho (corrigido):** o contrato do frontend declara `outcome` como união literal de texto, mas `RenewalOutcome` não tinha conversor e a API não configura conversor global — o campo sairia como `0..3`, `describeOutcome` devolveria `undefined` e a tela mostraria rótulo indefinido, **sem erro de compilação**. Resolvido com `[JsonConverter(typeof(JsonStringEnumConverter))]` no próprio enum (blast radius de um tipo só) e travado por `RenewalOutcomeWireFormatTests`.

**Gates:** `tsc` ✅ · lint **0 erros** (2 avisos pré-existentes) · frontend **96/96** · `npm run build` ✅ · backend **403/403**.

**Medido no Chrome real:** `/admin/enrollment-renewal` monta com o seletor de período; ao escolher `Matrículas 2026`, a prévia devolve o aluno bloqueado com o motivo em português, checkbox **desabilitado**, pendências listadas e confirmação desabilitada; `GET renewal-preview` → 200, `POST renewal-batch` → `"outcome":"BlockedPendingDocuments"` (texto, provando o conversor no fio); a página de Matrículas expõe o link `Rematrícula em lote`.

**Nota de escopo:** a sonda criou no tenant demo o aluno `Teste Rematrícula` (turma `4º Ano D`, ano letivo 2027) para exercitar a prévia.

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
