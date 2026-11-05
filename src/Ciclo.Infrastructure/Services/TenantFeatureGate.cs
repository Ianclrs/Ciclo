using Ciclo.Core.Entities;

namespace Ciclo.Infrastructure.Services;

/// <summary>
/// Portão das funcionalidades do tenant (ADR-200-2): responde "esta escola tem a flag X ligada?".
///
/// A resposta vem da configuração já cacheada por <see cref="ITenantService"/> — uma leitura de
/// cache por requisição, sem tabela nova e sem um segundo cache que pudesse divergir do primeiro.
/// </summary>
public interface ITenantFeatureGate
{
    /// <summary>Indica se o tenant tem a funcionalidade ligada. Flag desconhecida nunca está ligada.</summary>
    Task<bool> IsEnabledAsync(Guid tenantId, string feature);
}

public sealed class TenantFeatureGate : ITenantFeatureGate
{
    private readonly ITenantService _tenantService;

    public TenantFeatureGate(ITenantService tenantService)
    {
        _tenantService = tenantService;
    }

    public async Task<bool> IsEnabledAsync(Guid tenantId, string feature)
    {
        // Funcionalidade vazia não é uma pergunta válida: negar aqui evitava que um atributo
        // mal escrito (`[RequiresFeature("")]`) liberasse tudo por acidente.
        if (string.IsNullOrWhiteSpace(feature) || !TenantFeature.All.Contains(feature))
            return false;

        var configuration = await _tenantService.GetConfigurationAsync(tenantId);

        // Escola que nunca salvou a configuração tem a lista vazia. A coluna hoje não distingue
        // "nunca configurado" de "desligaram tudo", então tratar vazio como "nada ligado"
        // derrubaria funcionalidades que já funcionavam em toda escola anterior a este portão.
        // Enquanto o vazio for ambíguo, ele significa "sem configuração" — e a tela de
        // configurações recusa salvar um conjunto vazio justamente para não criar a ambiguidade.
        if (configuration.FeatureFlags.Count == 0)
            return true;

        return configuration.FeatureFlags.Contains(feature, StringComparer.Ordinal);
    }
}
