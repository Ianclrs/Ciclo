using Microsoft.Extensions.Options;
using Ciclo.Infrastructure.Storage;

namespace Ciclo.Api.Tests;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _rootPath;
    private readonly LocalFileStorage _storage;
    private readonly byte[] _secretContent = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

    public LocalFileStorageTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "ciclo-tests", Guid.NewGuid().ToString("N"));
        var options = Options.Create(new FileStorageOptions
        {
            RootPath = _rootPath,
            EncryptionKey = Convert.ToBase64String("0123456789abcdef0123456789abcdef"u8.ToArray())
        });
        _storage = new LocalFileStorage(options);
    }

    [Fact]
    public async Task SaveAndGet_RoundTrip_PreservesContent()
    {
        var fileName = "documento.pdf";
        using var content = new MemoryStream(_secretContent);
        var tenantId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        var filePath = await _storage.SaveAsync(tenantId, studentId, fileName, content);

        var storedBytes = await File.ReadAllBytesAsync(filePath);
        var startsWithMagic = storedBytes.Length >= 8
            && storedBytes[0] == (byte)'C'
            && storedBytes[1] == (byte)'I'
            && storedBytes[2] == (byte)'C'
            && storedBytes[3] == (byte)'L'
            && storedBytes[4] == (byte)'O'
            && storedBytes[5] == (byte)'E'
            && storedBytes[6] == (byte)'N'
            && storedBytes[7] == (byte)'C';
        Assert.True(startsWithMagic, "Arquivo salvo deve ter o header de criptografia CICLOENC.");

        using var roundTrip = await _storage.GetAsync(filePath);
        using var roundTripBuffer = new MemoryStream();
        await roundTrip.CopyToAsync(roundTripBuffer);

        Assert.Equal(_secretContent, roundTripBuffer.ToArray());
    }

    [Fact]
    public async Task SaveAndGet_StoredBytes_AreNotPlainText()
    {
        var fileName = "foto.png";
        using var content = new MemoryStream(_secretContent);

        var filePath = await _storage.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), fileName, content);
        var storedBytes = await File.ReadAllBytesAsync(filePath);

        // O conteúdo criptografado não pode conter o plaintext na posição após o header
        var plaintextAtBody = storedBytes.AsSpan(8).IndexOf(_secretContent) >= 0;
        Assert.False(plaintextAtBody, "O conteúdo em disco não pode conter o texto puro.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
            Directory.Delete(_rootPath, recursive: true);
    }
}
