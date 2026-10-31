using System.Security.Cryptography;

namespace Ciclo.Infrastructure.Storage;

/// <summary>
/// Decorador de leitura que calcula SHA-256 e o total de bytes consumidos enquanto o driver
/// grava o conteúdo (AD-170-3). Não exige stream pesquisável nem <c>Length</c> e nunca
/// materializa o arquivo: o hash avança bloco a bloco (NFR-001).
/// </summary>
internal sealed class HashingReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long? _maxBytes;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public HashingReadStream(Stream inner, long? maxBytes = null)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        _maxBytes = maxBytes;
    }

    /// <summary>Total de bytes entregues pelo stream até agora.</summary>
    public long BytesRead { get; private set; }

    /// <summary>SHA-256 do conteúdo consumido, em 64 hexadecimais maiúsculos.</summary>
    public string HashHex => Convert.ToHexString(_hash.GetCurrentHash());

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer[..Limit(buffer.Length)]);
        Track(buffer[..read]);

        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer[..Limit(buffer.Length)], cancellationToken).ConfigureAwait(false);
        Track(buffer.Span[..read]);

        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    // O stream interno pertence ao chamador: Dispose aqui não fecha a origem.
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _hash.Dispose();

        base.Dispose(disposing);
    }

    /// <summary>
    /// Nunca pede mais que o limite restante + 1 byte: o excesso é detectado sem consumir
    /// o restante do conteúdo.
    /// </summary>
    private int Limit(int requested)
    {
        if (_maxBytes is not { } max)
            return requested;

        var remaining = max - BytesRead;
        if (remaining < 0)
            throw new InvalidDataException("file_too_large");

        return (int)Math.Min(requested, remaining + 1);
    }

    private void Track(ReadOnlySpan<byte> data)
    {
        var total = BytesRead + data.Length;

        if (_maxBytes is { } max && total > max)
            throw new InvalidDataException("file_too_large");

        if (total == BytesRead)
            return;

        _hash.AppendData(data);
        BytesRead = total;
    }
}
