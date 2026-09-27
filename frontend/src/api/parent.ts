import api from './client';
import type { Student, Document, ParentDashboard, ChildDetail, Grade } from '../types';

export async function getDashboard(): Promise<ParentDashboard> {
  const res = await api.get('/api/parent/dashboard');
  return res.data;
}

export async function getChildren(): Promise<Student[]> {
  const res = await api.get('/api/parent/children');
  return res.data;
}

export async function getChildDetail(id: string): Promise<ChildDetail> {
  const res = await api.get(`/api/parent/children/${id}`);
  return res.data;
}

export async function getChildDocuments(id: string): Promise<Document[]> {
  const res = await api.get(`/api/parent/children/${id}/documents`);
  return res.data;
}

export async function uploadChildDocument(id: string, formData: FormData): Promise<Document> {
  const res = await api.post(`/api/parent/children/${id}/documents/upload`, formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
  return res.data;
}

export async function getChildGrades(id: string): Promise<Grade[]> {
  const res = await api.get(`/api/parent/children/${id}/grades`);
  return res.data;
}
