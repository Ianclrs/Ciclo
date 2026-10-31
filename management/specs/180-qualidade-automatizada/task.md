<!-- PIPELINE-CONTRACT: Task completion is determined by "**Status:** [x] done" at the end of each "### Task N" section.
     Checkboxes "- [ ]" under "**Validation:**" are human verification guides — the pipeline ignores them completely.
     To mark a task as done, add "**Status:** [x] done". To mark it as pending, use "**Status:** [ ] pending". -->
# Spec 180: Qualidade Automatizada — Implementation Tasks

## Planned Scaffolds

- `global.json`

## Task Order

Tasks MUST be executed sequentially in numerical order. Each task depends on the completion of all preceding tasks.

## Tasks

### Task 1: Fixar o SDK do .NET em global.json e consumi-lo na CI

**Objective:** Criar `global.json` na raiz declarando o SDK `10.0.100` com `rollForward` `latestFeature`, e fazer o job `backend` resolver o SDK por esse arquivo em vez da faixa `10.0.x`.
**Requirements Covered:** FR-001
**Design References:** AD-180-01; Component `global.json`; Component `job backend em .github/workflows/ci.yml` (passo 3); Component `job frontend` (passo 1, `timeout-minutes`).
**Actions:**
1. Criar `global.json` na raiz do repositório com exatamente o conteúdo: chave `sdk` contendo `version` = `10.0.100` e `rollForward` = `latestFeature`.
2. Em `.github/workflows/ci.yml`, no passo `uses: actions/setup-dotnet@v4` do job `backend`, substituir a chave `dotnet-version: '10.0.x'` pela chave `global-json-file: global.json`; manter `cache: true` e `cache-dependency-path: '**/*.csproj'`.
3. Em `.github/workflows/ci.yml`, declarar `timeout-minutes: 20` em `jobs.backend` e `timeout-minutes: 15` em `jobs.frontend`, imediatamente após `runs-on` de cada job.
**Expected Files:**
- `global.json`
- `.github/workflows/ci.yml`
**Validation:** `dotnet --version` na raiz do repositório resolve uma versão `10.0.100` ou superior da mesma banda (o `latestFeature` permite banda superior); `dotnet build Ciclo.sln` continua compilando; em `.github/workflows/ci.yml` a chave `global-json-file` está presente, `dotnet-version` não está mais presente no job `backend`, e os dois jobs declaram `timeout-minutes`.
**Status:** [x] done

### Task 2: Portão de cobertura de linha no backend e no frontend

**Objective:** Fazer o backend e o frontend falharem quando a cobertura total de linha ficar abaixo de 60, com a medição e o veredito produzidos pelo próprio runner de teste.
**Requirements Covered:** FR-002
**Design References:** AD-180-02; AD-180-03; Component `script test de frontend/package.json`; Component `instrumentação no projeto de teste do backend`; Data Structures.
**Actions:**
1. Em `tests/Ciclo.Api.Tests/Ciclo.Api.Tests.csproj`, acrescentar ao `ItemGroup` de `PackageReference` a linha `<PackageReference Include="coverlet.msbuild" Version="6.0.4" />`; manter a referência existente a `coverlet.collector` versão `6.0.4`.
2. Em `.github/workflows/ci.yml`, no job `backend`, substituir o passo de teste por `dotnet test tests/Ciclo.Api.Tests --logger "console;verbosity=normal" -p:CollectCoverage=true -p:CoverletOutputFormat=cobertura -p:Threshold=60 -p:ThresholdType=line -p:ThresholdStat=total`; a flag `--no-build` é removida desse passo.
3. Em `frontend/package.json`, alterar `scripts.test` para `node --test --experimental-test-coverage --test-coverage-lines=60 "tests/**/*.test.ts"`, preservando o glob atual.
4. Executar o comando de teste do backend e, se a cobertura total de linha ficar abaixo de 60, acrescentar testes aos arquivos de teste existentes em `tests/Ciclo.Api.Tests/` até o limiar ser atingido; o limiar não é reduzido.
5. Executar `npm test` em `frontend` e, se a cobertura total de linha ficar abaixo de 60, acrescentar testes aos arquivos de teste existentes em `frontend/tests/` até o limiar ser atingido; o limiar não é reduzido.
**Expected Files:**
- `tests/Ciclo.Api.Tests/Ciclo.Api.Tests.csproj`
- `.github/workflows/ci.yml`
- `frontend/package.json`
**Validation:** `dotnet test tests/Ciclo.Api.Tests --logger "console;verbosity=normal" -p:CollectCoverage=true -p:CoverletOutputFormat=cobertura -p:Threshold=60 -p:ThresholdType=line -p:ThresholdStat=total` termina com código de saída zero e produz `tests/Ciclo.Api.Tests/coverage.cobertura.xml`; `npm run lint`, `npm test` e `npm run build` em `frontend` terminam com código de saída zero; o script `test` contém `--experimental-test-coverage` e `--test-coverage-lines=60`.
**Status:** [x] done

### Task 3: Ausência de execução ignorada na suíte

**Objective:** Garantir que nenhum teste da suíte carregue marcador de execução ignorada e que a verificação seja parte do estado versionado.
**Requirements Covered:** FR-003
**Design References:** AD-180-05; Component `job backend` (Error Handling); Component `job frontend` (Error Handling).
**Actions:**
1. Procurar o marcador `Skip=` em `tests/Ciclo.Api.Tests/`; para cada ocorrência encontrada, tornar o teste executável ou removê-lo, de forma que nenhuma ocorrência permaneça. Não criar marcador novo.
2. Procurar o marcador `test.skip` em `frontend/tests/`; para cada ocorrência encontrada, tornar o teste executável ou removê-lo, de forma que nenhuma ocorrência permaneça. Não criar marcador novo.
3. Confirmar que `timeout-minutes` está declarado nos dois jobs de `.github/workflows/ci.yml`, declarado na Task 1, e que nenhum passo do workflow descarta o código de saída dos comandos (sem `|| true`, sem `continue-on-error`).
**Expected Files:**
- `.github/workflows/ci.yml`
**Validation:** `dotnet test tests/Ciclo.Api.Tests --logger "console;verbosity=normal" -p:CollectCoverage=true -p:CoverletOutputFormat=cobertura -p:Threshold=60 -p:ThresholdType=line -p:ThresholdStat=total` termina com código de saída zero; `npm test` em `frontend` termina com código de saída zero; nenhuma ocorrência de `Skip=` em `tests/Ciclo.Api.Tests/` e nenhuma ocorrência de `test.skip` em `frontend/tests/`.
**Status:** [x] done
