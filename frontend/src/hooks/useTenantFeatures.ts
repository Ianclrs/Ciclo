import { useEffect, useState } from 'react';
import { getTenantConfiguration } from '../api/tenants';
import { parseTenantFeatures, type TenantFeatureName } from '../domain/features';

/**
 * Funcionalidades ligadas para a escola do usuário logado.
 *
 * Espelha o portão da API (`[RequiresFeature]`): esconder o item de menu é conveniência, não
 * segurança — quem abre a rota direto recebe 403 `feature_disabled` do servidor.
 *
 * Duas escolhas deliberadas:
 * - **Falha abre.** Sem resposta da API (modo demonstração, rede, 500) nada é escondido: o menu
 *   não pode desaparecer por causa de uma leitura que falhou. A API continua sendo o portão.
 * - **Uma busca por escola.** O cache é por `tenantId`, então trocar de sessão com outra escola
 *   refaz a leitura em vez de reaproveitar as funcionalidades da anterior.
 */

interface CacheEntry {
  tenantId: string;
  features: TenantFeatureName[];
}

let cache: CacheEntry | null = null;
let pending: { tenantId: string; promise: Promise<TenantFeatureName[]> } | null = null;

async function load(tenantId: string): Promise<TenantFeatureName[]> {
  if (cache?.tenantId === tenantId) return cache.features;
  if (pending?.tenantId === tenantId) return pending.promise;

  const promise = getTenantConfiguration()
    .then((config) => {
      const features = parseTenantFeatures(config.featureFlags);
      cache = { tenantId, features };
      return features;
    })
    .finally(() => {
      if (pending?.tenantId === tenantId) pending = null;
    });

  pending = { tenantId, promise };
  return promise;
}

/** Esquece o que foi lido — o logout não pode herdar as funcionalidades da sessão anterior. */
export function clearTenantFeatures(): void {
  cache = null;
  pending = null;
}

export interface TenantFeaturesState {
  /** `null` enquanto a leitura não concluiu, ou quando ela falhou. */
  features: TenantFeatureName[] | null;
  /** `true` quando a funcionalidade está ligada — ou quando ainda não se sabe. */
  enabled: (feature: TenantFeatureName) => boolean;
}

export function useTenantFeatures(tenantId: string | null | undefined): TenantFeaturesState {
  const [loaded, setLoaded] = useState<CacheEntry | null>(() =>
    tenantId && cache?.tenantId === tenantId ? cache : null,
  );

  useEffect(() => {
    if (!tenantId) return;

    let cancelled = false;

    load(tenantId)
      .then((features) => {
        if (!cancelled) setLoaded({ tenantId, features });
      })
      .catch(() => {
        // Falha abre: sem lista conhecida, nada é escondido (a API decide de fato).
        if (!cancelled) setLoaded(null);
      });

    return () => {
      cancelled = true;
    };
  }, [tenantId]);

  // Só vale a lista da escola atual — trocar de tenant volta a "não sei" sem precisar de estado
  // intermediário nem de um `setState` síncrono dentro do efeito.
  const features = loaded !== null && loaded.tenantId === tenantId ? loaded.features : null;

  return {
    features,
    enabled: (feature) => (features === null ? true : features.includes(feature)),
  };
}
