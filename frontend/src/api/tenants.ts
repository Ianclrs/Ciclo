import api from './client';
import type { TenantSummary } from '../types';

export interface RegisterSchoolPayload {
  schoolName: string;
  country: string;
  documentoFiscal?: string;
  telefone?: string;
  endereco?: string;
  cidade?: string;
  estado?: string;
  cep?: string;
  adminName: string;
  adminEmail: string;
  adminPassword: string;
}

export interface RegisterSchoolResponse {
  tenantId: string;
  slug: string;
  country: string;
}

export async function registerSchool(payload: RegisterSchoolPayload): Promise<RegisterSchoolResponse> {
  const res = await api.post('/api/tenants/register', payload);
  return res.data;
}

/** Colégio do usuário logado (chip de identificação na topbar). */
export async function getCurrentTenant(): Promise<TenantSummary> {
  const res = await api.get('/api/tenants/me');
  return res.data;
}

/** Configuração da escola (Spec 200, FR-006). Espelha `TenantConfigurationDto`. */
export type TenantConfiguration = {
  tenantId: string;
  displayName: string;
  logoUrl: string | null;
  primaryColor: string | null;
  secondaryColor: string | null;
  timeZone: string;
  defaultSchoolYear: number;
  locale: string;
  storageQuotaBytes: number;
  userQuota: number;
  plan: string;
  featureFlags: string[];
};

/**
 * Atualização parcial: campo ausente preserva o valor atual na API (ADR-200-3), então a tela
 * envia apenas o que mudou. Texto em branco limpa `displayName`, `logoUrl` e as cores.
 */
export type UpdateTenantConfiguration = Partial<Omit<TenantConfiguration, 'tenantId'>>;

export async function getTenantConfiguration(): Promise<TenantConfiguration> {
  const res = await api.get('/api/tenants/me/configuration');
  return res.data;
}

export async function updateTenantConfiguration(
  payload: UpdateTenantConfiguration,
): Promise<TenantConfiguration> {
  const res = await api.put('/api/tenants/me/configuration', payload);
  return res.data;
}
