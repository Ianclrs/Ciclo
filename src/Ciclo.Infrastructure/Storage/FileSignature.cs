namespace Ciclo.Infrastructure.Storage;

/// <summary>Tipo real de arquivo, reconhecido pelos bytes iniciais.</summary>
public enum FileKind
{
    Unknown,
    Pdf,
    Jpeg,
    Png,
    WebP
}

/// <summary>
/// Assinatura de arquivo (magic bytes) em um só lugar (M6).
///
/// O upload validava apenas a EXTENSÃO do nome enviado pelo cliente — que é texto que ele
/// controla. Um executável renomeado para `.pdf` passava. A assinatura é a única parte do
/// conteúdo que o cliente não escolhe livremente, então é ela que decide o tipo.
///
/// Não substitui antivírus: detecta o formato declarado, não conteúdo malicioso dentro de um
/// formato válido.
/// </summary>
public static class FileSignature
{
    private const int HeaderLength = 12;

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Reconhece o tipo pelo início do arquivo. O stream precisa ser reposicionável: o cabeçalho
    /// é lido e a posição é restaurada para que o mesmo stream siga para o armazenamento.
    /// </summary>
    public static async Task<FileKind> DetectAsync(Stream stream)
    {
        if (!stream.CanSeek)
            throw new InvalidOperationException("A assinatura exige um stream reposicionável.");

        var position = stream.Position;
        var head = new byte[HeaderLength];
        var read = await stream.ReadAtLeastAsync(head, HeaderLength, throwOnEndOfStream: false);
        stream.Position = position;

        return Detect(head.AsSpan(0, read));
    }

    public static FileKind Detect(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith("%PDF"u8))
            return FileKind.Pdf;

        if (head.StartsWith(Jpeg))
            return FileKind.Jpeg;

        if (head.StartsWith(Png))
            return FileKind.Png;

        // WebP: "RIFF" + 4 bytes de tamanho + "WEBP".
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
            return FileKind.WebP;

        return FileKind.Unknown;
    }

    /// <summary>O tipo detectado corresponde à extensão informada?</summary>
    public static bool MatchesExtension(FileKind kind, string extension)
    {
        var normalized = extension.ToLowerInvariant();

        return kind switch
        {
            FileKind.Pdf => normalized == ".pdf",
            FileKind.Jpeg => normalized is ".jpg" or ".jpeg",
            FileKind.Png => normalized == ".png",
            _ => false
        };
    }
}
