import api from './client';
import type {
  Enrollment,
  EnrollmentPeriod,
  PagedResponse,
  RenewalBatchRequest,
  RenewalBatchResult,
  RenewalPreview,
} from '../types';

export async function getEnrollments(params: {
  periodId?: string;
  status?: string;
  page?: number;
  pageSize?: number;
}): Promise<PagedResponse<Enrollment>> {
  const res = await api.get('/api/enrollments', { params });
  return res.data;
}

export async function getEnrollment(id: string): Promise<Enrollment> {
  const res = await api.get(`/api/enrollments/${id}`);
  return res.data;
}

export async function submitEnrollment(id: string): Promise<void> {
  await api.post(`/api/enrollments/${id}/submit`);
}

export async function approveEnrollment(id: string): Promise<void> {
  await api.post(`/api/enrollments/${id}/approve`);
}

export async function cancelEnrollment(id: string): Promise<void> {
  await api.post(`/api/enrollments/${id}/cancel`);
}

export async function reopenEnrollment(id: string): Promise<void> {
  await api.post(`/api/enrollments/${id}/reopen`);
}

export async function rejectEnrollment(id: string, motivo: string): Promise<void> {
  await api.post(`/api/enrollments/${id}/reject`, { motivo });
}

export async function getEnrollmentPeriods(): Promise<EnrollmentPeriod[]> {
  // B1: a resposta é uma lista, não um envelope paginado.
  const res = await api.get('/api/enrollment-periods');
  return res.data;
}

/**
 * Prévia da rematrícula em lote (Spec 160 FR-001). Só lê: não cria nem altera matrícula.
 *
 * O prefixo `/api` vem do próprio módulo (`api` tem `baseURL` vazio ou `VITE_API_URL`), como em
 * todas as chamadas acima — o design escreve `client.get('/enrollments/…')` supondo um cliente
 * com `/api` na base, que não é o desta aplicação.
 */
export async function getRenewalPreview(periodId: string): Promise<RenewalPreview> {
  const res = await api.get('/api/enrollments/renewal-preview', { params: { periodId } });
  return res.data;
}

/** Executa o lote (Spec 160 FR-002). Idempotente por aluno no servidor. */
export async function renewEnrollmentBatch(
  request: RenewalBatchRequest,
): Promise<RenewalBatchResult> {
  const res = await api.post('/api/enrollments/renewal-batch', request);
  return res.data;
}
