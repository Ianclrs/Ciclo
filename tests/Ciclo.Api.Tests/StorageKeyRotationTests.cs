using System.Security.Cryptography;
using System.Text;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// Rotação da chave de armazenamento.
///
/// O risco que estes testes cobrem é concreto: trocar `FileStorage:EncryptionKey` sem reescrever
/// os arquivos torna **todos os documentos ilegíveis**. O mecanismo tem três partes — chave atual
/// cifra, anteriores decifram, e a re-encriptação regrava no mesmo caminho.
/// </summary>
public sealed class StorageKeyRotationTests : IDisposable
{
    private static readonly string Key1 =
        Convert.ToBase64String(Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef"));

    private static readonly string Key2 =
        Convert.ToBase64String(Encoding.ASCII.GetBytes("fedcba9876543210fedcba9876543210"));

    private readonly string _rootPath;
    private readonly byte[] _content = Encoding.UTF8.GetBytes("conteudo confidencial do documento");

    public StorageKeyRotationTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "ciclo-rotation", Guid.NewGuid().ToString("N"));
    }

    private LocalFileStorage Storage(string currentKey, params string[] previousKeys) =>
        new(Options.Create(new FileStorageOptions
        {
            RootPath = _rootPath,
            EncryptionKey = currentKey,
            PreviousEncryptionKeys = previousKeys
        }));

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public async Task OldKey_StaysReadableWhileItIsInTheKeyRing()
    {
        var before = Storage(Key1);
        using var content = new MemoryStream(_content);
        var path = await before.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), "rg.pdf", content);

        // Depois da troca: chave nova cifra, antiga ainda decifra.
        var during = Storage(Key2, Key1);
        using var read = await during.GetAsync(path);

        Assert.Equal(_content, await ReadAllAsync(read));
    }

    [Fact]
    public async Task Reencrypt_MakesTheFileReadableWithTheNewKeyAlone()
    {
        var before = Storage(Key1);
        using var content = new MemoryStream(_content);
        var path = await before.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), "rg.pdf", content);

        var during = Storage(Key2, Key1);
        await during.ReencryptAsync(path);

        // Sem a chave antiga no keyring, o arquivo continua legível.
        var after = Storage(Key2);
        using var read = await after.GetAsync(path);
        Assert.Equal(_content, await ReadAllAsync(read));

        // E a chave antiga já não serve — a prova de que houve regravação.
        var oldOnly = Storage(Key1);
        await Assert.ThrowsAsync<CryptographicException>(() => oldOnly.GetAsync(path));
    }

    [Fact]
    public async Task Reencrypt_IsIdempotent()
    {
        var before = Storage(Key1);
        using var content = new MemoryStream(_content);
        var path = await before.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), "rg.pdf", content);

        var during = Storage(Key2, Key1);
        await during.ReencryptAsync(path);
        await during.ReencryptAsync(path);
        await during.ReencryptAsync(path);

        var after = Storage(Key2);
        using var read = await after.GetAsync(path);
        Assert.Equal(_content, await ReadAllAsync(read));
    }

    [Fact]
    public async Task LegacyPlaintextFile_IsEncryptedByTheRotation()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "legacy"));
        var path = Path.Combine(_rootPath, "legacy", "antigo.pdf");
        await File.WriteAllBytesAsync(path, _content);

        await Storage(Key2).ReencryptAsync(path);

        var stored = await File.ReadAllBytesAsync(path);
        Assert.Equal("CICLOENC"u8.ToArray(), stored[..8]);

        using var read = await Storage(Key2).GetAsync(path);
        Assert.Equal(_content, await ReadAllAsync(read));
    }

    [Fact]
    public async Task Rotation_ReencryptsEveryDocumentAndCountsMissingFiles()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        await using var _ = db;

        var before = Storage(Key1);
        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var type = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "RG");

        using var content = new MemoryStream(_content);
        var path = await before.SaveAsync(tenantId, student.Id, "rg.pdf", content);

        var document = await DocumentTestData.AddDocumentAsync(
            db, tenantId, student.Id, type.Id, DocumentStatus.Pendente);
        document.CaminhoArquivo = path;

        var orphan = await DocumentTestData.AddDocumentAsync(
            db, tenantId, student.Id, type.Id, DocumentStatus.Pendente);
        orphan.CaminhoArquivo = Path.Combine(_rootPath, "nao-existe.pdf");
        await db.SaveChangesAsync();

        var rotation = new StorageKeyRotation(
            db, Storage(Key2, Key1), NullLogger<StorageKeyRotation>.Instance);

        var result = await rotation.RotateAsync();

        Assert.Equal(1, result.Rotated);
        Assert.Equal(1, result.MissingFiles);
        Assert.Equal(0, result.Failed);

        using var read = await Storage(Key2).GetAsync(path);
        Assert.Equal(_content, await ReadAllAsync(read));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
            Directory.Delete(_rootPath, recursive: true);
    }
}
