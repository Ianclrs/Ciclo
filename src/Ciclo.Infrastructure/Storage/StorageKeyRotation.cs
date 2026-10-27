using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ciclo.Infrastructure.Data;

namespace Ciclo.Infrastructure.Storage;

/// <summary>Resultado da rotação: o operador precisa saber o que ficou para trás.</summary>
public sealed record StorageRotationResult(int Rotated, int MissingFiles, int Failed);

/// <summary>
/// Re-encriptação de todos os documentos com a chave ATUAL do armazenamento.
///
/// Existe porque trocar a chave sem reescrever os arquivos torna os documentos ilegíveis.
/// O procedimento (README, "Rotação de segredos") é: gerar a chave nova, mantê-la como
/// `FileStorage:EncryptionKey`, mover a antiga para `FileStorage:PreviousEncryptionKeys`,
/// rodar este comando e só então remover a chave antiga.
///
/// O comando é idempotente: rodar de novo apenas regrava o que já está na chave atual.
/// Falhas são contadas, não engolidas — o processo termina com código de erro se alguma
/// ficar para trás.
/// </summary>
public sealed class StorageKeyRotation
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ILogger<StorageKeyRotation> _logger;

    public StorageKeyRotation(
        AppDbContext db, IFileStorage storage, ILogger<StorageKeyRotation> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    public async Task<StorageRotationResult> RotateAsync(CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: a rotação é uma operação de manutenção e precisa cobrir TODOS
        // os tenants, não só o do contexto corrente (que aqui nem existe).
        var paths = await _db.Documents
            .IgnoreQueryFilters()
            .Select(document => document.CaminhoArquivo)
            .Distinct()
            .ToListAsync(cancellationToken);

        var rotated = 0;
        var missing = 0;
        var failed = 0;

        foreach (var path in paths)
        {
            try
            {
                await _storage.ReencryptAsync(path);
                rotated++;
            }
            catch (FileNotFoundException)
            {
                // Registro órfão (arquivo já removido do disco): não impede os demais.
                missing++;
#pragma warning disable CA1848, CA1873
                _logger.LogWarning("Storage rotation: file not found for {FilePath}", path);
#pragma warning restore CA1848, CA1873
            }
            catch (CryptographicException exception)
            {
                // Nenhuma chave configurada decifra o arquivo — sinal de chave antiga faltando.
                failed++;
#pragma warning disable CA1848, CA1873
                _logger.LogError(exception, "Storage rotation: cannot decrypt {FilePath}", path);
#pragma warning restore CA1848, CA1873
            }
        }

        return new StorageRotationResult(rotated, missing, failed);
    }
}
