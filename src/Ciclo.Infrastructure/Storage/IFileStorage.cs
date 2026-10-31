namespace Ciclo.Infrastructure.Storage;

/// <summary>
/// Contrato único de armazenamento de documentos. O valor devolvido por
/// <see cref="SaveAsync"/> e <see cref="SaveQuarantineAsync"/> é exatamente a chave aceita por
/// <see cref="GetAsync"/>, <see cref="OpenReadAsync"/>, <see cref="ExistsAsync"/>,
/// <see cref="DeleteAsync"/>, <see cref="ReencryptAsync"/>, <see cref="GetSignedUrlAsync"/> e
/// devolvida por <see cref="ListKeysAsync"/>.
/// </summary>
public interface IFileStorage
{
    Task<string> SaveAsync(Guid tenantId, Guid studentId, string fileName, Stream content);
    Task<Stream> GetAsync(string filePath);
    Task DeleteAsync(string filePath);

    /// <summary>
    /// Reescreve o arquivo no MESMO caminho usando a chave ATUAL (rotação de chave).
    /// Arquivos gravados com uma chave anterior são decifrados com o keyring e regravados;
    /// arquivos legados sem criptografia passam a ser criptografados. Idempotente.
    /// </summary>
    Task ReencryptAsync(string filePath);

    /// <summary>Verdadeiro quando a chave existe no armazenamento.</summary>
    Task<bool> ExistsAsync(string filePath);

    /// <summary>Leitura em stream, sem materializar o conteúdo inteiro em memória.</summary>
    Task<Stream> OpenReadAsync(string filePath);

    /// <summary>URL de download válida por <paramref name="lifetime"/>.</summary>
    Task<string> GetSignedUrlAsync(string filePath, TimeSpan lifetime);

    /// <summary>Retém o conteúdo suspeito na área de quarentena e devolve a chave gravada.</summary>
    Task<string> SaveQuarantineAsync(Guid tenantId, string fileName, Stream content);

    /// <summary>Chaves gravadas no escopo do tenant (todas quando <paramref name="tenantId"/> é nulo).</summary>
    Task<IReadOnlyList<string>> ListKeysAsync(Guid? tenantId);
}
