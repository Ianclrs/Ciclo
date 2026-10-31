export interface User {
  id: string;
  name: string;
  email: string;
  role: 'Admin' | 'Staff' | 'Parent';
  tenantId: string;
  /** M11: presente em /auth/me e na gestão de usuários. */
  emailConfirmed?: boolean;
}

/** Colégio do usuário logado (`GET /api/tenants/me`). */
export interface TenantSummary {
  id: string;
  name: string;
  slug: string;
}

export type ManagedRole = 'Admin' | 'Staff' | 'Parent';

/** Usuário como a tela de gestão recebe (M11). */
export interface ManagedUser {
  id: string;
  email: string;
  name: string;
  role: ManagedRole;
  tenantId: string;
  emailConfirmed: boolean;
  isActive: boolean;
}

export interface Student {
  id: string;
  nome: string;
  dataNascimento: string;
  cpf: string | null;
  turma: string;
  anoLetivo: number;
  status: string;
  observacoes: string | null;
  /** Foto como data URL (Base64); null quando o aluno não tem foto. */
  foto: string | null;
  createdAt: string;
  parents: ParentLink[];
}

export interface ParentLink {
  parentId: string;
  parentName: string;
  parentEmail: string;
}

export interface Document {
  id: string;
  studentId: string;
  studentName: string;
  studentFoto: string | null;
  documentTypeId: string;
  documentTypeName: string;
  nomeArquivo: string;
  status: string;
  dataValidade: string | null;
  motivoRejeicao: string | null;
  createdAt: string;
}

export interface DocumentType {
  id: string;
  nome: string;
  descricao: string | null;
  isRequired: boolean;
  validadeMeses: number;
  isActive: boolean;
}

export interface Enrollment {
  id: string;
  studentId: string;
  studentName: string;
  studentFoto: string | null;
  periodId: string;
  periodName: string;
  status: string;
  motivoRejeicao: string | null;
  createdAt: string;
  approvedAt: string | null;
  // Situação documental: preenchida apenas no DETALHE da matrícula (carry-forward, Spec 60 FR-004).
  carriedDocuments?: CarriedDocument[] | null;
  missingDocumentTypes?: string[] | null;
}

export interface CarriedDocument {
  documentId: string;
  documentType: string;
  dataValidade: string | null;
  /** Aprovado mas vencido: não conta como entregue e precisa ser reenviado. */
  isExpired: boolean;
}

export interface EnrollmentPeriod {
  id: string;
  nome: string;
  anoLetivo: number;
  dataInicio: string;
  dataFim: string;
  isActive: boolean;
}

export interface Notification {
  id: string;
  userNotificationId: string;
  titulo: string;
  mensagem: string;
  tipo: string;
  referenceId: string | null;
  isRead: boolean;
  createdAt: string;
}

export interface PagedResponse<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Grade {
  disciplina: string;
  nota: number | null;
  observacoes: string | null;
}

export interface ParentDashboard {
  totalChildren: number;
  unreadNotifications: number;
  pendingDocuments: number;
  activeEnrollments: number;
  children: ChildSummary[];
}

export interface ChildSummary {
  studentId: string;
  nome: string;
  turma: string;
  anoLetivo: number;
  enrollmentStatus: string | null;
  pendingDocuments: number;
  studentFoto: string | null;
}

export interface ChildDetail {
  student: Student;
  documents: Document[];
  currentEnrollment: Enrollment | null;
  grades: Grade[];
}

/* ── Spec 150: estrutura acadêmica e boletim ────────────────────────────────
   Campos exatamente como a API os serializa (camelCase). `startDate`, `endDate`
   e `date` são `DateOnly` no backend e chegam como `YYYY-MM-DD`, sem hora e sem
   fuso — por isso são formatados por `formatDateOnly`, nunca por `formatDate`. */

export interface AcademicYear {
  id: string;
  year: number;
  startDate: string;
  endDate: string;
  isActive: boolean;
}

export interface SchoolClass {
  id: string;
  name: string;
  academicYearId: string;
  /** Ano letivo resolvido pelo backend, para a tela não precisar cruzar listas. */
  year: number;
}

export interface CurriculumComponent {
  id: string;
  name: string;
  code: string;
  isActive: boolean;
}

export interface Assessment {
  id: string;
  schoolClassId: string;
  curriculumComponentId: string;
  title: string;
  weight: number;
  date: string;
  isRecovery: boolean;
}

/** Situações possíveis de um componente no boletim (vocabulário do backend). */
export type ReportCardStatus = 'aprovado' | 'recuperacao' | 'reprovado' | 'sem_nota';

export interface ReportCardScore {
  avaliacao: string;
  peso: number;
  nota: number | null;
  data: string;
  recuperacao: boolean;
}

export interface ReportCardEntry {
  componente: string;
  mediaRegular: number | null;
  notaRecuperacao: number | null;
  mediaFinal: number | null;
  situacao: ReportCardStatus;
  avaliacoes: ReportCardScore[];
}

export interface ReportCard {
  alunoId: string;
  turma: string | null;
  anoLetivo: number | null;
  componentes: ReportCardEntry[];
}

/* ── Spec 160: rematrícula em lote ─────────────────────────────────────────
   `outcome` chega como texto porque `RenewalOutcome` declara
   `JsonStringEnumConverter` no backend — sem isso o campo viria como `0..3` e o
   rótulo do desfecho ficaria indefinido. */

export type RenewalOutcome =
  | 'Renewed'
  | 'SkippedAlreadyExists'
  | 'BlockedPendingDocuments'
  | 'Failed';

export interface RenewalPreviewItem {
  studentId: string;
  studentName: string;
  eligible: boolean;
  /** Código do motivo do bloqueio; `null` quando `eligible` é verdadeiro. */
  blockReason: string | null;
  carriedDocuments: CarriedDocument[];
  missingDocumentTypes: string[];
  enrollmentAlreadyExists: boolean;
}

export interface RenewalPreview {
  periodId: string;
  periodName: string;
  anoLetivo: number;
  /** Instante usado pelo backend para decidir `isExpired`; a tela não recalcula validade. */
  dataReferencia: string;
  items: RenewalPreviewItem[];
}

export interface RenewalBatchRequest {
  enrollmentPeriodId: string;
  studentIds: string[];
}

export interface RenewalBatchItemResult {
  studentId: string;
  studentName: string;
  outcome: RenewalOutcome;
  message: string | null;
}

export interface RenewalBatchResult {
  enrollmentPeriodId: string;
  anoLetivo: number;
  items: RenewalBatchItemResult[];
  renewed: number;
  skipped: number;
  blocked: number;
  failed: number;
}
