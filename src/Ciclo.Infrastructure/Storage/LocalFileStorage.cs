using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Ciclo.Infrastructure.Storage;

public class LocalFileStorage : IFileStorage
{
    // AES-256-GCM: nonce de 12 bytes e tag de 16 bytes (padrão recomendado)
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // Tamanho do bloco de texto claro cifrado por registro do contêiner v2.
    private const int ChunkSize = 65536;

    // Prefixo int32 little-endian de cada registro do contêiner v2.
    private const int LengthSize = 4;

    private const string QuarantineDirectoryName = "quarantine";
    private const string CorruptedChunkMessage = "Corrupted storage chunk.";

    private const string RotationMessage =
        "No configured storage key could decrypt this file. "
        + "If a key was rotated, keep the previous key in FileStorage:PreviousEncryptionKeys "
        + "until every document has been re-encrypted (--rotate-storage-key).";

    // Contêiner v1: [Magic][Nonce][Tag][Ciphertext] — conteúdo inteiro cifrado de uma vez.
    private static readonly byte[] MagicV1 = "CICLOENC"u8.ToArray();

    // Contêiner v2: [Magic][registros] — cada registro é
    // [comprimento int32 LE][nonce][tag][cifrado], o que permite decifrar bloco a bloco.
    private static readonly byte[] MagicV2 = "CICLOEN2"u8.ToArray();

    private readonly string _rootPath;
    private readonly byte[][] _encryptionKeys;
    private readonly string? _publicBaseUrl;

    public LocalFileStorage(IOptions<FileStorageOptions> options)
    {
        _rootPath = Path.GetFullPath(options.Value.RootPath);
        _publicBaseUrl = options.Value.PublicBaseUrl;

        // Rotação (runbook no README): a chave ATUAL cifra; as ANTERIORES só decifram.
        // A lista é validada com o mesmo fail-closed da chave principal — uma chave antiga
        // malformada impediria a leitura de documentos e deve falhar no startup, não no download.
        var previous = options.Value.PreviousEncryptionKeys ?? [];
        _encryptionKeys = [FileStorageKey.Parse(options.Value.EncryptionKey), .. previous.Select(FileStorageKey.Parse)];
    }

    /// <summary>
    /// Grava o conteúdo em streaming: cada bloco de até <see cref="ChunkSize"/> bytes de texto
    /// claro é selado individualmente, então o arquivo inteiro nunca precisa estar em memória.
    /// </summary>
    public async Task<string> SaveAsync(Guid tenantId, Guid studentId, string fileName, Stream content)
    {
        var safeName = SanitizeFileName(fileName);
        var uniqueName = $"{Guid.NewGuid():N}_{safeName}";
        var dir = Path.Combine(_rootPath, tenantId.ToString(), studentId.ToString());
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, uniqueName);
        await WriteEncryptedAsync(filePath, content);

        return filePath;
    }

    public async Task<Stream> GetAsync(string filePath)
    {
        var stream = await OpenReadAsync(filePath);

        try
        {
            var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.Position = 0;
            return buffer;
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    public Task DeleteAsync(string filePath)
    {
        var fullPath = ResolvePath(filePath);

        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    public async Task ReencryptAsync(string filePath)
    {
        var fullPath = ResolvePath(filePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("File not found.", fullPath);

        byte[] plain;
        await using (var source = await OpenReadAsync(fullPath))
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            plain = buffer.ToArray();
        }

        // Grava ao lado e move por cima: uma falha no meio não deixa o documento truncado.
        var tempPath = fullPath + ".rotate.tmp";
        try
        {
            await WriteEncryptedAsync(tempPath, new MemoryStream(plain));
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    public Task<bool> ExistsAsync(string filePath)
    {
        return Task.FromResult(File.Exists(ResolvePath(filePath)));
    }

    /// <summary>
    /// Abre o arquivo para leitura em streaming. O contêiner v2 é decifrado bloco a bloco sob
    /// demanda; o v1 e os arquivos legados sem cabeçalho continuam legíveis.
    /// </summary>
    public async Task<Stream> OpenReadAsync(string filePath)
    {
        var fullPath = ResolvePath(filePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("File not found.", fullPath);

        var file = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true);

        try
        {
            var header = new byte[MagicV2.Length];
            var read = ReadUpTo(file, header);

            if (read == MagicV2.Length && header.AsSpan().SequenceEqual(MagicV2))
                return new DecryptingStream(file, _encryptionKeys);

            if (read == MagicV2.Length && header.AsSpan().SequenceEqual(MagicV1))
            {
                using var body = new MemoryStream();
                await file.CopyToAsync(body);
                await file.DisposeAsync();
                return new MemoryStream(DecryptVersionOne(body.ToArray(), _encryptionKeys));
            }

            // Legado sem marcador: servido como está, do começo do arquivo.
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    /// <summary>Retém o conteúdo suspeito fora do escopo de documentos e devolve a chave gravada.</summary>
    public async Task<string> SaveQuarantineAsync(Guid tenantId, string fileName, Stream content)
    {
        var safeName = SanitizeFileName(fileName);
        var dir = Path.Combine(_rootPath, QuarantineDirectoryName, tenantId.ToString());
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, $"{Guid.NewGuid():N}_{safeName}");
        await WriteEncryptedAsync(filePath, content);

        return filePath;
    }

    /// <summary>
    /// Chaves gravadas no escopo do tenant. A quarentena fica fora da listagem para que a
    /// varredura de órfãos nunca a considere arquivo de documento.
    /// </summary>
    public Task<IReadOnlyList<string>> ListKeysAsync(Guid? tenantId)
    {
        var scope = tenantId is null
            ? _rootPath
            : Path.Combine(_rootPath, tenantId.Value.ToString());

        if (!Directory.Exists(scope))
            return Task.FromResult<IReadOnlyList<string>>([]);

        var quarantinePrefix = Path.Combine(_rootPath, QuarantineDirectoryName) + Path.DirectorySeparatorChar;

        var keys = Directory
            .EnumerateFiles(scope, "*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(quarantinePrefix, StringComparison.Ordinal))
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

    /// <summary>
    /// URL de download assinada por HMAC-SHA256 sobre <c>filePath|epoch</c>. O token é a
    /// autorização da rota que serve os bytes; a rota de emissão mantém a autorização por papel.
    /// </summary>
    public Task<string> GetSignedUrlAsync(string filePath, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(lifetime), lifetime, "The signed URL lifetime must be positive.");

        if (string.IsNullOrWhiteSpace(_publicBaseUrl))
            throw new InvalidOperationException(
                "FileStorage:PublicBaseUrl is required for the local provider.");

        var fullPath = ResolvePath(filePath);
        var epoch = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();

        var signature = HMACSHA256.HashData(
            _encryptionKeys[0], Encoding.UTF8.GetBytes($"{fullPath}|{epoch}"));

        var token = $"{epoch}.{Base64UrlEncode(signature)}.{Base64UrlEncode(Encoding.UTF8.GetBytes(fullPath))}";

        return Task.FromResult($"{_publicBaseUrl.TrimEnd('/')}/api/documents/signed/{token}");
    }

    /// <summary>
    /// Grava o contêiner v2 lendo a origem em blocos. Em qualquer falha o arquivo parcial é
    /// removido e a exceção original segue para o chamador.
    /// </summary>
    private async Task WriteEncryptedAsync(string filePath, Stream content)
    {
        try
        {
            await using var file = new FileStream(
                filePath, FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize, useAsync: true);

            await file.WriteAsync(MagicV2);

            using var aes = new AesGcm(_encryptionKeys[0], TagSize);

            var buffer = new byte[ChunkSize];
            while (true)
            {
                var read = await ReadChunkAsync(content, buffer);
                if (read == 0)
                    break;

                var ciphertext = new byte[read];
                var nonce = new byte[NonceSize];
                var tag = new byte[TagSize];

                RandomNumberGenerator.Fill(nonce);
                aes.Encrypt(nonce, buffer.AsSpan(0, read), ciphertext, tag);

                var length = new byte[LengthSize];
                BinaryPrimitives.WriteInt32LittleEndian(length, read);

                await file.WriteAsync(length);
                await file.WriteAsync(nonce);
                await file.WriteAsync(tag);
                await file.WriteAsync(ciphertext);
            }
        }
        catch
        {
            // A exceção original é a que importa; a limpeza é melhor esforço e não a substitui.
            DeleteIfExists(filePath);
            throw;
        }
    }

    /// <summary>
    /// Preenche o bloco até <see cref="ChunkSize"/> ou até o fim da origem. Um <c>Read</c>
    /// parcial não é tratado como fim: streams de rede devolvem menos bytes sem terem acabado.
    /// </summary>
    private static async Task<int> ReadChunkAsync(Stream source, byte[] buffer)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await source.ReadAsync(buffer.AsMemory(total, buffer.Length - total));
            if (read == 0)
                break;

            total += read;
        }

        return total;
    }

    private static int ReadUpTo(Stream stream, byte[] buffer)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
                break;

            total += read;
        }

        return total;
    }

    private static byte[] DecryptVersionOne(byte[] data, byte[][] keys)
    {
        if (data.Length < NonceSize + TagSize)
            throw new IOException(CorruptedChunkMessage);

        return DecryptChunk(
            data.AsSpan(0, NonceSize).ToArray(),
            data.AsSpan(NonceSize, TagSize).ToArray(),
            data.AsSpan(NonceSize + TagSize).ToArray(),
            keys);
    }

    private static byte[] DecryptChunk(byte[] nonce, byte[] tag, byte[] ciphertext, byte[][] keys)
    {
        var plaintext = new byte[ciphertext.Length];

        // Tenta a chave atual e depois cada anterior. A tag GCM é a prova: uma chave errada
        // falha na autenticação, então não há como decifrar com a chave errada por engano.
        foreach (var key in keys)
        {
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return plaintext;
            }
            catch (AuthenticationTagMismatchException)
            {
                // Chave errada para este bloco: tenta a próxima.
            }
        }

        throw new CryptographicException(RotationMessage);
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Arquivo ainda em uso por outro processo: nada a fazer além de seguir.
        }
        catch (UnauthorizedAccessException)
        {
            // Sem permissão de remoção: nada a fazer além de seguir.
        }
    }

    private string ResolvePath(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);

        // Prevent path traversal: verify file is within root
        if (!fullPath.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal) && fullPath != _rootPath)
            throw new UnauthorizedAccessException("File path is outside storage root.");

        return fullPath;
    }

    private static string SanitizeFileName(string fileName)
    {
        // Allow only alphanumeric, underscore, dot, hyphen
        var safe = Regex.Replace(fileName, @"[^a-zA-Z0-9._-]", "_");
        if (string.IsNullOrWhiteSpace(safe))
            safe = "unnamed";
        return safe;
    }

    /// <summary>
    /// Decifra o contêiner v2 registro a registro, mantendo apenas o bloco corrente em memória.
    /// A leitura de disco é síncrona de propósito: o bloco é de 64 KB e cada chamada devolve
    /// dados de um único bloco, então não há ganho em uma máquina de estados assíncrona por byte.
    /// </summary>
    private sealed class DecryptingStream : Stream
    {
        private readonly FileStream _file;
        private readonly byte[][] _keys;

        private byte[] _current = [];
        private int _offset;

        public DecryptingStream(FileStream file, byte[][] keys)
        {
            _file = file;
            _keys = keys;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return ReadCore(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer) => ReadCore(buffer);

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return Task.FromResult(ReadCore(buffer.AsSpan(offset, count)));
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(ReadCore(buffer.Span));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _file.Dispose();

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _file.DisposeAsync();
            await base.DisposeAsync();
        }

        private int ReadCore(Span<byte> destination)
        {
            if (destination.Length == 0)
                return 0;

            if (_offset >= _current.Length && !FillNextChunk())
                return 0;

            var available = Math.Min(destination.Length, _current.Length - _offset);
            _current.AsSpan(_offset, available).CopyTo(destination);
            _offset += available;

            return available;
        }

        private bool FillNextChunk()
        {
            var header = new byte[LengthSize];
            var read = ReadUpTo(_file, header);

            if (read == 0)
                return false;

            if (read < LengthSize)
                throw new IOException(CorruptedChunkMessage);

            var length = BinaryPrimitives.ReadInt32LittleEndian(header);

            // Registro vazio não é produzido pelo driver; tratá-lo como fim seria mascarar
            // um arquivo corrompido como documento vazio.
            if (length <= 0 || _file.Position + length + NonceSize + TagSize > _file.Length)
                throw new IOException(CorruptedChunkMessage);

            var nonce = new byte[NonceSize];
            var tag = new byte[TagSize];
            var ciphertext = new byte[length];

            if (ReadUpTo(_file, nonce) < NonceSize
                || ReadUpTo(_file, tag) < TagSize
                || ReadUpTo(_file, ciphertext) < length)
            {
                throw new IOException(CorruptedChunkMessage);
            }

            _current = DecryptChunk(nonce, tag, ciphertext, _keys);
            _offset = 0;

            return true;
        }
    }
}

public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Driver de armazenamento: <c>Local</c> (padrão) ou <c>S3</c>.</summary>
    public string Provider { get; set; } = "Local";

    public string RootPath { get; set; } = "uploads";
    public string? EncryptionKey { get; set; }

    /// <summary>Base pública usada para montar a URL assinada; obrigatória no provider local.</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>Bucket do driver S3.</summary>
    public string? S3Bucket { get; set; }

    /// <summary>Endpoint S3-compatível (MinIO, R2); nulo usa o endpoint da AWS.</summary>
    public string? S3ServiceUrl { get; set; }

    /// <summary>Estilo de caminho exigido por provedores S3-compatíveis que não usam DNS por bucket.</summary>
    public bool S3ForcePathStyle { get; set; }

    /// <summary>
    /// Chaves anteriores (Base64), aceitas SOMENTE para leitura durante a rotação.
    /// Remova-as depois de rodar a re-encriptação de todos os documentos.
    /// </summary>
    public string[] PreviousEncryptionKeys { get; set; } = [];
}
