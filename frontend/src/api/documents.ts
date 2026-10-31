import api from './client';
import type { Document, PagedResponse, DocumentType } from '../types';

export async function getPendingDocuments(params: {
  page?: number;
  pageSize?: number;
}): Promise<PagedResponse<Document>> {
  const res = await api.get('/api/documents/pending', { params });
  return res.data;
}

/** A6b: listagem global filtrável por status, aluno, tipo e nome do aluno. */
export interface DocumentFilters {
  status?: string;
  studentId?: string;
  documentTypeId?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

export async function getDocuments(
  params: DocumentFilters,
): Promise<PagedResponse<Document>> {
  const res = await api.get('/api/documents', { params });
  return res.data;
}

export async function getStudentDocuments(studentId: string, params: {
  page?: number;
  pageSize?: number;
}): Promise<PagedResponse<Document>> {
  const res = await api.get(`/api/students/${studentId}/documents`, { params });
  return res.data;
}

// A6: `GET /api/documents/expiring` existia desde a V7 e não era consumido por nenhuma tela.
// Devolve lista simples (não paginada) — o contrato do servidor é `List<DocumentDto>`.
export async function getExpiringDocuments(days = 30): Promise<Document[]> {
  const res = await api.get('/api/documents/expiring', { params: { days } });
  return res.data;
}

export async function uploadDocument(formData: FormData): Promise<Document> {
  const res = await api.post('/api/documents/upload', formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
  return res.data;
}

export async function verifyDocument(id: string, approved: boolean, motivoRejeicao?: string): Promise<void> {
  await api.post(`/api/documents/${id}/verify`, { approved, motivoRejeicao });
}

export async function getDocumentTypes(): Promise<DocumentType[]> {
  const res = await api.get('/api/document-types');
  return res.data;
}

/** Emite a URL assinada de download; a validade é de cinco minutos. */
export async function requestDownloadUrl(id: string): Promise<{ url: string; expiresAt: string }> {
  const res = await api.get(`/api/documents/${id}/download-url`);
  return res.data;
}

/** Baixa pela URL assinada, sem passar pelo cabeçalho de autorização. */
export async function downloadDocument(id: string): Promise<Blob> {
  const { url } = await requestDownloadUrl(id);
  const res = await fetch(url);
  if (!res.ok) throw new Error('download_failed');
  return res.blob();
}
