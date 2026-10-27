using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Ciclo.Infrastructure.Storage;

public class LocalFileStorage : IFileStorage
{
    // AES-256-GCM: nonce de 12 bytes e tag de 16 bytes (padrão recomendado)
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // Cabeçalho mágico para identificar arquivos criptografados
    // (arquivos antigos, sem header, continuam sendo lidos normalmente)
    private static readonly byte[] Magic = "CICLOENC"u8.ToArray();

    private readonly string _rootPath;
    private readonly byte[][] _encryptionKeys;

    public LocalFileStorage(IOptions<FileStorageOptions> options)
    {
        _rootPath = Path.GetFullPath(options.Value.RootPath);

        // Rotação (runbook no README): a chave ATUAL cifra; as ANTERIORES só decifram.
        // A lista é validada com o mesmo fail-closed da chave principal — uma chave antiga
        // malformada impediria a leitura de documentos e deve falhar no startup, não no download.
        var previous = options.Value.PreviousEncryptionKeys ?? [];
        _encryptionKeys = [FileStorageKey.Parse(options.Value.EncryptionKey), .. previous.Select(FileStorageKey.Parse)];
    }

    public async Task<string> SaveAsync(Guid tenantId, Guid studentId, string fileName, Stream content)
    {
        var safeName = SanitizeFileName(fileName);
        var uniqueName = $"{Guid.NewGuid():N}_{safeName}";
        var dir = Path.Combine(_rootPath, tenantId.ToString(), studentId.ToString());
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, uniqueName);

        // Lê todo o conteúdo (limite da aplicação: 10 MB) e criptografa com AES-GCM
        using var plainBuffer = new MemoryStream();
        await content.CopyToAsync(plainBuffer);

        var encrypted = Encrypt(plainBuffer.ToArray());
        await File.WriteAllBytesAsync(filePath, encrypted);

        return filePath;
    }

    public Task<Stream> GetAsync(string filePath)
    {
        var fullPath = ResolvePath(filePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("File not found.", fullPath);

        var bytes = File.ReadAllBytes(fullPath);

        // Compatibilidade: arquivos gravados antes da criptografia são servidos como estão
        var plain = IsEncrypted(bytes) ? Decrypt(bytes) : bytes;

        return Task.FromResult<Stream>(new MemoryStream(plain));
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

        var bytes = await File.ReadAllBytesAsync(fullPath);
        var plain = IsEncrypted(bytes) ? Decrypt(bytes) : bytes;
        var reencrypted = Encrypt(plain);

        // Grava ao lado e move por cima: uma falha no meio não deixa o documento truncado.
        var tempPath = fullPath + ".rotate.tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, reencrypted);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private byte[] Encrypt(byte[] plaintext)
    {
        var ciphertext = new byte[plaintext.Length];
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_encryptionKeys[0], TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Formato do arquivo: [Magic(8)][Nonce(12)][Tag(16)][Ciphertext]
        var result = new byte[Magic.Length + NonceSize + TagSize + ciphertext.Length];
        Magic.CopyTo(result, 0);
        nonce.CopyTo(result, Magic.Length);
        tag.CopyTo(result, Magic.Length + NonceSize);
        ciphertext.CopyTo(result, Magic.Length + NonceSize + TagSize);

        return result;
    }

    private byte[] Decrypt(byte[] data)
    {
        var nonce = data.AsSpan(Magic.Length, NonceSize);
        var tag = data.AsSpan(Magic.Length + NonceSize, TagSize);
        var ciphertext = data.AsSpan(Magic.Length + NonceSize + TagSize);

        // Tenta a chave atual e depois cada anterior. A tag GCM é a prova: uma chave errada
        // falha na autenticação, então não há como decifrar com a chave errada por engano.
        foreach (var key in _encryptionKeys)
        {
            var plaintext = new byte[ciphertext.Length];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return plaintext;
            }
            catch (AuthenticationTagMismatchException)
            {
                // Chave errada para este arquivo: tenta a próxima.
            }
        }

        throw new CryptographicException(
            "No configured storage key could decrypt this file. "
            + "If a key was rotated, keep the previous key in FileStorage:PreviousEncryptionKeys "
            + "until every document has been re-encrypted (--rotate-storage-key).");
    }

    private static bool IsEncrypted(byte[] data)
    {
        return data.Length >= Magic.Length && data.AsSpan(0, Magic.Length).SequenceEqual(Magic);
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
}

public class FileStorageOptions
{
    public const string SectionName = "FileStorage";
    public string RootPath { get; set; } = "uploads";
    public string? EncryptionKey { get; set; }

    /// <summary>
    /// Chaves anteriores (Base64), aceitas SOMENTE para leitura durante a rotação.
    /// Remova-as depois de rodar a re-encriptação de todos os documentos.
    /// </summary>
    public string[] PreviousEncryptionKeys { get; set; } = [];
}
