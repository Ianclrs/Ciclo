# Issue: `spec-implement` fica bloqueado por `task-ledger-divergence` em qualquer spec com mais de uma task

**Severidade:** bloqueador
**Componente:** runtime DsCode — `spec-implement` (task ledger guard)
**Ambiente:** Windows 10 (19045) · .NET SDK 10.0.302 · PostgreSQL 18.3 · binário `dscode.exe` (Node SEA/Bun, 107 MB)
**Repositório de teste:** projeto `Ciclo` — spec 110, 18 tasks
**Data:** 2026-10-27

---

## Resumo

1. O guard `task-ledger-divergence` compara o hash atual do `task.md` com `taskSourceHash`, gravado **apenas na inicialização** do ledger. Como o próprio engine reescreve os marcadores `**Status:** [x] done`, o hash muda por ação dele — e o valor persistido nunca é atualizado. Resultado: a execução seguinte sempre bloqueia.
2. O selo de conclusão de task aceita arquivo **inalterado**: a task é marcada `done` mesmo quando nenhum `expectedFile` mudou (chegou a marcar a última task 51 ms antes da primeira começar).

## Passos para reproduzir (defeito 1)

1. Ter uma spec em `verified` com 2+ tasks e ledger ainda não inicializado.
2. Rodar `/spec-implement <spec>`.
3. O runtime executa a task 1, executa a task 2, encerra (`task-completed-next-task-pending`) e **reinicia sozinho** para a task 3.
4. No reinício: `ledger-guard ... action=block ... equivalence=none ... task-ledger-divergence` → execução `failed`; a task 3 nunca roda.

**Esperado:** a execução continua na task pendente depois de o engine gravar os marcadores.
**Observado:** bloqueio permanente; rodar de novo repete o ciclo.

## Causa raiz (código extraído do bundle)

```js
function computeTaskSourceHash(taskMarkdown) {
  return createHash21("sha256").update(`${TASK_PARSER_REVISION} ${taskMarkdown}`, "utf8").digest("hex");
}
// TASK_PARSER_REVISION = "2026-10-23-component-identity-not-path"
```

```js
const EXACT_STATUS_LINE = /^([ \t]*)\*\*Status:\*\*[ \t]*\[([ xX])\][ \t]*(done|pending)[ \t]*(\r?)$/i;

// findStatusCandidate: reconstrói a versão original resetando sufixos de status
for (let doneCount = tasks.length - 1; doneCount >= 0; doneCount -= 1) { ... }
```

Três problemas somados:

- `initializeSddTaskExecutionState` grava `taskSourceHash` **uma única vez**; nada o atualiza quando o engine reescreve `task.md`.
- `findStatusCandidate` exige **exatamente uma linha de status por task** e depende de reconstrução byte-exata do markdown original.
- O laço **nunca considera `doneCount = tasks.length`**, então uma spec com todas as tasks concluídas jamais encontra candidato.

## Medições

Todas com o `task.md` byte-idêntico ao HEAD (CRLF, 338 linhas, sem BOM):

| Momento | Conteúdo de `task.md` | Ledger | Hash calculado |
|---|---|---|---|
| Inicialização | 18 `pending` | 0 done | `102b8446…` |
| Após execução | 3 `done` | 3 done | `7f276a3a…` |
| Após reconciliação | 18 `pending` | 1 done | `22ab361d…` |

Conteúdo igual ao da inicialização produziu hash diferente. O valor persistido (`102b8446…`) **não foi reproduzido** a partir do arquivo atual sob nenhuma normalização testada (CRLF, LF, trim, sem newline final), o que impede reconciliação manual confiável.

## Correção sugerida

```js
// (a) ao gravar marcadores, atualizar o hash persistido
state.taskSourceHash = computeTaskSourceHash(rewrittenMarkdown);

// (b) ou comparar descritores (id, título, expectedFiles, validationCommands)
//     em vez de bytes do markdown;

// (c) e iniciar a busca em tasks.length, cobrindo o caso "todas concluídas".

// (d) oferecer um comando explícito de reconciliação que recalcule o hash
//     a partir do estado atual e registre o que reconciliou.
```

## Defeito 2 — selo aceita arquivo inalterado

Os `sealedFiles` são `sha256` do conteúdo normalizado para LF do arquivo **como está**, não um delta:

- **Task 2** marcada `done` com `filesChanged = [AppDbContext.cs]`, mas o diff real foi de **2 linhas** (dois `using`), sem o modelo nem a guarda que a task exigia.
- **Task 18** marcada `done` com `completedAt = 19:52:39.637`, **51 ms antes** de a task 1 iniciar (`19:52:39.688`), `startedAt = null`, selando `adr.md` **intocado** e registrando `dotnet test --no-build` como validação aprovada sem execução correspondente.

**Sugestão:** guardar o hash de cada `expectedFile` antes da task, recusar `done` quando nenhum mudou (salvo tasks declaradas de verificação), executar e registrar os `validationCommands` e impedir que a inicialização marque qualquer task como concluída.

## Contorno testado e descartado

Mover o `state.json` para backup e deixar o engine reinicializar. **Não funciona:** a execução recriou o mesmo estado (mesmo `taskSourceHash`, mesmas 3 tasks concluídas) e ainda reescreveu o `task.md`. O registro de execução tem `checkpointReference: None`, o que indica que o ledger é reidratado de um armazenamento próprio do runtime (`.dscode/state.db`) — não do arquivo por spec.

## Impacto

Qualquer spec com mais de uma task fica presa em `verified`, mesmo com o código pronto. Neste repositório, a spec 110 foi implementada manualmente (build 0/0, 144/144 testes, migration e gatilhos validados em PostgreSQL real) e **não pode ser certificada** enquanto o guard não for corrigido.

## O que não serve como contorno

- Apagar `state.json`, lock ou artefatos de execução.
- Gravar `taskSourceHash` à mão.
- Editar `task.md` para "caber" no hash.
- Marcar tasks por texto, sem execução.
