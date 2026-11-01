import api from './client';

/**
 * Cliente da superfície de privacidade e LGPD (Spec 210, FR-005 e FR-006).
 *
 * Os enums viajam como o nome do membro (`ConsentDto.SubjectType` é `string` na API), então
 * acrescentar um valor numérico novo no backend não quebra esta tela.
 */

export type ConsentSubjectType = 'Student' | 'Guardian';
export type ConsentStatus = 'Granted' | 'Revoked';
export type PrivacyRequestType = 'Export' | 'Anonymization' | 'ConsentRevocation';
export type PrivacyRequestStatus = 'Submitted' | 'Approved' | 'Rejected' | 'Completed';

export interface Consent {
  id: string;
  subjectType: ConsentSubjectType;
  subjectId: string;
  purpose: string;
  legalBasis: string;
  status: ConsentStatus;
  grantedAt: string;
  revokedAt: string | null;
}

export interface PrivacyRequest {
  id: string;
  type: PrivacyRequestType;
  status: PrivacyRequestStatus;
  subjectStudentId: string;
  requestedByUserId: string;
  reason: string | null;
  resolution: string | null;
  legalHold: boolean;
  retainUntil: string | null;
  submittedAt: string;
  decidedAt: string | null;
  completedAt: string | null;
}

export interface PrivacyRequestCompletion {
  request: PrivacyRequest;
  export: unknown | null;
}

export interface CreatePrivacyRequestRequest {
  studentId: string;
  type: PrivacyRequestType;
  reason?: string | null;
}

export interface DecidePrivacyRequestRequest {
  decision: 'approve' | 'reject';
  resolution?: string | null;
  legalHold: boolean;
  retainUntil?: string | null;
}

export async function getPrivacyRequests(): Promise<PrivacyRequest[]> {
  const res = await api.get('/api/privacy/requests');
  return res.data;
}

export async function getConsents(
  subjectId: string,
  subjectType: ConsentSubjectType = 'Student',
): Promise<Consent[]> {
  const res = await api.get('/api/privacy/consents', { params: { subjectId, subjectType } });
  return res.data;
}

export async function submitPrivacyRequest(
  request: CreatePrivacyRequestRequest,
): Promise<PrivacyRequest> {
  const res = await api.post('/api/privacy/requests', request);
  return res.data;
}

export async function decidePrivacyRequest(
  id: string,
  decision: DecidePrivacyRequestRequest,
): Promise<PrivacyRequest> {
  const res = await api.post(`/api/privacy/requests/${id}/decision`, decision);
  return res.data;
}

export async function completePrivacyRequest(id: string): Promise<PrivacyRequestCompletion> {
  const res = await api.post(`/api/privacy/requests/${id}/complete`);
  return res.data;
}

/**
 * Documento do titular como `Blob`. A rota responde um arquivo JSON, então o corpo não é
 * desserializado: quem chama decide se baixa ou só confere o tamanho.
 */
export async function downloadStudentExport(studentId: string): Promise<Blob> {
  const res = await api.get(`/api/privacy/students/${studentId}/export`, { responseType: 'blob' });
  return res.data;
}
