<!-- PIPELINE-CONTRACT: Task completion is determined by "**Status:** [x] done" at the end of each "### Task N" section.
     Checkboxes "- [ ]" under "**Validation:**" are human verification guides — the pipeline ignores them completely.
     To mark a task as done, add "**Status:** [x] done". To mark it as pending, use "**Status:** [ ] pending". -->
# Spec 170: armazenamento-de-documentos — Implementation Tasks

## Planned Scaffolds

- `src/Ciclo.Infrastructure/Storage/S3FileStorage.cs`
- `src/Ciclo.Infrastructure/Storage/HashingReadStream.cs`
- `src/Ciclo.Infrastructure/Storage/OrphanFileScanner.cs`
- `tests/Ciclo.Api.Tests/FileStorageDriverTests.cs`
- `tests/Ciclo.Api.Tests/DocumentStorageScaleTests.cs`

## Task Order

Tasks MUST be executed sequentially in numerical order. Each task depends on the completion of all preceding tasks.

## Tasks

### Task 1: Contrato `IFileStorage` estendido e driver local com streaming

**Objective:** Estender o contrato de armazenamento com as cinco operações novas e reimplementar o driver local no contêiner em blocos, mantendo a criptografia AES-256-GCM, a rotação de chave e a leitura dos formatos legados.
**Requirements Covered:** FR-001, FR-002, NFR-001, NFR-003
**Design References:** Componentes `IFileStorage`, `LocalFileStorage` e `FileStorageOptions`; seção Data Structures (contêiner local v2 e v1); AD-170-2.
**Actions:**
1. Em `src/Ciclo.Infrastructure/Storage/IFileStorage.cs`, adicionar os membros `Task<bool> ExistsAsync(string filePath)`, `Task<Stream> OpenReadAsync(string filePath)`, `Task<string> GetSignedUrlAsync(string filePath, TimeSpan lifetime)`, `Task<string> SaveQuarantineAsync(Guid tenantId, string fileName, Stream content)` e `Task<IReadOnlyList<string>> ListKeysAsync(Guid? tenantId)`, mantendo as assinaturas atuais de `SaveAsync`, `GetAsync`, `DeleteAsync` e `ReencryptAsync`.
2. Em `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs`, renomear o marcador atual para `MagicV1`, adicionar `MagicV2 = "CICLOEN2"u8` e `ChunkSize = 65536`, e reescrever `SaveAsync` para gravar `MagicV2` seguido de registros `[int32 LE comprimento][nonce 12][tag 16][cifrado]`, lendo o stream de entrada em blocos com `AesGcm(_encryptionKeys[0], TagSize)`, sem buffer integral e apagando o arquivo parcial em caso de exceção.
3. Na mesma classe, implementar `DecryptingStream` como classe aninhada privada que decifra registro a registro sob demanda, usada por `OpenReadAsync`; manter `GetAsync` materializando o conteúdo e tratar os formatos `MagicV1` e sem marcador exatamente como descrito no design.
4. Implementar `ExistsAsync` (`File.Exists` sobre `ResolvePath`), `DeleteAsync` inalterado, `SaveQuarantineAsync` gravando em `{RootPath}/quarantine/{tenantId}/{Guid:N}_{nomeSanitizado}` com a mesma criptografia em blocos, `ListKeysAsync` enumerando os arquivos do escopo e ignorando o diretório `quarantine`, e `GetSignedUrlAsync` gerando `{PublicBaseUrl}/api/documents/signed/{epoch}.{base64url(hmac)}.{base64url(caminhoAbsoluto)}` com HMAC-SHA256 sobre `caminhoAbsoluto + "|" + epoch` e chave obtida de `FileStorageKey.Parse(options.Value.EncryptionKey)`. Lançar `ArgumentOutOfRangeException` quando `lifetime <= TimeSpan.Zero` e `InvalidOperationException` quando `PublicBaseUrl` está vazio.
5. Em `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs`, estender `FileStorageOptions` com `Provider` (padrão `"Local"`), `PublicBaseUrl`, `S3Bucket`, `S3ServiceUrl` e `S3ForcePathStyle`.
6. Em `tests/Ciclo.Api.Tests/TestDoubles.cs`, implementar os cinco membros novos em `NoopStorage` (lista vazia, `false`, stream vazio e string vazia) para que a suíte existente continue compilando.
**Expected Files:**
- `src/Ciclo.Infrastructure/Storage/IFileStorage.cs`
- `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs`
- `tests/Ciclo.Api.Tests/TestDoubles.cs`
**Validation:** `dotnet build` na solução sem erros; inspeção de que `LocalFileStorage` não possui mais o buffer `plainBuffer` e de que `IFileStorage` declara os nove membros. A prova de comportamento vem dos testes do Task 7.
**Status:** [x] done

### Task 2: Driver S3-compatível e seleção de provider

**Objective:** Implementar o driver S3-compatível sobre `IAmazonS3` e selecionar o driver por configuração, falhando na inicialização quando a configuração do provider escolhido está incompleta.
**Requirements Covered:** FR-003, NFR-003
**Design References:** Componentes `S3FileStorage` e `Program.cs`; AD-170-4.
**Actions:**
1. Adicionar a referência de pacote `AWSSDK.S3` (versão estável mais recente) em `src/Ciclo.Infrastructure/Ciclo.Infrastructure.csproj`.
2. Criar `src/Ciclo.Infrastructure/Storage/S3FileStorage.cs` com `public class S3FileStorage : IFileStorage` e construtor `(IAmazonS3 s3, IOptions<FileStorageOptions> options)`, implementando as nove operações com as chaves `{tenantId}/{studentId}/{Guid:N}_{nome}` e `quarantine/{tenantId}/{Guid:N}_{nome}`, `GetObjectMetadataAsync` para `ExistsAsync`, `GetPreSignedURL` para `GetSignedUrlAsync`, `ListObjectsV2Async` paginado sem o prefixo `quarantine/`, e `ReencryptAsync` como no-op que verifica a existência da chave.
3. Em `src/Ciclo.Api/Program.cs`, substituir o registro incondicional de `IFileStorage` por `LocalFileStorage` por um `switch` sobre `FileStorageOptions.Provider`, registrando `LocalFileStorage` no caso `Local` (exigindo `PublicBaseUrl`), `AmazonS3Client` com `AmazonS3Config` (`ServiceURL = S3ServiceUrl`, `ForcePathStyle = S3ForcePathStyle`) e `S3FileStorage` no caso `S3` (exigindo `S3Bucket`), e lançando `InvalidOperationException` para qualquer outro valor.
**Expected Files:**
- `src/Ciclo.Infrastructure/Storage/S3FileStorage.cs`
- `src/Ciclo.Infrastructure/Ciclo.Infrastructure.csproj`
- `src/Ciclo.Api/Program.cs`
**Validation:** `dotnet build` na solução sem erros; `S3FileStorage` presente no projeto e referenciado em `Program.cs`; a suíte existente continua verde com `Provider` ausente (assume `Local`).
**Status:** [x] done

### Task 3: Metadados de armazenamento, checksum, cota e quarentena

**Objective:** Persistir tamanho, hash SHA-256, MIME real e autor no documento, aplicar a cota por tenant antes da gravação e reter conteúdo suspeito em quarentena.
**Requirements Covered:** FR-004, FR-005, FR-006
**Design References:** Componentes `HashingReadStream`, `DocumentService` (extensão), `Document` e `Tenant`; AD-170-3, AD-170-6, AD-170-7.
**Actions:**
1. Criar `src/Ciclo.Infrastructure/Storage/HashingReadStream.cs` com `internal sealed class HashingReadStream : Stream` envolvendo um `IncrementalHash` SHA-256, expondo `BytesRead` e `HashHex` e lançando `InvalidDataException("file_too_large")` quando a leitura ultrapassa `maxBytes`; `CanSeek` falso, `Length` e `Position` lançando `NotSupportedException`, `Dispose` sem fechar o stream interno.
2. Em `src/Ciclo.Core/Entities/Document.cs`, adicionar `TamanhoBytes` (`long`), `HashSha256` (`string`, padrão `string.Empty`), `ContentType` (`string`, padrão `string.Empty`) e `EnviadoPorUsuarioId` (`Guid`). Em `src/Ciclo.Core/Entities/Tenant.cs`, adicionar `StorageQuotaBytes` (`long`, padrão `1_073_741_824`).
3. Em `src/Ciclo.Infrastructure/Contracts/DocumentDtos.cs`, adicionar `public sealed record SignedDownloadDto(string Url, DateTimeOffset ExpiresAt);` e os quatro campos novos em `DocumentDto`.
4. Em `src/Ciclo.Infrastructure/Services/DocumentService.cs`, adicionar o mapa privado `MimeByExtension` (`.pdf` para `application/pdf`, `.jpg` e `.jpeg` para `image/jpeg`, `.png` para `image/png`) e reescrever o trecho de persistência de `UploadAsync` na ordem: pesquisabilidade, extensão, assinatura com quarentena, limite de 10 MB, aluno, vínculo, tipo de documento, cota (`soma de TamanhoBytes + fileStream.Length` contra `Tenant.StorageQuotaBytes`, lançando `DocumentException("storage_quota_exceeded", 413)`), gravação via `HashingReadStream` e `Document` com os quatro campos novos. Capturar `InvalidDataException` do stream e converter em `DocumentException("file_too_large", 400)`.
5. No mesmo serviço, implementar a retenção em quarentena chamando `_storage.SaveQuarantineAsync(tenantId, fileName, fileStream)` a partir de `fileStream.Position = 0` dentro de `try/catch` que registra `_logger.LogError` e prossegue com a recusa `invalid_file_content`.
6. Gerar a migration com `dotnet ef migrations add AddDocumentStorageMetadata --project src/Ciclo.Infrastructure --startup-project src/Ciclo.Api`, com valor padrão em todas as colunas novas.
**Expected Files:**
- `src/Ciclo.Infrastructure/Storage/HashingReadStream.cs`
- `src/Ciclo.Core/Entities/Document.cs`
- `src/Ciclo.Core/Entities/Tenant.cs`
- `src/Ciclo.Infrastructure/Contracts/DocumentDtos.cs`
- `src/Ciclo.Infrastructure/Services/DocumentService.cs`
**Validation:** `dotnet build` sem erros e `dotnet test` sem regressão nos testes de documentos existentes (`DocumentComplianceTests`, `DocumentFilterTests`); a migration contém as cinco colunas novas com valor padrão.
**Status:** [x] done

### Task 4: Rotas de URL assinada no controlador de documentos

**Objective:** Emitir a URL assinada para download e servir o arquivo a partir do token, no controlador existente.
**Requirements Covered:** FR-007
**Design References:** Componentes `DocumentService` (extensão) e `DocumentController` (extensão); AD-170-5.
**Actions:**
1. Em `src/Ciclo.Infrastructure/Services/DocumentService.cs`, adicionar `Task<SignedDownloadDto> GetDownloadUrlAsync(Guid documentId, Guid tenantId, Guid userId, UserRole role)` em `IDocumentService`, extraindo de `DownloadAsync` o carregamento e a autorização para um método privado `LoadAuthorizedAsync(Guid documentId, Guid tenantId, Guid userId, UserRole role)` reutilizado pelos dois métodos, e devolvendo `new SignedDownloadDto(await _storage.GetSignedUrlAsync(document.CaminhoArquivo, TimeSpan.FromMinutes(5)), DateTimeOffset.UtcNow.AddMinutes(5))`.
2. Em `src/Ciclo.Api/Controllers/DocumentController.cs`, adicionar `[HttpGet("documents/{id:guid}/download-url")]` com `[Authorize(Roles = AccessPolicy.Roles.DocumentRead)]` mapeando `DocumentException` para `ProblemResponse.FromCode`, e `[HttpGet("documents/signed/{token}")]` com `[AllowAnonymous]` que decodifica o token no formato `{epoch}.{base64url(hmac)}.{base64url(caminho)}`, valida expiração, recomputa o HMAC com `FileStorageKey.Parse(FileStorage:EncryptionKey)` e compara com `CryptographicOperations.FixedTimeEquals`, abrindo o arquivo por `IFileStorage.OpenReadAsync` e devolvendo `File(stream, contentType, fileName)` com o MIME derivado da extensão.
3. Na rota assinada, mapear token malformado, HMAC divergente ou `UnauthorizedAccessException` para `signed_url_invalid` (403), expiração vencida para `signed_url_expired` (403) e arquivo ausente para `document_not_found` (404).
**Expected Files:**
- `src/Ciclo.Infrastructure/Services/DocumentService.cs`
- `src/Ciclo.Api/Controllers/DocumentController.cs`
**Validation:** `dotnet build` sem erros; `dotnet test` sem regressão nos testes de documentos; `GET api/documents/{id}/download-url` devolve `url` e `expiresAt` com validade de cinco minutos em execução local.
**Status:** [x] done

### Task 5: Download do frontend pela URL assinada

**Objective:** Fazer a tela de verificação baixar o documento pela URL assinada sem alterar o contrato de `Blob` consumido pela tela.
**Requirements Covered:** FR-007
**Design References:** Componente `Frontend de download`.
**Actions:**
1. Em `frontend/src/api/documents.ts`, adicionar `requestDownloadUrl(id: string): Promise<{ url: string; expiresAt: string }>` chamando `api.get('/api/documents/${id}/download-url')` e reescrever `downloadDocument(id: string): Promise<Blob>` para obter a URL assinada, buscar com `fetch`, lançar `Error('download_failed')` quando o status não for 2xx e devolver `await res.blob()`.
2. Em `frontend/src/mocks/handlers.ts`, adicionar o handler de `/api/documents/:id/download-url` devolvendo `{ url, expiresAt }` para que os fluxos de mock continuem respondendo.
**Expected Files:**
- `frontend/src/api/documents.ts`
- `frontend/src/mocks/handlers.ts`
**Validation:** `npm run build` em `frontend/` sem erros e `npm test` (se configurado) sem regressão; `frontend/src/pages/admin/DocumentVerification.tsx` permanece inalterado.
**Status:** [x] done

### Task 6: Varredura de órfãos e opção de linha de comando

**Objective:** Comparar as chaves gravadas com as referenciadas por `Document` e expor a varredura por opção de linha de comando, com remoção opcional.
**Requirements Covered:** FR-008
**Design References:** Componentes `OrphanFileScanner` e `Program.cs` (composição e linha de comando).
**Actions:**
1. Criar `src/Ciclo.Infrastructure/Storage/OrphanFileScanner.cs` com `public sealed record OrphanScanResult(int Scanned, int Orphaned, int Deleted, IReadOnlyList<string> Failures)` e `public class OrphanFileScanner` cujo construtor recebe `(IFileStorage storage, AppDbContext db, ILogger<OrphanFileScanner> logger)`, com `Task<OrphanScanResult> ScanAsync(Guid? tenantId, bool deleteOrphans)` calculando a diferença entre `ListKeysAsync(tenantId)` e as chaves de `Document.CaminhoArquivo` do escopo com `StringComparer.Ordinal`, removendo os órfãos por `DeleteAsync` quando solicitado e acumulando falhas em `Failures` sem interromper a varredura.
2. Em `src/Ciclo.Api/Program.cs`, registrar `OrphanFileScanner` no contêiner e adicionar, no padrão do bloco `--rotate-storage-key` existente, o tratamento de `args.Contains("--scan-orphan-files")` que resolve o scanner em escopo próprio, executa `ScanAsync(null, args.Contains("--delete"))`, imprime `Scanned`, `Orphaned`, `Deleted` e as falhas, e define `Environment.ExitCode` como `1` quando houver falhas e `0` caso contrário.
**Expected Files:**
- `src/Ciclo.Infrastructure/Storage/OrphanFileScanner.cs`
- `src/Ciclo.Api/Program.cs`
**Validation:** `dotnet build` sem erros; `dotnet run --project src/Ciclo.Api -- --scan-orphan-files` sobre uma raiz de armazenamento vazia imprime totais zerados e retorna código de saída `0`.
**Status:** [x] done

### Task 7: Testes dos drivers de armazenamento

**Objective:** Provar o comportamento dos dois drivers, incluindo formatos legados, falhas de arquivo, contenção de caminho, URL assinada e operações S3 com dublê.
**Requirements Covered:** FR-001, FR-002, FR-003, NFR-001
**Design References:** Seção Testing Strategy (primeiro bloco) e componentes `LocalFileStorage` e `S3FileStorage`.
**Actions:**
1. Criar `tests/Ciclo.Api.Tests/FileStorageDriverTests.cs` com xUnit e Moq cobrindo: ida e volta de 150000 bytes por `OpenReadAsync` e por `GetAsync`; gravação a partir de `Stream` somente-avanço sem `Length` nem `Position`; leitura de arquivo legado em texto claro e de arquivo `CICLOENC` produzido no teste com `AesGcm`; `ReencryptAsync` convertendo `CICLOENC` em `CICLOEN2` com conteúdo idêntico; `IOException` em arquivo truncado; `UnauthorizedAccessException` para caminho fora da raiz; `ExistsAsync` verdadeiro e falso; `ListKeysAsync` sem chaves de quarentena e lista vazia em diretório ausente; `GetSignedUrlAsync` com `PublicBaseUrl` e `ArgumentOutOfRangeException` para `lifetime` zero.
2. No mesmo arquivo, cobrir `S3FileStorage` com `Mock<IAmazonS3>`: `PutObject` com bucket e chave esperados, `GetObject` devolvendo conteúdo, 404 em `GetObjectMetadata` resultando em `false`, 404 em `GetObject` resultando em `FileNotFoundException`, `DeleteObject` chamado, `GetPreSignedURL` usado e `ListObjectsV2` paginando sem o prefixo `quarantine/`.
**Expected Files:**
- `tests/Ciclo.Api.Tests/FileStorageDriverTests.cs`
**Validation:** `dotnet test --filter FullyQualifiedName~FileStorageDriverTests` verde, sem acesso de rede.
**Status:** [x] done

### Task 8: Testes de upload, cota, quarentena e varredura de órfãos

**Objective:** Provar a persistência de metadados, o bloqueio por cota, a retenção em quarentena e a varredura de órfãos.
**Requirements Covered:** FR-004, FR-005, FR-006, FR-008, NFR-001, NFR-002
**Design References:** Seção Testing Strategy (segundo bloco) e componentes `DocumentService`, `HashingReadStream` e `OrphanFileScanner`.
**Actions:**
1. Criar `tests/Ciclo.Api.Tests/DocumentStorageScaleTests.cs` com xUnit, Moq e EF InMemory cobrindo: upload persistindo `HashSha256` igual ao SHA-256 calculado no teste, `TamanhoBytes`, `ContentType` e `EnviadoPorUsuarioId`; upload acima da cota devolvendo `storage_quota_exceeded` sem chamar `SaveAsync` no dublê; upload exatamente igual ao saldo restante aceito e um byte acima recusado; conteúdo com assinatura divergente chamando `SaveQuarantineAsync` e mantendo `invalid_file_content`; `OrphanFileScanner` relatando chave órfã, removendo com `deleteOrphans` verdadeiro, devolvendo resultado vazio em raiz inexistente e isolando falhas por chave.
**Expected Files:**
- `tests/Ciclo.Api.Tests/DocumentStorageScaleTests.cs`
**Validation:** `dotnet test --filter FullyQualifiedName~DocumentStorageScaleTests` verde e `dotnet test` completo sem regressão.
**Status:** [x] done
