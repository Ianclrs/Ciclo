using System.Net;
using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Ciclo.Infrastructure.Storage;

/// <summary>
/// Driver S3-compatível. A chave é o próprio valor devolvido por <see cref="SaveAsync"/> e
/// aceito pelos demais membros do contrato, exatamente como no driver local — só que aqui ela
/// é a chave do objeto no bucket, não um caminho de disco.
/// </summary>
public class S3FileStorage : IFileStorage
{
    private const string QuarantinePrefix = "quarantine/";

    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public S3FileStorage(IAmazonS3 s3, IOptions<FileStorageOptions> options)
    {
        _s3 = s3;

        if (string.IsNullOrWhiteSpace(options.Value.S3Bucket))
            throw new InvalidOperationException("FileStorage:S3Bucket is required for the S3 provider.");

        _bucket = options.Value.S3Bucket;
    }

    public async Task<string> SaveAsync(Guid tenantId, Guid studentId, string fileName, Stream content)
    {
        var key = $"{tenantId}/{studentId}/{Guid.NewGuid():N}_{SanitizeFileName(fileName)}";
        await PutAsync(key, content);

        return key;
    }

    /// <summary>Retém o conteúdo suspeito fora do escopo de documentos e devolve a chave gravada.</summary>
    public async Task<string> SaveQuarantineAsync(Guid tenantId, string fileName, Stream content)
    {
        var key = $"{QuarantinePrefix}{tenantId}/{Guid.NewGuid():N}_{SanitizeFileName(fileName)}";
        await PutAsync(key, content);

        return key;
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

    public async Task<Stream> OpenReadAsync(string filePath)
    {
        try
        {
            var response = await _s3.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucket,
                Key = filePath,
            });

            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("File not found.", filePath);
        }
    }

    public async Task<bool> ExistsAsync(string filePath)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _bucket,
                Key = filePath,
            });

            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task DeleteAsync(string filePath)
    {
        // DeleteObject é idempotente no S3: apagar uma chave ausente não é erro.
        await _s3.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucket,
            Key = filePath,
        });
    }

    /// <summary>
    /// No-op documentado: a criptografia em repouso é responsabilidade do provedor de objetos
    /// (SSE do bucket). O membro só confirma que a chave existe, como o contrato exige.
    /// </summary>
    public async Task ReencryptAsync(string filePath)
    {
        if (!await ExistsAsync(filePath))
            throw new FileNotFoundException("File not found.", filePath);
    }

    public Task<string> GetSignedUrlAsync(string filePath, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(lifetime), lifetime, "The signed URL lifetime must be positive.");

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = filePath,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(lifetime),
        };

        return Task.FromResult(_s3.GetPreSignedURL(request));
    }

    /// <summary>
    /// Chaves do escopo listadas no bucket. A área de quarentena fica fora da listagem para que
    /// a varredura de órfãos nunca a trate como arquivo de documento.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListKeysAsync(Guid? tenantId)
    {
        var prefix = tenantId is null ? string.Empty : $"{tenantId}/";
        var keys = new List<string>();
        string? continuationToken = null;

        do
        {
            var response = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken,
            });

            keys.AddRange(response.S3Objects
                .Select(item => item.Key)
                .Where(key => !key.StartsWith(QuarantinePrefix, StringComparison.Ordinal)));

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (continuationToken is not null);

        return keys;
    }

    private async Task PutAsync(string key, Stream content)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = "application/octet-stream",
        };

        // S3 exige Content-Length; streams somente-avanço não o informam, então o comprimento
        // só é enviado quando o stream permite descobri-lo sem consumir o conteúdo.
        if (content.CanSeek)
            request.Headers.ContentLength = content.Length;

        await _s3.PutObjectAsync(request);
    }

    private static string SanitizeFileName(string fileName)
    {
        // Mesma regra do driver local: só alfanumérico, ponto, hífen e underscore.
        var safe = Regex.Replace(fileName, @"[^a-zA-Z0-9._-]", "_");
        return string.IsNullOrWhiteSpace(safe) ? "unnamed" : safe;
    }
}
