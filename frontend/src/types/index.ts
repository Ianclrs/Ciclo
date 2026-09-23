export interface User {
  id: string;
  name: string;
  email: string;
  role: 'Admin' | 'Staff' | 'Parent';
  tenantId: string;
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
