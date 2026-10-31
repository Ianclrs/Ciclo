# Spec 180: Qualidade Automatizada — Design

## Design Approach

A spec estende o único dono existente do pipeline de qualidade — `.github/workflows/ci.yml` — em vez de criar um segundo pipeline, um script orquestrador ou um módulo de verificação paralelo. A entrega é composta por quatro edições pequenas e verificáveis:

1. um arquivo novo na raiz, `global.json`, que passa a ser a única fonte da versão do SDK do .NET;
2. o job `backend` do workflow passa a resolver o SDK por esse arquivo, declara `timeout-minutes` e executa o teste com portão de cobertura;
3. uma linha alterada em `frontend/package.json` (flags de cobertura no script `test`);
4. uma referência de pacote acrescentada ao projeto de teste do backend.

Nenhum código de produto é alterado; nenhum script de shell novo é criado. A cobertura é medida e enforçada pelo próprio runner de teste (código de saída), o que elimina a necessidade de um passo intermediário que leia XML e decida por conta própria.

## Architecture Decisions

### AD-180-01: Versão do SDK declarada uma única vez, em `global.json`

`.github/workflows/ci.yml` deixa de declarar `dotnet-version: '10.0.x'` e passa a apontar para `global.json` via `global-json-file`. A versão e a política de roll-forward passam a ter um único dono (o arquivo), consumido tanto pelo `dotnet` na máquina local quanto pelo `setup-dotnet` no runner. Declarar a versão nos dois lugares criaria duas fontes de verdade que divergem na primeira atualização.

`rollForward: latestFeature` é declarado explicitamente porque o padrão (`latestPatch`) recusaria feature bands superiores do .NET 10, quebrando qualquer máquina ou runner com uma banda mais nova do mesmo major.

### AD-180-02: Portão de cobertura no código de saída do runner, sem script intermediário

- Backend: `coverlet.msbuild` expõe `Threshold`, `ThresholdType` e `ThresholdStat` como propriedades MSBuild; quando a cobertura total de linha fica abaixo do valor, o próprio `dotnet test` termina com código diferente de zero. O pacote entra na mesma versão já usada por `coverlet.collector` (`6.0.4`), mantendo os dois instrumentadores casados.
- Frontend: o runner nativo do Node, já usado pelo script `test`, aceita `--experimental-test-coverage` combinado com `--test-coverage-lines=60`; abaixo do limiar, ele termina com código diferente de zero.

A alternativa rejeitada seria um passo de CI que baixasse um relatório e comparasse o número com um script: mais superfície, mais um dono e uma falha silenciosa sempre que o formato do relatório mudasse.

### AD-180-03: Runner de teste do frontend preservado

O script `test` continua sobre `node --test`. Introduzir `vitest` ou `@testing-library/*` criaria um segundo dono para a mesma responsabilidade (executar os testes de `frontend/tests/`) e obrigaria a manter duas configurações de resolução de módulo. As flags de cobertura são acrescentadas ao comando existente, não o substituem.

### AD-180-04: Limite de tempo no nível do job

`timeout-minutes` é declarado no job, não em cada passo. Um passo pendurado em rede (restore, `npm ci`) consumiria o orçamento do passo; o limite do job garante que o run inteiro termine em tempo limitado e seja marcado como falho, sem depender de saber qual passo travou.

### AD-180-05: Execução ignorada tratada como ausência de marcador de skip

Contar testes executados exigiria ler a saída do runner ou um arquivo TRX. A verificação adotada é determinística e menor: nenhum teste carrega marcador de skip (`Skip=` no backend, `test.skip` no frontend). Com essa invariante, um run verde prova que toda a suíte foi executada.

## Component / Module Breakdown

### Component: `global.json` (arquivo novo na raiz)

**Purpose:** declarar a versão do SDK do .NET e a política de roll-forward usadas tanto pela máquina local quanto pela CI.
**Interface:** arquivo JSON na raiz do repositório com o conteúdo exato:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

**Internal Logic:** o comando `dotnet` resolve o SDK a partir do diretório corrente subindo até encontrar `global.json`; `actions/setup-dotnet@v4` lê o mesmo arquivo quando recebe `global-json-file`.
**Dependencies:** nenhuma.
**Error Handling:** se nenhum SDK satisfizer `10.0.100` com `latestFeature`, o `dotnet` falha com `NETSDK1141` e o job `backend` falha antes do restore. Se o arquivo estiver malformado, o `dotnet` falha com erro de JSON e o job falha da mesma forma.

### Component: job `backend` em `.github/workflows/ci.yml`

**Purpose:** compilar, testar com portão de cobertura e provar o build do backend sob um SDK fixado e um orçamento de tempo limitado.
**Interface:** job YAML com a sequência exata de passos:

1. `jobs.backend.timeout-minutes: 20`
2. `actions/checkout@v4`
3. `actions/setup-dotnet@v4` com `with: { global-json-file: global.json, cache: true, cache-dependency-path: '**/*.csproj' }`; a chave `dotnet-version` é removida
4. `dotnet restore Ciclo.sln`
5. `dotnet build Ciclo.sln --no-restore`
6. teste com cobertura e portão:
   `dotnet test tests/Ciclo.Api.Tests --logger "console;verbosity=normal" -p:CollectCoverage=true -p:CoverletOutputFormat=cobertura -p:Threshold=60 -p:ThresholdType=line -p:ThresholdStat=total`

O passo 6 não usa `--no-build`: `coverlet.msbuild` instrumenta os assemblies durante o build, e reaproveitar um build não instrumentado produziria relatório vazio.
**Internal Logic:** cada passo é um comando único; qualquer código de saída diferente de zero encerra o job naquele passo.
**Dependencies:** `global.json`, o projeto de teste do backend e o pacote `coverlet.msbuild`.
**Error Handling:** SDK indisponível falha no passo 3; restore falho falha no passo 4; erro de compilação falha no passo 5; teste reprovado ou cobertura abaixo de 60 falha no passo 6; job excedendo 20 minutos é cancelado e marcado como falho.

### Component: job `frontend` em `.github/workflows/ci.yml`

**Purpose:** instalar, lintar, testar com portão de cobertura e compilar o frontend sob um orçamento de tempo limitado.
**Interface:** job YAML com a sequência exata de passos:

1. `jobs.frontend.timeout-minutes: 15`
2. `actions/checkout@v4`
3. `actions/setup-node@v4` com `node-version: '24'`, `cache: npm`, `cache-dependency-path: frontend/package-lock.json`
4. `npm ci`
5. `npm run lint`
6. `npm test`
7. `npm run build`

Todos os passos de comando permanecem com `working-directory: frontend`.
**Internal Logic:** o passo 6 herda o portão de cobertura declarado no script `test`; nenhum passo adicional compara números.
**Dependencies:** `frontend/package.json` e `frontend/package-lock.json`.
**Error Handling:** lockfile divergente falha no passo 4; achado de lint falha no passo 5; teste reprovado ou cobertura abaixo de 60 falha no passo 6; erro de build falha no passo 7; job excedendo 15 minutos é cancelado e marcado como falho.

### Component: script `test` de `frontend/package.json`

**Purpose:** executar a suíte do frontend com cobertura nativa e limiar de 60% de linhas.
**Interface:** valor exato da chave `scripts.test`:

```
node --test --experimental-test-coverage --test-coverage-lines=60 "tests/**/*.test.ts"
```

**Internal Logic:** `node --test` descobre os arquivos pelo glob, mede a cobertura dos módulos carregados e compara o total de linhas com 60.
**Dependencies:** Node 24, já fixado no job.
**Error Handling:** cobertura de linha total abaixo de 60 ou nenhum arquivo de teste encontrado faz o processo terminar com código diferente de zero, e o job falha no passo `npm test`.

### Component: instrumentação no projeto de teste do backend

**Purpose:** permitir que o `dotnet test` meça cobertura e enforçe o limiar a partir de propriedades MSBuild.
**Interface:** uma linha acrescentada ao `ItemGroup` de `PackageReference` de `tests/Ciclo.Api.Tests/Ciclo.Api.Tests.csproj`:

```xml
<PackageReference Include="coverlet.msbuild" Version="6.0.4" />
```

A referência existente a `coverlet.collector` versão `6.0.4` permanece.
**Internal Logic:** as targets do pacote interceptam a execução do `VSTest` e instrumentam os assemblies carregados.
**Dependencies:** acesso ao feed NuGet no restore.
**Error Handling:** restore falho (feed indisponível) falha o job no passo de restore; não há execução com cobertura desativada por omissão silenciosa, porque as propriedades estão na linha de comando do passo de teste.

## Data Flow

Execução de CI disparada por `push` em `main` ou por `pull_request`:

1. GitHub Actions abre o run com grupo de concorrência `ci-<ref>` e `cancel-in-progress: true`.
2. Job `backend`: checkout lê o commit; `setup-dotnet` lê `global.json` e instala o SDK correspondente; `dotnet restore` popula o cache NuGet; `dotnet build` compila a solução; `dotnet test` compila o projeto de teste com instrumentação, executa a suíte, calcula a cobertura total de linha e compara com 60 pelo próprio MSBuild.
3. Job `frontend`: checkout; `setup-node` instala Node 24 com cache npm; `npm ci` instala a árvore exata do lockfile; `npm run lint` roda o lint; `npm test` executa a suíte com cobertura e compara o total de linhas com 60; `npm run build` compila a aplicação.
4. Cada job reporta sucesso apenas se todos os seus passos saírem com código zero dentro do `timeout-minutes` declarado. O run é verde somente quando os dois jobs são verdes.

## Data Structures

`global.json`:

| Campo | Tipo | Valor | Restrição |
|---|---|---|---|
| `sdk.version` | string | `10.0.100` | versão mínima exigida do SDK |
| `sdk.rollForward` | string | `latestFeature` | aceita feature bands superiores do mesmo major |

`frontend/package.json` → `scripts.test`: string única com as flags `--experimental-test-coverage` e `--test-coverage-lines=60` antes do glob `"tests/**/*.test.ts"`.

`.github/workflows/ci.yml`: `jobs.backend.timeout-minutes` = `20`; `jobs.frontend.timeout-minutes` = `15`; `jobs.backend.steps[setup-dotnet].with.global-json-file` = `global.json`.

Artefato de cobertura do backend: `tests/Ciclo.Api.Tests/coverage.cobertura.xml`, produzido por `-p:CoverletOutputFormat=cobertura` no caminho padrão do `coverlet.msbuild`. O arquivo é produzido mesmo em falha de limiar, para diagnóstico, e não é versionado.

## File / Module Layout

| Arquivo | Ação | Conteúdo |
|---|---|---|
| global.json | criar | Declaração do SDK fixado (`version`, `rollForward`) |
| .github/workflows/ci.yml | modificar | Setup do .NET por `global-json-file`; passo de teste do backend com cobertura e limiar; `timeout-minutes` nos dois jobs |
| frontend/package.json | modificar | Script `test` com cobertura nativa e limiar de 60% de linhas |
| tests/Ciclo.Api.Tests/Ciclo.Api.Tests.csproj | modificar | `PackageReference` de `coverlet.msbuild` versão `6.0.4` |

Nenhum diretório novo é criado. Nenhum arquivo de código de produto é tocado.

## Testing Strategy

- `tests/Ciclo.Api.Tests/EnrollmentJourneyTests.cs` — prova o fluxo ponta a ponta matrícula → documentos → aprovação (`EnrollWithRequiredDocument_SubmitApprove_ReachesApproved`) e passa a ser executado sob instrumentação de cobertura, provando que a instrumentação não quebra a suíte existente.
- `frontend/tests/deployConfig.test.ts` — prova que a configuração de build do frontend continua resolvível quando o script `test` recebe as flags de cobertura.
- Validação determinística do backend: `dotnet restore Ciclo.sln`, `dotnet build Ciclo.sln --no-restore` e o comando de teste com cobertura do passo 6, que falha se qualquer teste reprovar ou se a cobertura total de linha ficar abaixo de 60.
- Validação determinística do frontend, executada em `frontend`: `npm run lint`, `npm test` e `npm run build`; um total de linhas abaixo de 60 ou nenhum arquivo de teste encontrado faz `npm test` sair com código diferente de zero.
- Verificação estrutural dos portões: presença de `global-json-file`, `timeout-minutes` e `ThresholdStat=total` no workflow; presença de `coverlet.msbuild` no projeto de teste; presença das flags de cobertura no script `test`; ausência de `Skip=`, `test.skip`, `vitest` e `deploy` nos escopos declarados.
- Test-scope declaration: a prova executável desta spec é `tests/Ciclo.Api.Tests/EnrollmentJourneyTests.cs` na suíte do backend e o script `test` de `frontend/package.json` sobre `frontend/tests/`. As propriedades do workflow (SDK fixado, timeouts, limiar de cobertura) são verificadas pelos comandos declarados acima, não por um arquivo de teste novo.

## Migration / Rollback

- Migração: nenhuma migração de dados. A mudança entra em um commit único com os quatro arquivos; o primeiro run de CI no PR é a prova de aplicação.
- Se a cobertura medida ficar abaixo de 60 em qualquer um dos dois lados, o procedimento é acrescentar testes às suítes existentes (`tests/Ciclo.Api.Tests/` e `frontend/tests/`) até o limiar ser atingido; reduzir o limiar exigiria revisão rastreada da spec e não é feito nesta implementação.
- Rollback: reverter os quatro arquivos restaura o comportamento anterior (faixa `10.0.x`, testes sem limiar de cobertura, jobs sem limite de tempo). Nenhum estado persistido fica órfão, porque nada é gravado além do relatório de cobertura não versionado.
