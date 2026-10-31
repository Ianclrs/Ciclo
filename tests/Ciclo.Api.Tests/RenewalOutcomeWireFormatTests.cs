using System.Text.Json;
using Ciclo.Infrastructure.Contracts;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 160, FR-003 — o desfecho do lote cruza a fronteira HTTP e o frontend declara a união
/// literal <c>'Renewed' | 'SkippedAlreadyExists' | 'BlockedPendingDocuments' | 'Failed'</c>.
///
/// A API não configura conversor global de enum; sem o <c>JsonStringEnumConverter</c> no próprio
/// <see cref="RenewalOutcome"/>, o campo sairia como <c>0..3</c>, o TypeScript não reclamaria
/// (o compilador confia na declaração) e a tela mostraria o rótulo indefinido. Este teste trava
/// o formato do fio sem subir host.
/// </summary>
public class RenewalOutcomeWireFormatTests
{
    /// <summary>
    /// Mesma política de nomes que o MVC aplica (`camelCase`): o teste mede o JSON que o cliente
    /// realmente recebe, não o padrão do `JsonSerializer`.
    /// </summary>
    private static readonly JsonSerializerOptions MvcLikeOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void RenewalOutcome_IsSerializedAsText_NotAsNumber()
    {
        var item = new RenewalBatchItemResultDto(
            Guid.NewGuid(), "Ana Clara", RenewalOutcome.SkippedAlreadyExists, "já matriculado");

        var json = JsonSerializer.Serialize(item, MvcLikeOptions);

        Assert.Contains("\"outcome\":\"SkippedAlreadyExists\"", json);
        Assert.DoesNotContain("\"outcome\":1", json);
    }

    [Theory]
    [InlineData(RenewalOutcome.Renewed, "Renewed")]
    [InlineData(RenewalOutcome.SkippedAlreadyExists, "SkippedAlreadyExists")]
    [InlineData(RenewalOutcome.BlockedPendingDocuments, "BlockedPendingDocuments")]
    [InlineData(RenewalOutcome.Failed, "Failed")]
    public void EveryOutcome_CrossesTheWireWithItsName(RenewalOutcome outcome, string expected)
    {
        Assert.Equal($"\"{expected}\"", JsonSerializer.Serialize(outcome));
    }
}
