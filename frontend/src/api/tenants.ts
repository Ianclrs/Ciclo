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
