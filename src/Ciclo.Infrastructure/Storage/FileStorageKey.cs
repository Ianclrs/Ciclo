namespace Ciclo.Infrastructure.Storage;

/// <summary>
/// Chave AES-256 do armazenamento de documentos, com validação fail-closed.
///
/// Diferente do segredo de JWT, esta chave protege <b>dados em repouso</b>: trocá-la torna
/// ilegíveis os documentos já gravados com a chave anterior. Por isso ela nunca tem valor
/// padrão no repositório — é obrigatória e explícita por ambiente.
///
/// É o dono único do formato da chave: o host valida em startup (falha antes de servir
/// requisições) e o <see cref="LocalFileStorage"/> usa a mesma validação ao criptografar.
/// </summary>
public static class FileStorageKey
{
    public const int SizeInBytes = 32;

    public const string ConfigurationKey = "FileStorage:EncryptionKey";
    public const string EnvironmentVariable = "FileStorage__EncryptionKey";

    /// <summary>
    /// Decodifica a chave Base64. Lança <see cref="InvalidOperationException"/> quando a chave
    /// está ausente, não é Base64 válido ou não tem exatamente <see cref="SizeInBytes"/> bytes.
    /// </summary>
    public static byte[] Parse(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} is not configured. Set it with " +
                $"`dotnet user-secrets set \"{ConfigurationKey}\" \"<base64>\"` in Development, or through " +
                $"the `{EnvironmentVariable}` environment variable elsewhere. " +
                "The application does not start without it.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} must be a Base64-encoded {SizeInBytes}-byte key (AES-256).", ex);
        }

        if (key.Length != SizeInBytes)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} must decode to exactly {SizeInBytes} bytes (AES-256); " +
                $"the configured value decodes to {key.Length} bytes.");
        }

        return key;
    }
}
