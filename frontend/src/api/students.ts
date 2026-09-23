import api from './client';
import type { Student, PagedResponse } from '../types';

export interface CreateStudentRequest {
  nome: string;
  dataNascimento: string;
  cpf?: string;
  turma: string;
  anoLetivo: number;
  observacoes?: string;
  /** Data URL (Base64) já redimensionada no navegador. Omitido remove a foto. */
  foto?: string;
}

export async function getStudents(params: {
  search?: string;
  turma?: string;
  status?: string;
  page?: number;
  pageSize?: number;
}): Promise<PagedResponse<Student>> {
  const res = await api.get('/students', { params });
  return res.data;
}

export async function getStudent(id: string): Promise<Student> {
  const res = await api.get(`/students/${id}`);
  return res.data;
}

export async function createStudent(data: CreateStudentRequest): Promise<Student> {
  const res = await api.post('/students', data);
  return res.data;
}

export async function updateStudent(id: string, data: CreateStudentRequest): Promise<Student> {
  const res = await api.put(`/students/${id}`, data);
  return res.data;
}

export async function deleteStudent(id: string): Promise<void> {
  await api.delete(`/students/${id}`);
}

export async function linkParent(studentId: string, parentId: string): Promise<void> {
  await api.post(`/students/${studentId}/link-parent`, { parentId });
}

export async function unlinkParent(studentId: string, parentId: string): Promise<void> {
  await api.delete(`/students/${studentId}/link-parent/${parentId}`);
}
