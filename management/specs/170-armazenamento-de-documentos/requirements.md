---
name: armazenamento-de-documentos
status: verified
determinism: D
references: [V17]
---

# Spec 170: Armazenamento de Documentos em Escala — Requirements

## Determinism Classification

**Class:** D — Deterministic

Todas as capacidades desta spec produzem resultado observável e reproduzível a partir de entradas concretas: o contrato de armazenamento tem assinaturas fixas, o driver local grava bytes em formato definido e os lê de volta, o checksum é o SHA-256 do conteúdo, a cota é uma comparação inteira entre soma de bytes e limite, a URL assinada é um HMAC verificável e a varredura de órfãos é a diferença entre o conjunto de chaves gravadas e o conjunto de chaves referenciadas por `Document`. Nenhuma saída depende de julgamento de modelo de linguagem, portanto nenhuma asserção exige schema de saída gerada. O driver S3-compatível é verificado contra a interface `IAmazonS3` com dublê de teste, mantendo a verificação determinística e sem rede.

## Value Delivery

V17 — Armazenamento de Documentos em Escala: "Armazenamento plugável (disco local em desenvolvimento, S3-compatível em produção) com URL assinada, streaming sem carregar o arquivo inteiro em memória, checksum, verificação de tipo real, quarentena de arquivos suspeitos, varredura de órfãos e cota por escola. Prepara o volume real de documentos sem perder o controle de acesso."

**Delivered by:** Spec 170 (armazenamento-de-documentos)

## Functional Requirements

### FR-001: Contrato `IFileStorage` plugável estendido

**What:** O arquivo `src/Ciclo.Infrastructure/Storage/IFileStorage.cs` passa a declarar, além dos quatro membros existentes com assinatura inalterada (`SaveAsync`, `GetAsync`, `DeleteAsync`, `ReencryptAsync`), os membros `Task<bool> ExistsAsync(string filePath)`, `Task<Stream> OpenReadAsync(string filePath)`, `Task<string> GetSignedUrlAsync(string filePath, TimeSpan lifetime)`, `Task<string> SaveQuarantineAsync(Guid tenantId, string fileName, Stream content)` e `Task<IReadOnlyList<string>> ListKeysAsync(Guid? tenantId)`.
**Why:** V17 exige armazenamento plugável com put/get/delete/exists e URL assinada; sem um contrato único cada driver inventa a própria superfície e o serviço de documentos fica acoplado ao disco local.
**Acceptance Criteria:**
- [ ] A interface declara `ExistsAsync` com retorno `Task<bool>` e parâmetro único `string filePath`. [verify: grep-present `ExistsAsync` `src/Ciclo.Infrastructure/Storage/IFileStorage.cs`]
- [ ] A interface declara `OpenReadAsync` como membro público. [verify: grep-present `OpenReadAsync` `src/Ciclo.Infrastructure/Storage/IFileStorage.cs`]
- [ ] A interface declara `SaveQuarantineAsync` e `ListKeysAsync`. [verify: grep-present `SaveQuarantineAsync` `src/Ciclo.Infrastructure/Storage/IFileStorage.cs`]
- [ ] Os quatro membros preexistentes continuam declarados sem alteração de assinatura. [verify: grep-present `ReencryptAsync` `src/Ciclo.Infrastructure/Storage/IFileStorage.cs`]
- [ ] Toda implementação de `IFileStorage` no repositório compila e a solução passa na validação do projeto. [verify: project-validation]
**Failure modes:**
- Um driver que não implemente um dos membros novos quebra a compilação da solução; a alteração correspondente em `tests/Ciclo.Api.Tests/TestDoubles.cs` (`NoopStorage`) faz parte desta spec.
- `GetSignedUrlAsync` recebendo `lifetime` menor ou igual a zero lança `ArgumentOutOfRangeException` nos dois drivers.

### FR-002: Driver local com streaming e criptografia preservada

**What:** `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs` grava o arquivo em blocos de 65536 bytes de texto claro, cada bloco selado com AES-256-GCM pela chave atual do keyring, em arquivo de formato `[Magic "CICLOEN2" (8 bytes)][registros]`, onde cada registro é `[Comprimento do cifrado (int32 LE)][Nonce (12 bytes)][Tag (16 bytes)][Cifrado]`. `SaveAsync` grava a partir do stream de entrada sem materializar o conteúdo inteiro em memória, `OpenReadAsync` devolve um `Stream` que decifra bloco a bloco sob demanda e `GetAsync` mantém o retorno materializado.
**Why:** V17 exige streaming sem carregar o arquivo inteiro em memória, e o driver local deve preservar a criptografia atual (AES-256-GCM com keyring e rotação de chave).
**Acceptance Criteria:**
- [ ] O driver local grava arquivos no novo formato com o marcador mágico `CICLOEN2`. [verify: grep-present `CICLOEN2` `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs`]
- [ ] O driver local implementa `OpenReadAsync` retornando `Stream`. [verify: function-exists `OpenReadAsync` `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs`]
- [ ] A cifra permanece AES-256-GCM com comprimento de tag de 16 bytes. [verify: grep-present `AesGcm` `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs`]
- [ ] Arquivos no formato antigo `CICLOENC` e arquivos legados sem cabeçalho continuam legíveis por `GetAsync` e `OpenReadAsync`. [verify: project-validation]
- [ ] `ReencryptAsync` reescreve um arquivo do formato antigo no formato novo mantendo o conteúdo idêntico. [verify: project-validation]
**Failure modes:**
- Arquivo truncado no meio de um registro lança `IOException` na leitura; nenhum byte parcial decifrado é devolvido.
- Nenhuma chave do keyring decifra o bloco: permanece o `CryptographicException` com a mensagem de rotação de chave já existente.
- Caminho fora da raiz de armazenamento continua bloqueado por `UnauthorizedAccessException` em todos os membros que recebem `filePath`.

### FR-003: Driver S3-compatível selecionado por configuração

**What:** O novo arquivo `src/Ciclo.Infrastructure/Storage/S3FileStorage.cs` implementa `IFileStorage` sobre `IAmazonS3` com chaves `{tenantId}/{studentId}/{nomeUnico}`, quarentena sob `quarantine/{tenantId}/{nomeUnico}`, e `src/Ciclo.Api/Program.cs` registra a implementação conforme `FileStorage:Provider` (`Local`, valor padrão, ou `S3`).
**Why:** V17 exige armazenamento S3-compatível em produção sem alterar o serviço de documentos, que conhece apenas o contrato.
**Acceptance Criteria:**
- [ ] Existe a classe `S3FileStorage` implementando `IFileStorage`. [verify: file-exists `src/Ciclo.Infrastructure/Storage/S3FileStorage.cs`]
- [ ] O projeto `Ciclo.Infrastructure` referencia o pacote `AWSSDK.S3`. [verify: grep-present `AWSSDK.S3` `src/Ciclo.Infrastructure/Ciclo.Infrastructure.csproj`]
- [ ] `Program.cs` referencia `S3FileStorage` no registro condicional do provider. [verify: grep-present `S3FileStorage` `src/Ciclo.Infrastructure/Storage/S3FileStorage.cs`]
- [ ] As opções de armazenamento declaram `S3Bucket` para o driver S3. [verify: grep-present `S3Bucket` `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs`]
- [ ] O driver S3 é exercitado por teste com dublê de `IAmazonS3`, sem acesso de rede. [verify: project-validation]
**Failure modes:**
- `FileStorage:Provider` com valor desconhecido impede a inicialização da aplicação com exceção explícita que cita os valores aceitos.
- `FileStorage:Provider` igual a `S3` sem `S3Bucket` preenchido impede a inicialização da aplicação com exceção explícita.
- Objeto inexistente no bucket: `ExistsAsync` devolve `false` sem lançar e `OpenReadAsync` lança `FileNotFoundException`.
- Falha de rede do SDK é propagada como `AmazonS3Exception`, sem conversão silenciosa para sucesso.

### FR-004: Metadados de armazenamento no documento e checksum SHA-256

**What:** `src/Ciclo.Core/Entities/Document.cs` recebe as propriedades `TamanhoBytes` (`long`), `HashSha256` (`string`, 64 caracteres hexadecimais maiúsculos), `ContentType` (`string`) e `EnviadoPorUsuarioId` (`Guid`); `DocumentDto` passa a expor esses quatro campos; `DocumentService.UploadAsync` calcula o SHA-256 e o tamanho durante a cópia do stream e grava o MIME detectado por `FileSignature`; uma migration EF adiciona as quatro colunas.
**Why:** V17 exige checksum e verificação de tipo real persistidos para que o conteúdo gravado possa ser conferido sem baixá-lo de novo.
**Acceptance Criteria:**
- [ ] A entidade declara `HashSha256` e `ContentType`. [verify: grep-present `HashSha256` `src/Ciclo.Core/Entities/Document.cs`]
- [ ] O tamanho do arquivo é persistido em bytes na entidade. [verify: grep-present `TamanhoBytes` `src/Ciclo.Core/Entities/Document.cs`]
- [ ] O autor do upload é persistido na entidade. [verify: grep-present `EnviadoPorUsuarioId` `src/Ciclo.Core/Entities/Document.cs`]
- [ ] O DTO de documento expõe `HashSha256` ao consumidor da API. [verify: grep-present `HashSha256` `src/Ciclo.Core/Entities/Document.cs`]
- [ ] Uma migration EF adiciona as quatro colunas e a API continua respondendo o contrato de documento. [verify: project-validation]
**Failure modes:**
- Documento gravado antes da migration fica com `TamanhoBytes` zero e `HashSha256` vazio; o DTO devolve esses valores sem exceção.
- Falha no `SaveChangesAsync` dispara a compensação de arquivo já existente no serviço, sem deixar arquivo órfão novo.

### FR-005: Quarentena de arquivos suspeitos

**What:** Em `src/Ciclo.Infrastructure/Services/DocumentService.cs`, quando a assinatura real do conteúdo não corresponde à extensão declarada ou quando a extensão não está em `AllowedExtensions`, os bytes recebidos são persistidos por `IFileStorage.SaveQuarantineAsync` sob a área de quarentena antes de a resposta `invalid_file_content` (400) ser emitida; a resposta HTTP permanece a mesma do comportamento atual.
**Why:** V17 exige quarentena de arquivos suspeitos e o controle de acesso atual já recusa o arquivo; guardar os bytes permite análise posterior sem alterar o contrato da API.
**Acceptance Criteria:**
- [ ] O serviço de documentos persiste o conteúdo suspeito pela operação de quarentena do contrato. [verify: grep-present `SaveQuarantineAsync` `src/Ciclo.Infrastructure/Services/DocumentService.cs`]
- [ ] A recusa de conteúdo suspeito continua resultando no código de problema `invalid_file_content`. [verify: grep-present `invalid_file_content` `src/Ciclo.Infrastructure/Services/DocumentService.cs`]
- [ ] Arquivo em quarentena nunca é servido por download, porque a chave de quarentena não é gravada em nenhum `Document`. [verify: project-validation]
**Failure modes:**
- Falha ao gravar a quarentena é registrada em log e não altera a resposta 400 emitida ao cliente.
- Stream de entrada não pesquisável: a quarentena usa o buffer da assinatura já lido, sem exigir segunda passagem pelo stream original.

### FR-006: Cota de armazenamento por escola

**What:** A entidade `Tenant` recebe `StorageQuotaBytes` (`long`, inicializado com 1073741824) e `DocumentService.UploadAsync` recusa o upload com `DocumentException("storage_quota_exceeded", 413)` quando o tamanho recebido somado à soma de `Document.TamanhoBytes` do tenant excede `StorageQuotaBytes`; nenhum byte é gravado no driver nesse caso; a migration do FR-004 inclui a coluna.
**Why:** V17 exige cota por escola para operar o volume real de documentos com limite conhecido por tenant.
**Acceptance Criteria:**
- [ ] A entidade de tenant declara o limite de armazenamento em bytes. [verify: grep-present `StorageQuotaBytes`]
- [ ] O serviço de documentos emite o código `storage_quota_exceeded` quando o limite é excedido. [verify: grep-present `storage_quota_exceeded` `src/Ciclo.Infrastructure/Services/DocumentService.cs`]
- [ ] A verificação de cota ocorre antes da gravação no driver. [verify: project-validation]
**Failure modes:**
- Tenant com `StorageQuotaBytes` igual a zero recusa todos os uploads com `storage_quota_exceeded`.
- Upload exatamente igual ao saldo restante da cota é aceito; um byte acima é recusado.
- Dois uploads concorrentes que juntos excedem a cota podem ambos passar na verificação prévia; a verificação não é transacional e esse é o comportamento declarado.

### FR-007: URL assinada para download

**What:** `src/Ciclo.Api/Controllers/DocumentController.cs` ganha `GET api/documents/{id:guid}/download-url`, autorizado por `AccessPolicy.Roles.DocumentRead`, que reutiliza as validações de `DownloadAsync` e devolve `{ url, expiresAt }` com validade de 5 minutos; o driver local implementa `GetSignedUrlAsync` com token `base64url(HMAC-SHA256(FileStorage:EncryptionKey, filePath + "|" + expiraEmEpoch)) + "." + filePath + "." + expiraEmEpoch` servido por `GET api/documents/signed/{token}`; o driver S3 usa a URL pré-assinada do SDK; a URL do driver local é montada a partir de `FileStorage:PublicBaseUrl`.
**Why:** V17 exige URL assinada para download em escala sem expor a chave do arquivo nem manter a autorização em cada requisição de bytes.
**Acceptance Criteria:**
- [ ] O controlador de documentos declara a rota de emissão de URL assinada. [verify: grep-present `download-url` `src/Ciclo.Api/Controllers/DocumentController.cs`]
- [ ] O driver local implementa `GetSignedUrlAsync` com HMAC-SHA256. [verify: function-exists `GetSignedUrlAsync` `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs`]
- [ ] O controlador declara a rota que serve o arquivo a partir do token assinado. [verify: grep-present `documents/signed` `src/Ciclo.Api/Controllers/DocumentController.cs`]
- [ ] A extração do download no frontend usa a URL assinada devolvida pela API. [verify: project-validation]
**Failure modes:**
- Token com assinatura inválida devolve `signed_url_invalid` (403) e nenhum byte do arquivo.
- Token expirado devolve `signed_url_expired` (403).
- Token cujo caminho resolve fora da raiz de armazenamento devolve `signed_url_invalid` (403).
- Documento de outro tenant segue o comportamento atual de `document_not_found` (404) na emissão da URL.
- `FileStorage:PublicBaseUrl` ausente impede a inicialização da aplicação quando o provider é `Local`.

### FR-008: Varredura de órfãos

**What:** O novo arquivo `src/Ciclo.Infrastructure/Storage/OrphanFileScanner.cs` define `OrphanFileScanner` com `Task<OrphanScanResult> ScanAsync(Guid? tenantId, bool deleteOrphans)` que compara `IFileStorage.ListKeysAsync(tenantId)` com as chaves de `Document.CaminhoArquivo` do mesmo escopo, e `src/Ciclo.Api/Program.cs` trata a opção de linha de comando `--scan-orphan-files` (com `--delete` opcional), no mesmo padrão da opção de rotação de chave existente.
**Why:** V17 exige varredura de órfãos para que arquivos sem registro não cresçam sem controle.
**Acceptance Criteria:**
- [ ] Existe a classe `OrphanFileScanner` no projeto de infraestrutura. [verify: file-exists `src/Ciclo.Infrastructure/Storage/OrphanFileScanner.cs`]
- [ ] A varredura expõe a operação `ScanAsync`. [verify: function-exists `ScanAsync` `src/Ciclo.Infrastructure/Storage/OrphanFileScanner.cs`]
- [ ] `Program.cs` trata a opção `--scan-orphan-files`. [verify: grep-present `scan-orphan-files` `src/Ciclo.Api/Program.cs`]
- [ ] Sem `--delete`, a varredura apenas relata as chaves órfãs e retorna código de saída zero. [verify: project-validation]
**Failure modes:**
- Diretório ou prefixo inexistente na raiz de armazenamento produz resultado vazio, sem exceção.
- Arquivo sem registro `Document` com `--delete` é removido e contabilizado no total de removidos.
- Falha de leitura de uma chave é registrada em log, a varredura continua e o código de saída é diferente de zero.

## Non-Functional Requirements

### NFR-001: Nenhum buffering integral do arquivo no caminho de gravação

**What:** O caminho de upload não pode exigir que o conteúdo inteiro esteja em memória nem que o stream de entrada seja pesquisável ou tenha `Length` disponível para gravar no driver local.
**Acceptance Criteria:**
- [ ] O driver local expõe a leitura por stream em `OpenReadAsync`, sem exigir o conteúdo inteiro em memória. [verify: function-exists `OpenReadAsync` `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs`]
- [ ] Um stream somente-avanço, sem `Length` e sem `Position`, é gravado com sucesso e relido com o mesmo conteúdo. [verify: project-validation]
**Failure modes:**
- Stream que falha durante a leitura propaga a exceção original e remove o arquivo parcialmente gravado.

### NFR-002: Controle de acesso e criptografia preservados

**What:** O download continua exigindo `AccessPolicy.Roles.DocumentRead` e o conteúdo em disco continua cifrado; nenhuma rota nova serve bytes sem autorização explícita ou token assinado válido.
**Acceptance Criteria:**
- [ ] A rota de download mantém a exigência de papel de leitura de documentos. [verify: grep-present `AccessPolicy.Roles.DocumentRead` `src/Ciclo.Api/Controllers/DocumentController.cs`]
- [ ] O conteúdo gravado pelo driver local permanece cifrado com AES-256-GCM. [verify: grep-present `AesGcm` `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs`]
- [ ] O limite de 10 MB por upload continua aplicado pelo serviço de documentos. [verify: grep-present `MaxFileSize` `src/Ciclo.Infrastructure/Services/DocumentService.cs`]
**Failure modes:**
- Requisição sem token ou papel exigido segue devolvendo 401/403 do middleware de autenticação existente.

### NFR-003: Seleção de provider verifica a configuração na inicialização

**What:** `FileStorageOptions` declara `Provider`, `S3Bucket`, `S3ServiceUrl`, `S3ForcePathStyle` e `PublicBaseUrl`, e a aplicação falha na inicialização quando a configuração do provider escolhido está incompleta.
**Acceptance Criteria:**
- [ ] As opções de armazenamento declaram o seletor de provider e as opções do driver S3. [verify: grep-present `S3ForcePathStyle` `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs`]
- [ ] A inicialização falha com mensagem explícita quando a configuração do provider está incompleta. [verify: project-validation]
**Failure modes:**
- Provider ausente na configuração assume `Local`.

## Constraints

- Os quatro membros preexistentes de `IFileStorage` mantêm nome e assinatura; nenhum consumidor atual precisa mudar. [verify: grep-present `SaveAsync` `src/Ciclo.Infrastructure/Storage/IFileStorage.cs`]
- A detecção de tipo real continua sendo feita por `FileSignature` antes da gravação. [verify: function-exists `DetectAsync`]
- A proteção contra travessia de caminho continua existindo no driver local. [verify: grep-present `ResolvePath` `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs`]
- O dublê `NoopStorage` de `tests/Ciclo.Api.Tests/TestDoubles.cs` implementa os membros novos do contrato para que os testes existentes compilem. [verify: grep-present `NoopStorage` `tests/Ciclo.Api.Tests/TestDoubles.cs`]
- Nenhum teste abre conexão de rede; o driver S3 é exercitado com dublê de `IAmazonS3`. [verify: project-validation]
- A validação completa do projeto passa após as alterações. [verify: project-validation]

## Edge Cases & Error States

- Upload maior que o saldo de cota disponível: nada é gravado e a resposta carrega `storage_quota_exceeded` (413).
- Upload exatamente igual ao saldo restante: aceito.
- Conteúdo suspeito: recusa 400 `invalid_file_content` e bytes retidos em quarentena.
- Arquivo legado sem cabeçalho de cifra: lido como texto claro, sem exceção.
- Arquivo legado `CICLOENC`: decifrado pelo caminho antigo e regravado no formato novo por `ReencryptAsync`.
- Arquivo do formato novo truncado: `IOException` na leitura, sem entrega parcial.
- Token assinado adulterado, expirado ou com caminho fora da raiz: 403 com o código correspondente.
- Documento inexistente, de outro tenant ou de aluno sem vínculo: mantém o comportamento atual de `document_not_found` ou de acesso negado na emissão da URL.
- Varredura de órfãos em raiz vazia: zero órfãos, código de saída zero.
- Varredura de órfãos com `--delete` e arquivo em uso: a falha daquela chave é registrada e a varredura prossegue.
- Tenant sem documento algum: soma de cota igual a zero, upload aceito até o limite.

## Anti-Scope

- Nenhum estado novo é adicionado ao ciclo de vida do documento. [verify: grep-absent `Quarentena` `src/Ciclo.Core/Entities/Document.cs`]
- Nenhum controlador novo é criado para armazenamento; as rotas novas ficam no controle de documentos existente. [verify: file-absent `src/Ciclo.Api/Controllers/StorageController.cs`]
- Nenhum motor de antivírus é integrado; a quarentena retém bytes suspeitos segundo a checagem de assinatura já existente. [verify: file-absent `src/Ciclo.Infrastructure/Storage/VirusScanner.cs`]
- Nenhum driver paralelo de buffering integral é criado; o streaming é implementado no driver local existente. [verify: file-absent `src/Ciclo.Infrastructure/Storage/BufferedFileStorage.cs`]
- A rota de upload existente não é duplicada nem renomeada. [verify: grep-absent `documents/upload-v2` `src/Ciclo.Api/Controllers/DocumentController.cs`]

## Dependencies

- `src/Ciclo.Infrastructure/Storage/IFileStorage.cs`, `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs` e `src/Ciclo.Infrastructure/Services/DocumentService.cs`, que já concentram o domínio de armazenamento e são estendidos por esta spec.
- `FileSignature` (detecção de assinatura real) e `DocumentException` (códigos de problema), já usados pelo serviço de documentos.
- `Tenant` e `ITenantScoped`, que definem o isolamento por escola.
- Pacote NuGet `AWSSDK.S3` para o driver S3-compatível e EF Core para a migration das colunas novas.
- V17 de `management/vision.md`, que define o resultado entregue.

## Out of Scope

- Agendador em segundo plano para a varredura de órfãos; a execução é por opção de linha de comando.
- Tela administrativa de cota ou de arquivos em quarentena.
- Exclusão automática de arquivos em quarentena por idade.
- CDN, cache de borda ou replicação entre regiões.
- Cota por usuário ou por aluno; a cota é por tenant.
- Migração em lote dos arquivos existentes para o formato novo além do que `ReencryptAsync` já faz por arquivo.

## Test Scope

- `tests/Ciclo.Api.Tests/FileStorageDriverTests.cs` — driver local (gravação e leitura em blocos, formatos legados, travessia de caminho, arquivo truncado, `ReencryptAsync`), driver S3 com dublê de `IAmazonS3` e verificação de URL assinada.
- `tests/Ciclo.Api.Tests/DocumentStorageScaleTests.cs` — upload com checksum e metadados, cota por tenant, quarentena de conteúdo suspeito e varredura de órfãos.
- `tests/Ciclo.Api.Tests/TestDoubles.cs` — atualização do dublê `NoopStorage` para o contrato estendido.
