import api from './client';
import type {
  AcademicYear,
  Assessment,
  CurriculumComponent,
  Grade,
  SchoolClass,
  Student,
} from '../types';

/**
 * Cliente tipado das rotas administrativas de estrutura acadêmica e de nota (Spec 150).
 *
 * Cada função monta a rota e devolve o corpo já tipado, como `students.ts`. Erro de API não é
 * tratado aqui: sobe para a tela, que resolve a mensagem com `apiErrorMessage` — o mapa de
 * códigos é o dono único do texto mostrado ao usuário.
 */

// ── Anos letivos ──────────────────────────────────────────────────────────

export interface AcademicYearRequest {
  year: number;
  /** `DateOnly` na API: enviar `YYYY-MM-DD`. */
  startDate: string;
  endDate: string;
  isActive: boolean;
}

export async function listAcademicYears(): Promise<AcademicYear[]> {
  const res = await api.get('/api/academic/years');
  return res.data;
}

export async function createAcademicYear(data: AcademicYearRequest): Promise<AcademicYear> {
  const res = await api.post('/api/academic/years', data);
  return res.data;
}

export async function updateAcademicYear(id: string, data: AcademicYearRequest): Promise<AcademicYear> {
  const res = await api.put(`/api/academic/years/${id}`, data);
  return res.data;
}

export async function deleteAcademicYear(id: string): Promise<void> {
  await api.delete(`/api/academic/years/${id}`);
}

// ── Turmas ────────────────────────────────────────────────────────────────

export interface SchoolClassRequest {
  name: string;
  academicYearId: string;
}

export async function listSchoolClasses(academicYearId?: string): Promise<SchoolClass[]> {
  const res = await api.get('/api/academic/classes', {
    params: academicYearId ? { academicYearId } : undefined,
  });
  return res.data;
}

export async function createSchoolClass(data: SchoolClassRequest): Promise<SchoolClass> {
  const res = await api.post('/api/academic/classes', data);
  return res.data;
}

export async function updateSchoolClass(id: string, data: SchoolClassRequest): Promise<SchoolClass> {
  const res = await api.put(`/api/academic/classes/${id}`, data);
  return res.data;
}

export async function deleteSchoolClass(id: string): Promise<void> {
  await api.delete(`/api/academic/classes/${id}`);
}

/** Roster da turma para a tela de lançamento — o mesmo DTO de aluno do cadastro. */
export async function listClassStudents(id: string): Promise<Student[]> {
  const res = await api.get(`/api/academic/classes/${id}/students`);
  return res.data;
}

// ── Componentes curriculares ──────────────────────────────────────────────

export interface CurriculumComponentRequest {
  name: string;
  code: string;
  isActive: boolean;
}

export async function listComponents(): Promise<CurriculumComponent[]> {
  const res = await api.get('/api/academic/components');
  return res.data;
}

export async function createComponent(data: CurriculumComponentRequest): Promise<CurriculumComponent> {
  const res = await api.post('/api/academic/components', data);
  return res.data;
}

export async function updateComponent(
  id: string,
  data: CurriculumComponentRequest,
): Promise<CurriculumComponent> {
  const res = await api.put(`/api/academic/components/${id}`, data);
  return res.data;
}

export async function deleteComponent(id: string): Promise<void> {
  await api.delete(`/api/academic/components/${id}`);
}

// ── Avaliações ────────────────────────────────────────────────────────────

export interface AssessmentRequest {
  schoolClassId: string;
  curriculumComponentId: string;
  title: string;
  weight: number;
  /** `DateOnly` na API: enviar `YYYY-MM-DD`. */
  date: string;
  isRecovery: boolean;
}

export interface AssessmentFilters {
  schoolClassId?: string;
  curriculumComponentId?: string;
}

export async function listAssessments(filters: AssessmentFilters = {}): Promise<Assessment[]> {
  const res = await api.get('/api/academic/assessments', { params: filters });
  return res.data;
}

export async function createAssessment(data: AssessmentRequest): Promise<Assessment> {
  const res = await api.post('/api/academic/assessments', data);
  return res.data;
}

export async function updateAssessment(id: string, data: AssessmentRequest): Promise<Assessment> {
  const res = await api.put(`/api/academic/assessments/${id}`, data);
  return res.data;
}

export async function deleteAssessment(id: string): Promise<void> {
  await api.delete(`/api/academic/assessments/${id}`);
}

// ── Lançamento de nota ────────────────────────────────────────────────────

/** Idempotente no servidor: lançar de novo a mesma nota atualiza em vez de duplicar. */
export async function upsertGrade(
  assessmentId: string,
  studentId: string,
  value: number,
): Promise<Grade> {
  const res = await api.put(`/api/academic/assessments/${assessmentId}/grades/${studentId}`, { value });
  return res.data;
}
