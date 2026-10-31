# Spec 170: armazenamento-de-documentos — Design

## Design Approach

A spec estende o dono já existente do domínio de armazenamento (`IFileStorage` + `LocalFileStorage`) em vez de criar uma camada paralela. O contrato ganha as operações que faltam (existência, leitura em stream, URL assinada, quarentena, listagem) e passa a ter dois drivers selecionados por configuração. O serviço de documentos (`DocumentService`) continua sendo o único ponto que orquestra validação, cota, criptografia, checksum e persistência; os drivers só movem bytes.

Três decisões de forma sustentam o streaming sem buffering integral:

1. O arquivo local passa a ser gravado em blocos de 65536 bytes de texto claro, cada bloco selado com AES-256-GCM pela chave atual do keyring, em um contêiner versionado por marcador mágico. O formato antigo (texto claro e `CICLOENC`) continua legível, então não há migração obrigatória de dados.
2. O checksum e o tamanho são calculados por um `Stream` decorador (`HashingReadStream`) que o driver consome enquanto grava; nenhuma segunda passagem pelo conteúdo é necessária e o stream de entrada não precisa ser pesquisável para o cálculo.
3. A URL assinada do driver local é um HMAC-SHA256 sobre o caminho e o instante de expiração, servido por uma rota do controlador de documentos existente; o driver S3 delega a assinatura ao SDK.

A quarentena não cria estado novo no ciclo de vida do documento: os bytes suspeitos são retidos em área separada do driver e a resposta HTTP de recusa permanece a atual.

## Architecture Decisions

- **AD-170-1 — Contrato único, dois drivers.** `IFileStorage` permanece a única abstração conhecida pelo serviço de documentos. A escolha do driver acontece no registro de dependências em `Program.cs`, não dentro do serviço de documentos.
- **AD-170-2 — Formato local versionado e retrocompatível.** O novo formato usa o marcador `CICLOEN2` com registros de bloco. Arquivos `CICLOENC` (v1) e arquivos sem cabeçalho continuam legíveis; `ReencryptAsync` converte v1 em v2. Nenhuma migração de dados é obrigatória para a aplicação continuar correta.
- **AD-170-3 — Checksum no caminho de escrita.** `HashingReadStream` decora o stream de entrada e calcula SHA-256 enquanto o driver grava. Isso evita ler o arquivo duas vezes e mantém a garantia de não materializar o conteúdo.
- **AD-170-4 — Provider S3 via `AWSSDK.S3` e `IAmazonS3`.** O driver depende da interface `IAmazonS3`, o que permite verificação determinística com dublê de teste sem rede. `S3ServiceUrl` e `S3ForcePathStyle` habilitam provedores S3-compatíveis que não são a AWS.
- **AD-170-5 — URL assinada servida pelo controlador existente.** Nenhum controlador novo. A rota `documents/{id}/download-url` emite a URL e a rota `documents/signed/{token}` serve os bytes do driver local. O token assinado é a autorização da segunda rota; a primeira mantém a autorização por papel e vínculo já existentes.
- **AD-170-6 — Cota verificada antes da gravação.** A soma de `Document.TamanhoBytes` do tenant é comparada com `Tenant.StorageQuotaBytes` antes de qualquer byte ser gravado no driver. A verificação não é transacional; concorrência pode permitir ultrapassar a cota em um upload simultâneo, e esse é o comportamento declarado.
- **AD-170-7 — Quarentena fora do ciclo de vida.** Arquivos suspeitos são gravados sob o prefixo `quarantine/` do driver e não geram linha em `Document`; a varredura de órfãos ignora esse prefixo em `ListKeysAsync` para nunca removê-los.

## Component / Module Breakdown

### Component: Contrato `IFileStorage`

**Purpose:** definir a superfície única de armazenamento consumida pelo serviço de documentos.
**Interface:** `src/Ciclo.Infrastructure/Storage/IFileStorage.cs`

```csharp
public interface IFileStorage
{
    Task<string> SaveAsync(Guid tenantId, Guid studentId, string fileName, Stream content);
    Task<Stream> GetAsync(string filePath);
    Task DeleteAsync(string filePath);
    Task ReencryptAsync(string filePath);
    Task<bool> ExistsAsync(string filePath);
    Task<Stream> OpenReadAsync(string filePath);
    Task<string> GetSignedUrlAsync(string filePath, TimeSpan lifetime);
    Task<string> SaveQuarantineAsync(Guid tenantId, string fileName, Stream content);
    Task<IReadOnlyList<string>> ListKeysAsync(Guid? tenantId);
}
```

Regra de representação de chave: o valor devolvido por `SaveAsync`/`SaveQuarantineAsync` é exatamente o valor aceito por `GetAsync`, `OpenReadAsync`, `ExistsAsync`, `DeleteAsync`, `ReencryptAsync`, `GetSignedUrlAsync` e é o valor devolvido por `ListKeysAsync`. O driver local usa o caminho absoluto; o driver S3 usa a chave do objeto.
**Internal Logic:** contrato sem lógica; toda implementação é obrigada a honrar a representação de chave acima.
**Dependencies:** nenhuma além de `System.IO`.
**Error Handling:** `GetSignedUrlAsync` lança `ArgumentOutOfRangeException` quando `lifetime <= TimeSpan.Zero` em ambas as implementações.

### Component: `LocalFileStorage`

**Purpose:** driver de disco local com criptografia AES-256-GCM em blocos, retrocompatível com os formatos anteriores.
**Interface:** `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs`

```csharp
public class LocalFileStorage : IFileStorage
{
    public LocalFileStorage(IOptions<FileStorageOptions> options);
    public Task<string> SaveAsync(Guid tenantId, Guid studentId, string fileName, Stream content);
    public Task<Stream> GetAsync(string filePath);
    public Task DeleteAsync(string filePath);
    public Task ReencryptAsync(string filePath);
    public Task<bool> ExistsAsync(string filePath);
    public Task<Stream> OpenReadAsync(string filePath);
    public Task<string> GetSignedUrlAsync(string filePath, TimeSpan lifetime);
    public Task<string> SaveQuarantineAsync(Guid tenantId, string fileName, Stream content);
    public Task<IReadOnlyList<string>> ListKeysAsync(Guid? tenantId);
}
```

Constantes: `MagicV2 = "CICLOEN2"u8`, `MagicV1 = "CICLOENC"u8` (o marcador atual passa a ser nomeado como v1), `NonceSize = 12`, `TagSize = 16`, `ChunkSize = 65536`.

**Internal Logic — gravação:**
1. Sanitiza o nome com `SanitizeFileName` (regra atual) e monta `{root}/{tenantId}/{studentId}/{Guid:N}_{nomeSanitizado}`.
2. Abre `FileStream` de escrita no destino; grava `MagicV2`.
3. Lê o stream de entrada em laços de até `ChunkSize` bytes (acumulando leituras parciais até completar o bloco ou atingir o fim).
4. Para cada bloco não vazio: gera `nonce` de 12 bytes com `RandomNumberGenerator.Fill`, cifra com `new AesGcm(_encryptionKeys[0], TagSize)`, grava `[int32 LE comprimento][nonce][tag][cifrado]`.
5. Fecha o `FileStream` e devolve o caminho absoluto.
6. Em qualquer exceção, fecha o stream, apaga o arquivo parcial e propaga a exceção original.

**Internal Logic — leitura:** `OpenReadAsync` abre `FileStream` do destino e devolve `DecryptingStream` (classe aninhada privada) que lê o marcador e decifra registro a registro sob demanda, mantendo em memória apenas o bloco corrente. Para arquivos sem marcador (legado em texto claro) devolve o `FileStream` sem decodificação. Para `CICLOENC` (v1) decifra o conteúdo único com o keyring e devolve `MemoryStream` do texto claro. `GetAsync` mantém o comportamento atual: materializa o conteúdo chamando a mesma decodificação e devolvendo `MemoryStream`.
**Dependencies:** `Microsoft.Extensions.Options`, `System.Security.Cryptography`, `FileStorageKey.Parse` (existente) para o keyring.
**Error Handling:**
- Arquivo ausente: `FileNotFoundException` em `GetAsync`, `OpenReadAsync` e `ReencryptAsync`; `ExistsAsync` devolve `false`; `DeleteAsync` não lança.
- Registro truncado (`comprimento` maior que os bytes restantes ou menor que zero): `IOException("Corrupted storage chunk.")`.
- Nenhuma chave decifra um bloco: mantém o `CryptographicException` com a mensagem de rotação já existente.
- Caminho fora da raiz: `UnauthorizedAccessException` via `ResolvePath`.
- `SaveQuarantineAsync`: destino `{root}/quarantine/{tenantId}/{Guid:N}_{nomeSanitizado}`; mesma criptografia em blocos; devolve o caminho absoluto.
- `ListKeysAsync`: enumera `{root}/{tenantId}/...` (ou `{root}/...` quando `tenantId` é nulo), ignorando o diretório `{root}/quarantine`; diretório inexistente devolve lista vazia.
- `GetSignedUrlAsync`: exige `PublicBaseUrl` preenchido, senão `InvalidOperationException("FileStorage:PublicBaseUrl is required for the local provider.")`; devolve `{PublicBaseUrl}/api/documents/signed/{token}`.

### Component: `HashingReadStream`

**Purpose:** calcular SHA-256 e tamanho enquanto o conteúdo é consumido pelo driver, sem exigir stream pesquisável nem materializar o arquivo.
**Interface:** `src/Ciclo.Infrastructure/Storage/HashingReadStream.cs`

```csharp
internal sealed class HashingReadStream : Stream
{
    public HashingReadStream(Stream inner, long? maxBytes = null);
    public long BytesRead { get; }
    public string HashHex { get; }   // 64 caracteres hexadecimais maiúsculos
    public override int Read(byte[] buffer, int offset, int count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);
    // demais membros de Stream delegados; CanSeek = false, Length e Position lançam NotSupportedException
}
```

**Internal Logic:** envolve um `IncrementalHash` SHA-256; cada leitura bem-sucedida alimenta o hash e incrementa `BytesRead`; `HashHex` é `Convert.ToHexString(hash)` calculado sob demanda após o fim do stream.
**Dependencies:** `System.Security.Cryptography.IncrementalHash`.
**Error Handling:** quando `maxBytes` está definido e a leitura faria `BytesRead` ultrapassar o limite, lança `InvalidDataException("file_too_large")` antes de consumir o excedente. `Dispose` não fecha o stream interno (o dono é o chamador).

### Component: `S3FileStorage`

**Purpose:** driver S3-compatível para produção.
**Interface:** `src/Ciclo.Infrastructure/Storage/S3FileStorage.cs`

```csharp
public class S3FileStorage : IFileStorage
{
    public S3FileStorage(IAmazonS3 s3, IOptions<FileStorageOptions> options);
    // mesmas nove operações de IFileStorage
}
```

**Internal Logic:**
- Chave de documento: `{tenantId}/{studentId}/{Guid:N}_{nomeSanitizado}`.
- `SaveAsync`: `PutObjectAsync` com `InputStream` posicionado em 0 e `AutoCloseStream = false`, `ContentType = application/octet-stream`. Não usa `ContentLength` obrigatório: quando o stream é pesquisável envia `Length`, quando não é envia `null`.
- `GetAsync`: `GetObjectAsync`; em sucesso materializa o response stream em `MemoryStream`.
- `OpenReadAsync`: `GetObjectAsync` e devolve `response.ResponseStream` diretamente.
- `ExistsAsync`: `GetObjectMetadataAsync`; `AmazonS3Exception` com `StatusCode == HttpStatusCode.NotFound` devolve `false`; outras exceções propagam.
- `DeleteAsync`: `DeleteObjectAsync` sem lançar quando a chave não existe.
- `ReencryptAsync`: no-op documentado — a criptografia do S3 é responsabilidade do provedor de objetos; o método apenas verifica existência e devolve.
- `SaveQuarantineAsync`: chave `quarantine/{tenantId}/{Guid:N}_{nomeSanitizado}`.
- `GetSignedUrlAsync`: `GetPreSignedURL(new GetPreSignedUrlRequest { BucketName = _bucket, Key = filePath, Verb = HttpVerb.GET, Expires = DateTime.UtcNow.Add(lifetime) })`.
- `ListKeysAsync`: `ListObjectsV2Async` com `Prefix = tenantId is null ? "" : $"{tenantId}/"`, paginando por `NextContinuationToken`, ignorando chaves que começam com `quarantine/`.
**Dependencies:** `AWSSDK.S3` (`IAmazonS3`, `AmazonS3Exception`, `GetPreSignedUrlRequest`, `HttpVerb`).
**Error Handling:** `AmazonS3Exception` não originada de 404 é propagada sem conversão; `GetAsync`/`OpenReadAsync` com 404 lançam `FileNotFoundException`.

### Component: `FileStorageOptions`

**Purpose:** configurar os dois drivers e o seletor de provider.
**Interface:** `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs` (mesmo arquivo que já declara a classe)

```csharp
public class FileStorageOptions
{
    public const string SectionName = "FileStorage";
    public string RootPath { get; set; } = "uploads";
    public string? EncryptionKey { get; set; }
    public string[] PreviousEncryptionKeys { get; set; } = [];
    public string Provider { get; set; } = "Local";
    public string? PublicBaseUrl { get; set; }
    public string? S3Bucket { get; set; }
    public string? S3ServiceUrl { get; set; }
    public bool S3ForcePathStyle { get; set; }
}
```

**Internal Logic:** apenas dados de configuração; a validação do provider ocorre em `Program.cs`.
**Error Handling:** nenhuma dentro da classe; valores ausentes são validados no registro de dependências.

### Component: `DocumentService` (extensão)

**Purpose:** calcular checksum e tamanho no upload, persistir metadados, aplicar cota, reter conteúdo suspeito em quarentena e emitir URL assinada.
**Interface:** `src/Ciclo.Infrastructure/Services/DocumentService.cs`

```csharp
public interface IDocumentService
{
    // assinaturas atuais preservadas
    Task<SignedDownloadDto> GetDownloadUrlAsync(Guid documentId, Guid tenantId, Guid userId, UserRole role);
}
```

Assinatura de upload inalterada: `UploadAsync(Stream fileStream, string fileName, string contentType, Guid studentId, Guid documentTypeId, Guid tenantId, Guid userId, UserRole role)`.

**Internal Logic — `UploadAsync`, ordem exata:**
1. `fileStream.CanSeek` falso: `DocumentException("invalid_file_content", 400)` (regra atual preservada).
2. Extensão fora de `AllowedExtensions`: `DocumentException("invalid_extension", 400)`.
3. `FileSignature.DetectAsync` + `MatchesExtension`: divergência ⇒ quarentena e `DocumentException("invalid_file_content", 400)`.
4. `fileStream.Length > MaxFileSize`: `DocumentException("file_too_large", 400)`.
5. Aluno, vínculo e tipo de documento: mesmas validações e códigos atuais.
6. Cota: `var used = await _db.Documents.Where(d => d.TenantId == tenantId).SumAsync(d => d.TamanhoBytes);` e `var quota = await _db.Tenants.Where(t => t.Id == tenantId).Select(t => t.StorageQuotaBytes).FirstOrDefaultAsync();`; se `used + fileStream.Length > quota` ⇒ `DocumentException("storage_quota_exceeded", 413)`.
7. `fileStream.Position = 0`; `await using var hashing = new HashingReadStream(fileStream, MaxFileSize);` e `var key = await _storage.SaveAsync(tenantId, studentId, fileName, hashing);`.
8. `Document` recebe `CaminhoArquivo = key`, `TamanhoBytes = hashing.BytesRead`, `HashSha256 = hashing.HashHex`, `ContentType = MimeByExtension[ext]`, `EnviadoPorUsuarioId = userId`, mais os campos atuais.
9. `SaveChangesAsync` mantém a compensação atual (remover o arquivo quando o insert falha).

Quarentena: `SaveQuarantineAsync` recebe uma cópia do stream a partir de `Position = 0` (o stream é pesquisável por construção, pela regra 1); a chamada acontece dentro de `try/catch (Exception ex)` que registra `_logger.LogError` e prossegue com a exceção de recusa original.

Mapa de MIME privado: `.pdf` ⇒ `application/pdf`; `.jpg` e `.jpeg` ⇒ `image/jpeg`; `.png` ⇒ `image/png`.

`GetDownloadUrlAsync`: carrega e autoriza o documento exatamente como `DownloadAsync` (mesmo filtro por `TenantId`, mesma checagem de vínculo do aluno), chama `_storage.GetSignedUrlAsync(document.CaminhoArquivo, TimeSpan.FromMinutes(5))` e devolve `new SignedDownloadDto(url, DateTimeOffset.UtcNow.AddMinutes(5))`. A carga e a autorização são extraídas para um método privado `LoadAuthorizedAsync(Guid documentId, Guid tenantId, Guid userId, UserRole role)` usado por `DownloadAsync` e por `GetDownloadUrlAsync`.
**Dependencies:** `AppDbContext`, `IFileStorage`, `IAuditTrail`, `INotificationService`, `FileSignature`, `HashingReadStream`.
**Error Handling:** `InvalidDataException("file_too_large")` vinda do `HashingReadStream` é capturada em `UploadAsync` e convertida em `DocumentException("file_too_large", 400)` (o arquivo parcial já é removido pelo driver). Documento inexistente ou de outro tenant: `DocumentException("document_not_found", 404)`. Sem vínculo com o aluno: o mesmo código de acesso negado já emitido por `StudentAccess`.

### Component: `DocumentController` (extensão)

**Purpose:** expor a emissão de URL assinada e servir o arquivo a partir do token.
**Interface:** `src/Ciclo.Api/Controllers/DocumentController.cs`

```csharp
[HttpGet("documents/{id:guid}/download-url")]
[Authorize(Roles = AccessPolicy.Roles.DocumentRead)]
public async Task<IActionResult> GetDownloadUrl(Guid id);

[HttpGet("documents/signed/{token}")]
[AllowAnonymous]
public async Task<IActionResult> DownloadSigned(string token);
```

**Internal Logic:** `GetDownloadUrl` extrai usuário, papel e tenant como as demais ações e devolve `Ok(new SignedDownloadDto(url, expiresAt))`. `DownloadSigned` decodifica o token no formato `{epoch}.{base64url(hmac)}.{base64url(filePath)}`, verifica `epoch` contra `DateTimeOffset.UtcNow.ToUnixTimeSeconds()`, recalcula o HMAC sobre `filePath + "|" + epoch` e compara com `CryptographicOperations.FixedTimeEquals`, então abre o arquivo com `IFileStorage.OpenReadAsync` e devolve `File(stream, contentType, fileName)`.
**Dependencies:** `IDocumentService`, `IFileStorage`, `IOptions<FileStorageOptions>`.
**Error Handling:** token malformado ou com HMAC divergente ⇒ `ProblemResponse.FromCode("signed_url_invalid", 403)`; token expirado ⇒ `signed_url_expired` (403); arquivo ausente ⇒ `document_not_found` (404); `UnauthorizedAccessException` do driver ⇒ `signed_url_invalid` (403).

### Component: `OrphanFileScanner`

**Purpose:** encontrar e, quando solicitado, remover arquivos sem registro `Document`.
**Interface:** `src/Ciclo.Infrastructure/Storage/OrphanFileScanner.cs`

```csharp
public sealed record OrphanScanResult(int Scanned, int Orphaned, int Deleted, IReadOnlyList<string> Failures);

public class OrphanFileScanner
{
    public OrphanFileScanner(IFileStorage storage, AppDbContext db, ILogger<OrphanFileScanner> logger);
    public Task<OrphanScanResult> ScanAsync(Guid? tenantId, bool deleteOrphans);
}
```

**Internal Logic:** `keys = await _storage.ListKeysAsync(tenantId)`; `referenced = await _db.Documents.Where(d => tenantId == null || d.TenantId == tenantId).Select(d => d.CaminhoArquivo).ToHashSetAsync()`; órfãos = chaves não presentes em `referenced` (comparação `StringComparer.Ordinal`). Com `deleteOrphans`, cada órfão é removido por `DeleteAsync`; falhas são registradas em log e acumuladas em `Failures`.
**Dependencies:** `IFileStorage`, `AppDbContext`, `ILogger<OrphanFileScanner>`.
**Error Handling:** raiz inexistente ou lista vazia ⇒ `OrphanScanResult(0, 0, 0, [])`. Falha por chave é isolada e não interrompe a varredura.

### Component: `Program.cs` (composição e linha de comando)

**Purpose:** selecionar o driver e executar a varredura por linha de comando.
**Interface:** `src/Ciclo.Api/Program.cs`

```csharp
// registro do driver, substituindo a linha 173 atual
var fileStorage = builder.Configuration.GetSection(FileStorageOptions.SectionName).Get<FileStorageOptions>() ?? new FileStorageOptions();
switch (fileStorage.Provider)
{
    case "Local":
        if (string.IsNullOrWhiteSpace(fileStorage.PublicBaseUrl))
            throw new InvalidOperationException("FileStorage:PublicBaseUrl is required when FileStorage:Provider is Local.");
        builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
        break;
    case "S3":
        if (string.IsNullOrWhiteSpace(fileStorage.S3Bucket))
            throw new InvalidOperationException("FileStorage:S3Bucket is required when FileStorage:Provider is S3.");
        builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(BuildS3Config(fileStorage)));
        builder.Services.AddScoped<IFileStorage, S3FileStorage>();
        break;
    default:
        throw new InvalidOperationException($"Unknown FileStorage:Provider '{fileStorage.Provider}'. Accepted values: Local, S3.");
}

// bloco de CLI, no mesmo padrão do bloco --rotate-storage-key existente
if (args.Contains("--scan-orphan-files")) { /* resolve OrphanFileScanner, executa, imprime totais, retorna 0 ou 1 */ }
```

**Internal Logic:** o switch roda antes de `builder.Build()`; o bloco de CLI roda depois de `Build()` e antes de `app.Run()`, devolvendo o código de saída com `Environment.ExitCode`.
**Dependencies:** `AmazonS3Client`, `AmazonS3Config`, `IFileStorage`, `LocalFileStorage`, `S3FileStorage`, `OrphanFileScanner`.
**Error Handling:** provider desconhecido ou configuração incompleta impedem a inicialização com as exceções citadas. Falha em qualquer chave durante `--scan-orphan-files` faz o código de saída ser `1`.

### Component: `Document` e `Tenant` (entidades) e `SignedDownloadDto`

**Purpose:** persistir metadados de armazenamento e expor a URL assinada.
**Interface:**
- `src/Ciclo.Core/Entities/Document.cs` ganha `public long TamanhoBytes { get; set; }`, `public string HashSha256 { get; set; } = string.Empty;`, `public string ContentType { get; set; } = string.Empty;`, `public Guid EnviadoPorUsuarioId { get; set; }`.
- `src/Ciclo.Core/Entities/Tenant.cs` ganha `public long StorageQuotaBytes { get; set; } = 1_073_741_824;`.
- `src/Ciclo.Infrastructure/Contracts/DocumentDtos.cs` ganha `public sealed record SignedDownloadDto(string Url, DateTimeOffset ExpiresAt);` e `DocumentDto` recebe `TamanhoBytes`, `HashSha256`, `ContentType`, `EnviadoPorUsuarioId`.
**Internal Logic:** apenas dados; nenhuma lógica de armazenamento nas entidades.
**Error Handling:** documentos anteriores à migration ficam com `TamanhoBytes = 0`, `HashSha256` e `ContentType` vazios e `EnviadoPorUsuarioId = Guid.Empty`; o DTO serializa esses valores sem exceção.

### Component: Frontend de download

**Purpose:** obter o arquivo pela URL assinada mantendo o contrato de `Blob` consumido pela tela.
**Interface:** `frontend/src/api/documents.ts`

```ts
export async function requestDownloadUrl(id: string): Promise<{ url: string; expiresAt: string }>;
export async function downloadDocument(id: string): Promise<Blob>;
```

**Internal Logic:** `downloadDocument` chama `requestDownloadUrl`, busca `url` com `fetch` e devolve `await res.blob()`; `DocumentVerification.tsx` não muda. `frontend/src/mocks/handlers.ts` ganha o handler de `/api/documents/:id/download-url` devolvendo uma URL de mock.
**Dependencies:** cliente `api` existente (axios), `fetch` nativo.
**Error Handling:** resposta não-2xx no `download-url` propaga o erro do axios; `fetch` com status diferente de 2xx lança `Error('download_failed')`.

## Data Flow

**Upload:** controlador recebe `IFormFile` ⇒ `DocumentService.UploadAsync` ⇒ checagem de pesquisabilidade, extensão e assinatura (quarentena + 400 em divergência) ⇒ limite de 10 MB ⇒ aluno, vínculo e tipo ⇒ cota (`soma de TamanhoBytes + Length` contra `Tenant.StorageQuotaBytes`; excedeu, para aqui) ⇒ `HashingReadStream` envolve o stream ⇒ `IFileStorage.SaveAsync` grava o contêiner em blocos ⇒ `Document` persistido com caminho, tamanho, hash, MIME e autor ⇒ resposta `Created`.

**Emissão de URL:** `GET api/documents/{id}/download-url` ⇒ autorização por papel ⇒ `GetDownloadUrlAsync` carrega e autoriza o documento ⇒ `IFileStorage.GetSignedUrlAsync(caminho, 5 min)` ⇒ `{ url, expiresAt }`.

**Download assinado (driver local):** `GET api/documents/signed/{token}` ⇒ decodifica `epoch`, `hmac` e `filePath` ⇒ HMAC recalculado com `FileStorage:EncryptionKey` e comparado ⇒ expiração verificada ⇒ `OpenReadAsync` devolve o `DecryptingStream` ⇒ resposta de bytes com o MIME derivado da extensão.

**Varredura de órfãos:** `--scan-orphan-files` ⇒ `ListKeysAsync` (sem a área de quarentena) ⇒ conjunto de `Document.CaminhoArquivo` do escopo ⇒ diferença ⇒ com `--delete`, `DeleteAsync` por órfão ⇒ totais impressos e código de saída.

## Data Structures

**Contêiner local v2 (bytes):**

| Offset | Tamanho | Conteúdo |
|---|---|---|
| 0 | 8 | `"CICLOEN2"` (ASCII) |
| 8 | 4 por registro | Comprimento do cifrado, `int32` little-endian |
| 12 | 12 por registro | Nonce GCM |
| 24 | 16 por registro | Tag GCM |
| 40 | N por registro | Cifrado |

Bloco de texto claro tem no máximo 65536 bytes, portanto o último registro pode ser menor; arquivo vazio contém apenas o marcador.

**Contêiner local v1 (legado, leitura apenas):** `"CICLOENC"` (8) + nonce (12) + tag (16) + cifrado do arquivo inteiro.

**Token de URL assinada (driver local):** `{epoch}.{base64url(HMAC-SHA256(key, filePath + "|" + epoch))}.{base64url(filePath)}`.

**`OrphanScanResult`:** `Scanned` (total de chaves listadas), `Orphaned` (chaves sem documento), `Deleted` (chaves removidas), `Failures` (mensagens das chaves que falharam).

**`Document` (campos novos):** `TamanhoBytes` `long`; `HashSha256` `string` 64 hexadecimais maiúsculos, não nulo, padrão vazio; `ContentType` `string` não nulo, padrão vazio; `EnviadoPorUsuarioId` `Guid`.

**`Tenant` (campo novo):** `StorageQuotaBytes` `long`, padrão `1_073_741_824`.

**Migration `AddDocumentStorageMetadata`:** adiciona as quatro colunas de `Document` e `Tenant.StorageQuotaBytes`, todas `NOT NULL` com valor padrão (`0`, `''`, `''`, `00000000-0000-0000-0000-000000000000`, `1073741824`).

## File / Module Layout

**Arquivos existentes modificados**

- `src/Ciclo.Infrastructure/Storage/IFileStorage.cs` — cinco membros novos.
- `src/Ciclo.Infrastructure/Storage/LocalFileStorage.cs` — contêiner v2, streaming, `DecryptingStream`, URL assinada, quarentena, listagem, `FileStorageOptions` estendida.
- `src/Ciclo.Infrastructure/Services/DocumentService.cs` — checksum, metadados, cota, quarentena, `GetDownloadUrlAsync`, `LoadAuthorizedAsync`.
- `src/Ciclo.Core/Entities/Document.cs` — quatro propriedades novas.
- `src/Ciclo.Core/Entities/Tenant.cs` — `StorageQuotaBytes`.
- `src/Ciclo.Infrastructure/Contracts/DocumentDtos.cs` — `SignedDownloadDto` e campos novos de `DocumentDto`.
- `src/Ciclo.Infrastructure/Ciclo.Infrastructure.csproj` — referência a `AWSSDK.S3`.
- `src/Ciclo.Api/Controllers/DocumentController.cs` — duas rotas novas.
- `src/Ciclo.Api/Program.cs` — seleção de provider, configuração S3, opção `--scan-orphan-files`.
- `frontend/src/api/documents.ts` — `requestDownloadUrl` e `downloadDocument` por URL assinada.
- `frontend/src/mocks/handlers.ts` — handler de `download-url`.
- `tests/Ciclo.Api.Tests/TestDoubles.cs` — `NoopStorage` com os membros novos.

**Arquivos novos**

- `src/Ciclo.Infrastructure/Storage/S3FileStorage.cs` — driver S3-compatível.
- `src/Ciclo.Infrastructure/Storage/HashingReadStream.cs` — decorador de checksum.
- `src/Ciclo.Infrastructure/Storage/OrphanFileScanner.cs` — varredura de órfãos e `OrphanScanResult`.
- `tests/Ciclo.Api.Tests/FileStorageDriverTests.cs` — testes dos dois drivers.
- `tests/Ciclo.Api.Tests/DocumentStorageScaleTests.cs` — testes de upload, cota, quarentena e varredura.
- Migration EF `AddDocumentStorageMetadata`, gerada por `dotnet ef migrations add AddDocumentStorageMetadata` na pasta de migrations existente do projeto de infraestrutura.

## Testing Strategy

`tests/Ciclo.Api.Tests/FileStorageDriverTests.cs` (xUnit + Moq, sem rede):
- Ida e volta no driver local com arquivo de 150000 bytes (três blocos), verificando igualdade byte a byte por `OpenReadAsync` e por `GetAsync`.
- Gravação a partir de `Stream` somente-avanço, sem `Length` nem `Position`, comprovando que a gravação não exige buffering integral nem pesquisabilidade.
- Leitura de arquivo legado em texto claro e de arquivo `CICLOENC` produzido no próprio teste com `AesGcm`.
- `ReencryptAsync` converte um `CICLOENC` em `CICLOEN2` preservando o conteúdo e torna o arquivo legível pelo caminho novo.
- Arquivo truncado: `IOException` e nenhum byte devolvido.
- Caminho fora da raiz: `UnauthorizedAccessException` em leitura e remoção.
- `ExistsAsync` verdadeiro e falso; `ListKeysAsync` não devolve chaves de quarentena; diretório ausente devolve lista vazia.
- `GetSignedUrlAsync` gera URL com `PublicBaseUrl` e token cujo HMAC é recomputável; `lifetime` zero lança `ArgumentOutOfRangeException`.
- Driver S3 com `Mock<IAmazonS3>`: `PutObject` com bucket e chave esperados; `GetObject` devolve conteúdo; `HeadObject`/`GetObjectMetadata` com 404 resulta em `false` e `FileNotFoundException`; `DeleteObject` chamado; `GetPreSignedURL` usado; `ListObjectsV2` pagina e ignora `quarantine/`.

`tests/Ciclo.Api.Tests/DocumentStorageScaleTests.cs` (xUnit + Moq + EF InMemory):
- Upload persiste `HashSha256` igual ao SHA-256 calculado independentemente no teste e `TamanhoBytes` igual ao comprimento real, além de `ContentType` e `EnviadoPorUsuarioId`.
- Upload acima da cota devolve `storage_quota_exceeded` e nenhuma chamada a `SaveAsync` acontece (verificado no `Mock<IFileStorage>`).
- Upload exatamente igual ao saldo restante é aceito; um byte acima é recusado.
- Conteúdo com assinatura divergente chama `SaveQuarantineAsync` e mantém `invalid_file_content`.
- `OrphanFileScanner` relata chave sem documento, Remove com `deleteOrphans` verdadeiro, devolve lista vazia em raiz inexistente e isola falhas por chave.

`tests/Ciclo.Api.Tests/TestDoubles.cs`: `NoopStorage` implementa os cinco membros novos com comportamento inerte para os testes existentes continuarem compilando.

Validação do projeto: `dotnet test` na solução e `npm run build` em `frontend/` para o ajuste de download.

## Migration / Rollback

**Migration:** gerada por `dotnet ef migrations add AddDocumentStorageMetadata` e aplicada no startup conforme o padrão atual do projeto. As colunas novas são `NOT NULL` com valor padrão, portanto a migration aplica sobre dados existentes sem reescrita de arquivos. Documentos antigos passam a expor `TamanhoBytes = 0` e `HashSha256` vazio até serem reenviados; nenhum backfill é executado por esta spec.

**Compatibilidade de arquivos:** o driver local lê os formatos v1 e texto claro indefinidamente. `--rotate-storage-key` continua funcionando e converte arquivos v1 para v2 por arquivo.

**Rollback:** reverter o `Provider` para `Local` mantém a aplicação operando; arquivos v2 não são legíveis por uma versão anterior do código, então o rollback de código exige reverter os arquivos por uma execução de re-criptografia com a versão nova antes do downgrade. A migration é reversível pelo `Down` padrão do EF (remoção das colunas), que apaga apenas metadados, nunca arquivos.
