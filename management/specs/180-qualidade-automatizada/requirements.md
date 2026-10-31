---
name: qualidade-automatizada
status: verified
determinism: D
references: [V18]
---

# Spec 180: Qualidade Automatizada — Requirements

## Determinism Classification

**Class:** D — Deterministic

Todo resultado desta spec é propriedade de um arquivo versionado no repositório (workflow YAML, `global.json`, `package.json`, projeto de teste) ou do código de saída de um comando. Não há conteúdo gerado por modelo entre a entrada e o resultado esperado: o workflow ou declara o SDK fixado, o limite de tempo e o limiar de cobertura, ou não declara; e `dotnet test` / `node --test` ou saem com código diferente de zero abaixo do limiar, ou não saem.

## Value Delivery

Pipeline de integração contínua que compila, testa e faz lint em cada alteração, com testes de serviço para as regras de negócio, teste ponta a ponta do fluxo crítico (matrícula → documentos → aprovação) e testes de frontend. Fecha a lacuna de verificação que hoje depende de execução manual.

O repositório já possui o workflow `.github/workflows/ci.yml`, a suíte do backend em `tests/Ciclo.Api.Tests/` (incluindo o teste ponta a ponta `EnrollWithRequiredDocument_SubmitApprove_ReachesApproved`) e a suíte do frontend em `frontend/tests/`. Esta spec NÃO substitui esses donos: ela fecha exatamente as lacunas de automação que V18 nomeia e que hoje não existem — SDK flutuante, ausência de limiar de cobertura, ausência de limite de tempo de job e ausência de detecção de execução ignorada. O que já existe permanece como está e é apenas verificado.

## Functional Requirements

### FR-001: SDK do .NET fixado e consumido pela CI

**What:** Cria o arquivo `global.json` na raiz do repositório declarando `sdk.version` = `10.0.100` e `sdk.rollForward` = `latestFeature`, e altera o passo `actions/setup-dotnet@v4` do job `backend` em `.github/workflows/ci.yml` para resolver a versão a partir desse arquivo (chave `global-json-file: global.json`) em vez da faixa flutuante `dotnet-version: '10.0.x'` hoje declarada.
**Why:** V18 exige o workflow de CI com SDK fixado. Hoje a faixa `10.0.x` deixa o runner escolher qualquer patch do .NET 10, então duas execuções do mesmo commit podem usar toolchains diferentes.
**Acceptance Criteria:**
- [ ] O arquivo `global.json` existe na raiz do repositório. [verify: file-exists `global.json`]
- [ ] `global.json` declara a versão `10.0.100`. [verify: grep-present `10.0.100` `global.json`]
- [ ] `global.json` declara a política de roll-forward `latestFeature`. [verify: grep-present `latestFeature` `global.json`]
- [ ] O job `backend` resolve o SDK a partir do `global.json`. [verify: grep-present `global-json-file` `.github/workflows/ci.yml`]
**Failure modes:**
- Nenhum SDK que satisfaça `10.0.100` com `latestFeature` instalado: o `dotnet` falha com `NETSDK1141` e o job `backend` falha antes do restore, nunca prosseguindo em silêncio com outro SDK.
- `global.json` ausente no runner: o passo `setup-dotnet` falha por arquivo não encontrado e o job `backend` falha.
- `rollForward` omitido: o padrão `latestPatch` passa a recusar feature bands superiores do .NET 10, por isso o valor é declarado explicitamente.

### FR-002: Limiar de cobertura de linha executado como portão da CI

**What:** O projeto de teste `tests/Ciclo.Api.Tests/Ciclo.Api.Tests.csproj` recebe a referência `coverlet.msbuild` versão `6.0.4` (mesma versão já usada por `coverlet.collector`) e o passo de teste do job `backend` em `.github/workflows/ci.yml` passa a executar `dotnet test` com `-p:CollectCoverage=true -p:CoverletOutputFormat=cobertura -p:Threshold=60 -p:ThresholdType=line -p:ThresholdStat=total`; o script `test` de `frontend/package.json` passa a executar `node --test --experimental-test-coverage --test-coverage-lines=60 "tests/**/*.test.ts"`. Cobertura total de linha abaixo de 60 faz o comando sair com código diferente de zero em ambos os casos.
**Why:** V18 exige limiares de cobertura. Sem limiar, uma queda de cobertura é invisível e a CI permanece verde.
**Acceptance Criteria:**
- [ ] O projeto de teste referencia o instrumentador de cobertura por MSBuild. [verify: grep-present `coverlet.msbuild` `tests/Ciclo.Api.Tests/Ciclo.Api.Tests.csproj`]
- [ ] O job `backend` declara o portão de cobertura de linha total. [verify: grep-present `ThresholdStat=total` `.github/workflows/ci.yml`]
- [ ] O job `backend` fixa 60 como limiar de linha. [verify: grep-present `Threshold=60` `.github/workflows/ci.yml`]
- [ ] O script `test` do frontend executa o runner com cobertura nativa do Node. [verify: grep-present `--experimental-test-coverage` `frontend/package.json`]
- [ ] O script `test` do frontend fixa 60 como limiar de linha. [verify: grep-present `--test-coverage-lines=60` `frontend/package.json`]
- [ ] Os comandos de teste da CI executam com os portões ativos. [verify: project-validation]
**Failure modes:**
- Cobertura de linha total abaixo de 60: `dotnet test` sai com código diferente de zero e o job `backend` falha; o remédio definido nesta spec é acrescentar testes ao projeto de teste existente, nunca reduzir o limiar.
- Passo de cobertura executado com `--no-build`: a instrumentação não é aplicada e o resultado sai vazio; por isso o passo de cobertura não usa `--no-build`.
- Restore falho ao instrumentador: o job falha no restore e nunca reporta cobertura parcial como aprovada.
- Glob do frontend sem nenhum arquivo correspondente: o runner do Node sai com código diferente de zero e o job falha.

### FR-003: Falha, timeout e execução ignorada detectados na CI

**What:** Declara `timeout-minutes` em cada job de `.github/workflows/ci.yml` (job `backend`: 20; job `frontend`: 15) e garante que nenhum teste carregue marcador de execução ignorada: nenhuma ocorrência do marcador `Skip=` do xUnit em `tests/Ciclo.Api.Tests/` e nenhuma ocorrência de `test.skip` em `frontend/tests/`. Qualquer código de saída diferente de zero de restore, build, teste ou lint encerra o job com falha.
**Why:** V18 exige comportamento em falha, timeout e execução ignorada. Hoje um job pendurado consome o limite do runner sem diagnóstico, e um teste marcado como ignorado mantém a CI verde sem executar a verificação.
**Acceptance Criteria:**
- [ ] Cada job do workflow declara limite de tempo. [verify: grep-present `timeout-minutes` `.github/workflows/ci.yml`]
- [ ] Nenhum teste do backend está marcado para ser ignorado. [verify: grep-absent `Skip=` `tests/Ciclo.Api.Tests/`]
- [ ] Nenhum teste do frontend está marcado para ser ignorado. [verify: grep-absent `test.skip` `frontend/tests/`]
- [ ] A suíte executada prova que as etapas rodam e passam. [verify: project-validation]
**Failure modes:**
- Job excedendo o limite declarado: o runner cancela o job e o marca como falho, em vez de mantê-lo pendurado até o limite global.
- Marcador de execução ignorada encontrado: a verificação de ausência falha e o portão rejeita a alteração; um teste hoje ignorado passa a ser executado ou é removido, nunca mantido ignorado.
- Falha em restore, build, teste ou lint: o passo sai com código diferente de zero e o job falha, sem passo posterior que mascare a falha.
- Execução cancelada por `cancel-in-progress`: a execução substituída não é contabilizada como aprovada.

## Non-Functional Requirements

### NFR-001: Nenhum framework de teste novo é introduzido

**What:** A infraestrutura de teste permanece a que o repositório já usa: `node --test` no frontend e xUnit sobre `dotnet test` no backend. Nenhum runner adicional (`vitest`, `jest`, `@testing-library/*`) é adicionado a `frontend/package.json`, e nenhum arquivo de configuração de runner paralelo é criado.
**Acceptance Criteria:**
- [ ] O frontend continua executando os testes pelo runner nativo do Node. [verify: grep-present `node --test` `frontend/package.json`]
- [ ] Nenhum runner de teste alternativo é adicionado ao frontend. [verify: grep-absent `vitest` `frontend/package.json`]
- [ ] Nenhum arquivo de configuração de runner paralelo é criado. [verify: file-absent `frontend/vitest.config.ts`]
**Failure modes:**
- Dois runners coexistentes: a mesma regra passa a ter dois donos e o custo de manutenção duplica; a verificação de ausência rejeita a alteração.
- Script `test` reescrito para outro runner: a verificação de presença de `node --test` falha.

### NFR-002: Comandos da CI idênticos aos comandos locais

**What:** Todo comando executado pela CI é reproduzível localmente sem variável de ambiente extra: `dotnet restore Ciclo.sln`, `dotnet build Ciclo.sln`, `dotnet test` sobre o projeto de testes e, no diretório `frontend`, `npm ci`, `npm run lint`, `npm test` e `npm run build`. Nenhum passo depende de segredo ou de serviço externo.
**Acceptance Criteria:**
- [ ] O job `backend` executa o build da solução antes do teste. [verify: grep-present `dotnet build Ciclo.sln` `.github/workflows/ci.yml`]
- [ ] O job `frontend` executa o build do frontend após lint e teste. [verify: grep-present `npm run build` `.github/workflows/ci.yml`]
- [ ] Os comandos de validação do projeto continuam passando com os portões ativos. [verify: project-validation]
**Failure modes:**
- Comando dependente de estado local (por exemplo, caminho absoluto da máquina): o job falha no runner e a divergência aparece como falha de CI, nunca como aprovado.
- Passo que exige segredo não declarado: o job falha por ausência da variável.

## Constraints

- O teste ponta a ponta do fluxo crítico permanece na suíte do backend. [verify: grep-present `SubmitApprove` `tests/Ciclo.Api.Tests/EnrollmentJourneyTests.cs`]
- Os testes de serviço das regras de negócio permanecem na suíte. [verify: file-exists `tests/Ciclo.Api.Tests/EnrollmentStateMachineTests.cs`]
- A suíte do frontend permanece sob o runner do Node. [verify: grep-present `node --test` `frontend/package.json`]
- O projeto de teste mantém o coletor de cobertura hoje referenciado. [verify: grep-present `coverlet.collector` `tests/Ciclo.Api.Tests/Ciclo.Api.Tests.csproj`]
- O workflow mantém o disparo em `push` para `main` e em toda `pull_request`. [verify: grep-present `pull_request` `.github/workflows/ci.yml`]
- O workflow mantém permissões somente de leitura. [verify: grep-present `contents: read` `.github/workflows/ci.yml`]
- O job `backend` continua executando `dotnet restore Ciclo.sln` antes do build. [verify: grep-present `dotnet restore Ciclo.sln` `.github/workflows/ci.yml`]
- O job `frontend` continua executando `npm run lint`. [verify: grep-present `npm run lint` `.github/workflows/ci.yml`]
- O job `frontend` continua instalando dependências com `npm ci`. [verify: grep-present `npm ci` `.github/workflows/ci.yml`]
- O job `frontend` continua fixado no Node 24. [verify: grep-present `node-version: '24'` `.github/workflows/ci.yml`]
- O cache do NuGet continua ligado ao passo de setup do .NET. [verify: grep-present `cache-dependency-path` `.github/workflows/ci.yml`]
- A validação completa do projeto passa. [verify: project-validation]

## Edge Cases & Error States

- SDK fixado ausente no runner: `setup-dotnet` falha e o job `backend` falha antes de qualquer build; `latestFeature` cobre apenas feature bands mais novas do mesmo major.
- Cobertura medida abaixo de 60%: o remédio é acrescentar testes ao projeto de teste existente; o limiar não é reduzido.
- Teste com marcador de execução ignorada: a ausência exigida é verificada e a alteração é rejeitada.
- Job pendurado (rede lenta em `npm ci` ou em restore): `timeout-minutes` cancela o job e ele é marcado como falho.
- Duas execuções concorrentes no mesmo ref: `cancel-in-progress` mantém apenas a mais recente, e a cancelada não conta como aprovada.
- Lockfile do frontend divergente de `package.json`: `npm ci` falha antes de lint e teste.
- Passo de cobertura executado com `--no-build`: a instrumentação não é aplicada; o passo de cobertura não usa `--no-build`.

## Anti-Scope

- Nenhum framework de teste de frontend novo é adicionado; o runner é o nativo do Node. [verify: grep-absent `vitest` `frontend/package.json`]
- Nenhum arquivo de configuração de runner paralelo é criado. [verify: file-absent `frontend/vitest.config.ts`]
- Nenhum passo de deploy ou publicação é adicionado ao workflow. [verify: grep-absent `deploy` `.github/workflows/ci.yml`]
- Nenhum serviço externo (banco, fila, navegador headless) é declarado no workflow. [verify: grep-absent `services:` `.github/workflows/ci.yml`]

## Dependencies

- V18 (Qualidade Automatizada) — valor entregue por esta spec.
- `.github/workflows/ci.yml` — dono existente do pipeline; esta spec o estende, não o substitui.
- `tests/Ciclo.Api.Tests/` — projeto de teste do backend, incluindo `ApiTestHost.cs` e `EnrollmentJourneyTests.cs`.
- `frontend/package.json` e `frontend/tests/` — runner e suíte de teste do frontend.
- `coverlet.msbuild` versão `6.0.4` — instrumentador acrescentado ao projeto de teste; é a única dependência de build nova e usa a mesma versão já presente em `coverlet.collector`.
- Runners hospedados do GitHub Actions com acesso a NuGet e ao registro npm.

## Out of Scope

- Migração para Vitest ou Testing Library; o runner existente é preservado. [verify: file-absent `frontend/testing-library.ts`]
- Cobertura por arquivo ou por módulo; o portão é sobre o total de linhas.
- Testes de carga, de mutação e de segurança.
- Publicação de artefatos, release e versionamento.
- Alteração de código de produto em `src/`.
- Novos testes além do necessário para sustentar o limiar de 60% de linhas.

## Test Scope

- `tests/Ciclo.Api.Tests/EnrollmentJourneyTests.cs` — prova o fluxo ponta a ponta matrícula → documentos → aprovação preservado por esta spec, executado sob o portão de cobertura do backend.
- `frontend/tests/deployConfig.test.ts` — prova que a configuração de build do frontend continua resolvível sob o script de teste versionado que recebe o limiar de cobertura.
- A aplicação do SDK fixado, do limiar de cobertura e dos limites de tempo é provada pela execução dos mesmos comandos da CI declarados neste documento e pela validação do projeto.
