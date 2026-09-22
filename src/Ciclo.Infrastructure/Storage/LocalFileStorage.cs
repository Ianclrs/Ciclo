using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Ciclo.Infrastructure.Storage;

public class LocalFileStorage : IFileStorage
{
    // AES-256-GCM: nonce de 12 bytes e tag de 16 bytes (padrão recomendado)
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    // Cabeçalho mágico para identificar arquivos criptografados
    // (arquivos antigos, sem header, continuam sendo lidos normalmente)
    private static readonly byte[] Magic = "CICLOENC"u8.ToArray();

    private readonly string _rootPath;
    private readonly byte[] _encryptionKey;

    public LocalFileStorage(IOptions<FileStorageOptions> options)
    {
        _rootPath = Path.GetFullPath(options.Value.RootPath);

        var key = options.Value.EncryptionKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "FileStorage:EncryptionKey is not configured. Configure a Base64-encoded 32-byte key for document encryption.");

        _encryptionKey = Convert.FromBase64String(key);
        if (_encryptionKey.Length != KeySize)
            throw new InvalidOperationException(
                "FileStorage:EncryptionKey must be a Base64-encoded 32-byte key (AES-256).");
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

    private byte[] Encrypt(byte[] plaintext)
    {
        var ciphertext = new byte[plaintext.Length];
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_encryptionKey, TagSize);
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

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
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
}
