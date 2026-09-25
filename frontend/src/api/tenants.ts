import api from './client';

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
  const res = await api.post('/tenants/register', payload);
  return res.data;
}
