using Ciclo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ciclo.Infrastructure.Storage;

/// <summary>
/// Resultado de uma varredura: o operador precisa saber o que foi encontrado e o que falhou.
/// <see cref="Failures"/> contém a chave e a mensagem de cada chave que não pôde ser processada.
/// </summary>
public sealed record OrphanScanResult(
    int Scanned, int Orphaned, int Deleted, IReadOnlyList<string> Failures);

/// <summary>
/// Compara as chaves gravadas no armazenamento com as chaves referenciadas por <c>Document</c>.
///
/// Uma chave sem registro é lógica morta: ocupa disco, nunca é servida e não aparece em nenhuma
/// tela. A varredura é uma operação de manutenção (executada por
/// <c>--scan-orphan-files</c>), portanto lê TODOS os tenants; a remoção só acontece quando
/// pedida explicitamente, e a falha de uma chave é isolada — as demais continuam.
/// </summary>
public sealed class OrphanFileScanner
{
    private readonly IFileStorage _storage;
    private readonly AppDbContext _db;
    private readonly ILogger<OrphanFileScanner> _logger;

    public OrphanFileScanner(
        IFileStorage storage, AppDbContext db, ILogger<OrphanFileScanner> logger)
    {
        _storage = storage;
        _db = db;
        _logger = logger;
    }

    public async Task<OrphanScanResult> ScanAsync(
        Guid? tenantId, bool deleteOrphans, CancellationToken cancellationToken = default)
    {
        var keys = await _storage.ListKeysAsync(tenantId);
        if (keys.Count == 0)
        {
            return new OrphanScanResult(0, 0, 0, []);
        }

        // IgnoreQueryFilters: manutenção cobre todos os tenants, e o escopo do comando não tem
        // tenant corrente (o filtro global reduziria o conjunto referenciado a zero e toda chave
        // viraria órfã).
        var referencedQuery = _db.Documents
            .IgnoreQueryFilters()
            .AsQueryable();

        if (tenantId is Guid scopedTenantId)
        {
            referencedQuery = referencedQuery.Where(document => document.TenantId == scopedTenantId);
        }

        var referenced = (await referencedQuery
                .Select(document => document.CaminhoArquivo)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var orphans = keys
            .Where(key => !referenced.Contains(key))
            .ToList();

        var deleted = 0;
        var failures = new List<string>();

        if (deleteOrphans)
        {
            foreach (var orphan in orphans)
            {
                try
                {
                    await _storage.DeleteAsync(orphan);
                    deleted++;
                }
                catch (Exception exception)
                {
                    // Uma chave problemática não pode interromper a limpeza das demais.
                    failures.Add($"{orphan}: {exception.Message}");
#pragma warning disable CA1848, CA1873
                    _logger.LogError(exception, "Orphan file scan: cannot delete {Key}", orphan);
#pragma warning restore CA1848, CA1873
                }
            }
        }

        return new OrphanScanResult(keys.Count, orphans.Count, deleted, failures);
    }
}
