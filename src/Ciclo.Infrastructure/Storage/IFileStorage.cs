namespace Ciclo.Infrastructure.Storage;

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
}
