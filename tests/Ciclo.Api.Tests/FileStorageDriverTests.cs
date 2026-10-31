using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Ciclo.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Moq;

namespace Ciclo.Api.Tests;

/// <summary>
/// Comportamento contratado dos dois drivers de armazenamento.
///
/// O que estes testes protegem é o formato persistido e a superfície do contrato: um arquivo
/// gravado hoje precisa continuar legível depois de uma rotação de chave, o upload não pode
/// exigir o conteúdo inteiro em memória nem um stream pesquisável, e a URL assinada precisa ser
/// verificável sem o banco. O driver S3 é exercitado só com dublê de <see cref="IAmazonS3"/>,
/// então nenhum teste abre conexão de rede.
/// </summary>
public sealed class FileStorageDriverTests : IDisposable
{
    private const int ChunkSize = 65536;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int RecordOverhead = 4 + NonceSize + TagSize;
    private const string PublicBaseUrl = "https://api.test";
    private const string Bucket = "bucket";

    private static readonly byte[] Key =
        Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");

    private readonly string _rootPath;

    public FileStorageDriverTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "ciclo-driver-tests", Guid.NewGuid().ToString("N"));
    }

    private LocalFileStorage CreateLocal(string? publicBaseUrl = PublicBaseUrl) =>
        new(Options.Create(new FileStorageOptions
        {
            RootPath = _rootPath,
            EncryptionKey = Convert.ToBase64String(Key),
            PublicBaseUrl = publicBaseUrl,
        }));

    private static S3FileStorage CreateS3(Mock<IAmazonS3> s3) =>
        new(s3.Object, Options.Create(new FileStorageOptions
        {
            S3Bucket = Bucket,
            S3ForcePathStyle = true,
        }));

    [Fact]
    public async Task SaveAndRead_RoundTripAcrossSeveralChunks_PreservesContent()
    {
        var storage = CreateLocal();
        var content = new byte[150_000];
        RandomNumberGenerator.Fill(content);

        string path;
        using (var source = new MemoryStream(content))
            path = await storage.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), "grande.pdf", source);

        var records = (content.Length + ChunkSize - 1) / ChunkSize;
        Assert.True(records > 1, "O conteúdo precisa ocupar mais de um bloco para provar o streaming.");

        // Marcador (8) + registros completos + texto claro: cada bloco é selado individualmente.
        Assert.Equal(8L + records * RecordOverhead + content.Length, new FileInfo(path).Length);
        Assert.Equal(content, await ReadAllAsync(await storage.OpenReadAsync(path)));
        Assert.Equal(content, await ReadAllAsync(await storage.GetAsync(path)));
    }

    [Fact]
    public async Task SaveAsync_AcceptsForwardOnlyStreamWithoutLengthOrPosition()
    {
        var storage = CreateLocal();
        var content = Encoding.UTF8.GetBytes(new string('a', 70_000) + "fim");

        string path;
        using (var source = new ForwardOnlyStream(content))
            path = await storage.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), "avancando.bin", source);

        Assert.Equal(content, await ReadAllAsync(await storage.OpenReadAsync(path)));
    }

    [Fact]
    public async Task Read_ReadsLegacyPlaintextFileWithoutHeader()
    {
        var storage = CreateLocal();
        var content = Encoding.UTF8.GetBytes("arquivo legado em texto claro, sem cabeçalho");
        var path = await WriteLegacyPlaintextAsync(content);

        Assert.Equal(content, await ReadAllAsync(await storage.OpenReadAsync(path)));
        Assert.Equal(content, await ReadAllAsync(await storage.GetAsync(path)));
    }

    [Fact]
    public async Task Read_DecryptsVersionOneContainer()
    {
        var storage = CreateLocal();
        var content = Encoding.UTF8.GetBytes("documento no contêiner antigo CICLOENC");
        var path = await WriteVersionOneAsync(content);

        Assert.Equal(content, await ReadAllAsync(await storage.OpenReadAsync(path)));
        Assert.Equal(content, await ReadAllAsync(await storage.GetAsync(path)));
    }

    [Fact]
    public async Task ReencryptAsync_ConvertsVersionOneFileToVersionTwo()
    {
        var storage = CreateLocal();
        var content = Encoding.UTF8.GetBytes("conteudo a reencriptar pela rotacao");
        var path = await WriteVersionOneAsync(content);

        await storage.ReencryptAsync(path);

        var stored = await File.ReadAllBytesAsync(path);
        Assert.Equal("CICLOEN2"u8.ToArray(), stored[..8]);
        Assert.Equal(content, await ReadAllAsync(await storage.GetAsync(path)));
    }

    [Fact]
    public async Task GetAsync_TruncatedChunk_ThrowsIOException()
    {
        var storage = CreateLocal();
        var dir = Path.Combine(_rootPath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "truncado.pdf");

        // Comprimento declarado maior que o corpo restante: nenhum byte parcial pode sair.
        var header = new byte[8 + 4];
        "CICLOEN2"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), 1024);
        await File.WriteAllBytesAsync(path, header);

        await Assert.ThrowsAsync<IOException>(() => storage.GetAsync(path));
    }

    [Fact]
    public async Task Members_RejectPathOutsideStorageRoot()
    {
        var storage = CreateLocal();
        var outside = Path.Combine(
            Path.GetTempPath(), "ciclo-fora-da-raiz", Guid.NewGuid().ToString("N"), "x.pdf");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => storage.OpenReadAsync(outside));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => storage.DeleteAsync(outside));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => storage.GetSignedUrlAsync(outside, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task ExistsAsync_ReflectsTheStoredFilePresence()
    {
        var storage = CreateLocal();

        string path;
        using (var source = new MemoryStream(Encoding.UTF8.GetBytes("existente")))
            path = await storage.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), "existe.pdf", source);

        Assert.True(await storage.ExistsAsync(path));
        Assert.False(await storage.ExistsAsync(Path.Combine(_rootPath, "nao-existe.pdf")));
    }

    [Fact]
    public async Task ListKeysAsync_ExcludesQuarantineAndMissingDirectoryIsEmpty()
    {
        var storage = CreateLocal();
        Assert.Empty(await storage.ListKeysAsync(null));

        var tenantId = Guid.NewGuid();
        using (var document = new MemoryStream(Encoding.UTF8.GetBytes("documento")))
            await storage.SaveAsync(tenantId, Guid.NewGuid(), "doc.pdf", document);

        using (var suspicious = new MemoryStream(Encoding.UTF8.GetBytes("suspeito")))
            await storage.SaveQuarantineAsync(tenantId, "suspeito.pdf", suspicious);

        var keys = await storage.ListKeysAsync(null);

        Assert.Single(keys);
        Assert.DoesNotContain(keys, key => key.Contains("quarantine", StringComparison.Ordinal));
        Assert.Empty(await storage.ListKeysAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetSignedUrlAsync_ProducesTokenWhoseHmacIsRecomputable()
    {
        var storage = CreateLocal();

        string path;
        using (var source = new MemoryStream(Encoding.UTF8.GetBytes("conteudo assinado")))
            path = await storage.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), "assinado.pdf", source);

        var url = await storage.GetSignedUrlAsync(path, TimeSpan.FromMinutes(5));

        const string prefix = PublicBaseUrl + "/api/documents/signed/";
        Assert.StartsWith(prefix, url);

        var parts = url[prefix.Length..].Split('.');
        Assert.Equal(3, parts.Length);

        var epoch = long.Parse(parts[0], CultureInfo.InvariantCulture);
        var signedPath = Encoding.UTF8.GetString(Base64UrlDecode(parts[2]));

        Assert.Equal(Path.GetFullPath(path), signedPath);
        Assert.Equal(
            HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes($"{signedPath}|{epoch}")),
            Base64UrlDecode(parts[1]));
        Assert.True(epoch > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task GetSignedUrlAsync_RequiresPositiveLifetimeAndPublicBaseUrl()
    {
        var storage = CreateLocal();
        var path = Path.Combine(_rootPath, "qualquer.pdf");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => storage.GetSignedUrlAsync(path, TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => storage.GetSignedUrlAsync(path, TimeSpan.FromSeconds(-1)));

        var withoutBaseUrl = CreateLocal(publicBaseUrl: null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => withoutBaseUrl.GetSignedUrlAsync(path, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task S3SaveAsync_PutsObjectUnderTenantAndStudentPrefix()
    {
        var s3 = new Mock<IAmazonS3>();
        PutObjectRequest? captured = null;
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        var storage = CreateS3(s3);
        var tenantId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        using var content = new MemoryStream(Encoding.UTF8.GetBytes("conteudo"));
        var key = await storage.SaveAsync(tenantId, studentId, "rg.pdf", content);

        Assert.NotNull(captured);
        Assert.Equal(Bucket, captured!.BucketName);
        Assert.Equal(key, captured.Key);
        Assert.StartsWith($"{tenantId}/{studentId}/", key);
        Assert.EndsWith("_rg.pdf", key);
    }

    [Fact]
    public async Task S3SaveQuarantineAsync_PutsObjectUnderTheQuarantinePrefix()
    {
        var s3 = new Mock<IAmazonS3>();
        PutObjectRequest? captured = null;
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        var storage = CreateS3(s3);
        var tenantId = Guid.NewGuid();

        using var content = new MemoryStream(Encoding.UTF8.GetBytes("suspeito"));
        var key = await storage.SaveQuarantineAsync(tenantId, "suspeito.pdf", content);

        Assert.NotNull(captured);
        Assert.Equal(key, captured!.Key);
        Assert.StartsWith($"quarantine/{tenantId}/", key);
    }

    [Fact]
    public async Task S3GetAsync_ReturnsTheObjectContent()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes("conteudo remoto")),
            });

        var storage = CreateS3(s3);

        Assert.Equal(
            Encoding.UTF8.GetBytes("conteudo remoto"),
            await ReadAllAsync(await storage.GetAsync("t/s/k.pdf")));
    }

    [Fact]
    public async Task S3ReadMissingObject_ThrowsFileNotFoundException()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<GetObjectResponse>(NotFound()));

        var storage = CreateS3(s3);

        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.OpenReadAsync("t/s/ausente.pdf"));
    }

    [Fact]
    public async Task S3ExistsAsync_MapsTheObjectMetadataPresence()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<GetObjectMetadataResponse>(NotFound()));

        var storage = CreateS3(s3);
        Assert.False(await storage.ExistsAsync("t/s/ausente.pdf"));

        var present = new Mock<IAmazonS3>();
        present.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse());

        Assert.True(await CreateS3(present).ExistsAsync("t/s/presente.pdf"));
    }

    [Fact]
    public async Task S3DeleteAsync_DeletesTheObjectKey()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        await CreateS3(s3).DeleteAsync("t/s/k.pdf");

        s3.Verify(
            x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(request => request.BucketName == Bucket && request.Key == "t/s/k.pdf"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task S3GetSignedUrlAsync_DelegatesToTheSdk()
    {
        var s3 = new Mock<IAmazonS3>();
        GetPreSignedUrlRequest? captured = null;
        s3.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Callback<GetPreSignedUrlRequest>(request => captured = request)
            .Returns("https://signed.example/objeto");

        var url = await CreateS3(s3).GetSignedUrlAsync("t/s/k.pdf", TimeSpan.FromMinutes(5));

        Assert.Equal("https://signed.example/objeto", url);
        Assert.NotNull(captured);
        Assert.Equal(Bucket, captured!.BucketName);
        Assert.Equal("t/s/k.pdf", captured.Key);
        Assert.Equal(HttpVerb.GET, captured.Verb);
    }

    [Fact]
    public async Task S3GetSignedUrlAsync_RejectsNonPositiveLifetime()
    {
        var storage = CreateS3(new Mock<IAmazonS3>());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => storage.GetSignedUrlAsync("t/s/k.pdf", TimeSpan.Zero));
    }

    [Fact]
    public async Task S3ListKeysAsync_PagesAndIgnoresQuarantine()
    {
        var tenantId = Guid.NewGuid();

        var s3 = new Mock<IAmazonS3>();
        s3.SetupSequence(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                IsTruncated = true,
                NextContinuationToken = "pagina-2",
                S3Objects =
                [
                    new S3Object { Key = $"{tenantId}/a/a.pdf" },
                    new S3Object { Key = $"quarantine/{tenantId}/suspeito.pdf" },
                ],
            })
            .ReturnsAsync(new ListObjectsV2Response
            {
                IsTruncated = false,
                S3Objects = [new S3Object { Key = $"{tenantId}/b/b.pdf" }],
            });

        var keys = await CreateS3(s3).ListKeysAsync(tenantId);

        Assert.Equal(new[] { $"{tenantId}/a/a.pdf", $"{tenantId}/b/b.pdf" }, keys);
        s3.Verify(
            x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(request => request.ContinuationToken == "pagina-2"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
            Directory.Delete(_rootPath, recursive: true);
    }

    private static AmazonS3Exception NotFound() =>
        new("The specified key does not exist.") { StatusCode = HttpStatusCode.NotFound };

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        await using var source = stream;
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - (padded.Length % 4)) % 4);
        return Convert.FromBase64String(padded);
    }

    private async Task<string> WriteLegacyPlaintextAsync(byte[] content)
    {
        var dir = Path.Combine(_rootPath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "legado.pdf");
        await File.WriteAllBytesAsync(path, content);

        return path;
    }

    private async Task<string> WriteVersionOneAsync(byte[] content)
    {
        var dir = Path.Combine(_rootPath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "v1.pdf");
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertext = new byte[content.Length];
        RandomNumberGenerator.Fill(nonce);

        using (var aes = new AesGcm(Key, TagSize))
            aes.Encrypt(nonce, content, ciphertext, tag);

        byte[] container = [.. "CICLOENC"u8.ToArray(), .. nonce, .. tag, .. ciphertext];
        await File.WriteAllBytesAsync(path, container);

        return path;
    }

    /// <summary>
    /// Stream sem busca, sem <c>Length</c> e sem <c>Position</c>: prova que a gravação não exige
    /// conhecer o tamanho nem voltar ao início da origem.
    /// </summary>
    private sealed class ForwardOnlyStream : Stream
    {
        private readonly byte[] _data;
        private int _offset;

        public ForwardOnlyStream(byte[] data) => _data = data;

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

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ReadCore(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int ReadCore(Span<byte> destination)
        {
            var available = Math.Min(destination.Length, _data.Length - _offset);
            _data.AsSpan(_offset, available).CopyTo(destination);
            _offset += available;

            return available;
        }
    }
}
