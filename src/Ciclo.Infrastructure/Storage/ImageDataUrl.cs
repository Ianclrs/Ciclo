namespace Ciclo.Infrastructure.Storage;

/// <summary>
/// Validação de foto enviada como data URL (`data:image/...;base64,...`) — M6.
///
/// A validação anterior aceitava qualquer prefixo `data:image/`, o que incluía
/// `data:image/svg+xml` — SVG executa script quando aberto no navegador. Agora há uma lista
/// fechada de formatos raster e a assinatura do conteúdo é conferida contra o prefixo.
/// </summary>
public static class ImageDataUrl
{
    /// <summary>Tamanho máximo da string (data URL), equivalente a ~150 KB de imagem.</summary>
    public const int MaxLength = 200_000;

    private const int Base64HeaderChars = 16; // múltiplo de 4 → 12 bytes, o bastante para WebP

    private static readonly (string Prefix, FileKind Kind)[] Allowed =
    [
        ("data:image/png;base64,", FileKind.Png),
        ("data:image/jpeg;base64,", FileKind.Jpeg),
        ("data:image/webp;base64,", FileKind.WebP)
    ];

    /// <summary>Verdadeiro apenas para PNG, JPEG ou WebP com assinatura coerente com o prefixo.</summary>
    public static bool IsSupported(string dataUrl)
    {
        foreach (var (prefix, kind) in Allowed)
        {
            if (!dataUrl.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var payload = dataUrl.AsSpan(prefix.Length);

            // Até 16 caracteres (12 bytes), ajustados ao grupo de 4 do Base64. O mínimo de um
            // grupo permite PNG (8 bytes) e JPEG (3 bytes); WebP (12 bytes) exige os 16.
            var take = Math.Min(payload.Length, Base64HeaderChars);
            take -= take % 4;
            if (take < 4)
                return false;

            byte[] header;
            try
            {
                header = Convert.FromBase64String(payload[..take].ToString());
            }
            catch (FormatException)
            {
                return false;
            }

            return FileSignature.Detect(header) == kind;
        }

        return false;
    }
}
