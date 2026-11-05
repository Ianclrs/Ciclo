/**
 * Vocabulário das funcionalidades do tenant — espelho de `TenantFeature` na API
 * (`src/Ciclo.Core/Entities/TenantFeature.cs`). O valor é o identificador persistido; o texto
 * de cada funcionalidade vive no catálogo de mensagens.
 *
 * Este módulo é o dono único da lista no frontend: telas que precisam perguntar "esta escola tem
 * a funcionalidade X?" passam por aqui (via `useTenantFeatures`) em vez de comparar strings soltas.
 */
export const TENANT_FEATURES = [
  'Documents',
  'Notifications',
  'ParentPortal',
  'AcademicPerformance',
  'BatchReenrollment',
  'AuditExport',
] as const;

export type TenantFeatureName = (typeof TENANT_FEATURES)[number];

const KNOWN = new Set<string>(TENANT_FEATURES);

/** Estreita uma string vinda da API para o vocabulário conhecido. */
export function isTenantFeature(value: string): value is TenantFeatureName {
  return KNOWN.has(value);
}

/**
 * Converte a lista devolvida pela API descartando valor desconhecido. Uma flag nova no backend
 * não pode quebrar o frontend antigo — ela apenas não terá efeito até existir aqui.
 */
export function parseTenantFeatures(
  values: readonly string[] | null | undefined,
): TenantFeatureName[] {
  if (!values) return [];
  return values.filter(isTenantFeature);
}
