import api from './client';
import type { Enrollment, EnrollmentPeriod, PagedResponse } from '../types';

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

export async function rejectEnrollment(id: string, motivo: string): Promise<void> {
  await api.post(`/api/enrollments/${id}/reject`, { motivo });
}

export async function getEnrollmentPeriods(): Promise<EnrollmentPeriod[]> {
  const res = await api.get('/api/enrollment-periods');
  return res.data.items;
}
