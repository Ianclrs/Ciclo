import type {
  Document,
  DocumentType,
  Enrollment,
  EnrollmentPeriod,
  Grade,
  ManagedUser,
  Notification,
  Student,
  TenantSummary,
} from '../types';
import { DEMO_ACCOUNTS } from './accounts.ts';

/**
 * Estado completo da demonstração.
 *
 * A demo existe para o deploy sem backend: os dados abaixo substituem a API e ficam
 * gravados no navegador (`localStorage`) para que criar/editar sobreviva ao recarregar.
 */
export interface DemoState {
  tenant: TenantSummary;
  users: ManagedUser[];
  students: Student[];
  enrollments: Enrollment[];
  periods: EnrollmentPeriod[];
  documents: Document[];
  documentTypes: DocumentType[];
  notifications: Notification[];
  /** Filhos vinculados ao responsável da demonstração. */
  parentStudentIds: string[];
  grades: Record<string, Grade[]>;
  /** Usuário "logado" — `null` significa que a visita começa na tela de login. */
  sessionUserId: string | null;
}

const TENANT: TenantSummary = { id: 'tenant-1', name: 'Colégio Modelo', slug: 'colegio-modelo' };

const USERS: ManagedUser[] = [
  {
    id: 'user-admin',
    email: DEMO_ACCOUNTS.admin.email,
    name: 'Diretor Demo',
    role: 'Admin',
    tenantId: TENANT.id,
    emailConfirmed: true,
    isActive: true,
  },
  {
    id: 'user-staff',
    email: 'secretaria@demo.ciclo',
    name: 'Sônia Ribeiro',
    role: 'Staff',
    tenantId: TENANT.id,
    emailConfirmed: true,
    isActive: true,
  },
  {
    id: 'user-parent',
    email: DEMO_ACCOUNTS.parent.email,
    name: 'Marta Almeida',
    role: 'Parent',
    tenantId: TENANT.id,
    emailConfirmed: true,
    isActive: true,
  },
  {
    id: 'user-parent-2',
    email: 'paulo.souza@demo.ciclo',
    name: 'Paulo Souza',
    role: 'Parent',
    tenantId: TENANT.id,
    emailConfirmed: true,
    isActive: false,
  },
];

const PERIODS: EnrollmentPeriod[] = [
  {
    id: 'period-2026',
    nome: 'Matrícula 2026',
    anoLetivo: 2026,
    dataInicio: '2025-10-01',
    dataFim: '2026-01-31',
    isActive: true,
  },
  {
    id: 'period-2025',
    nome: 'Matrícula 2025',
    anoLetivo: 2025,
    dataInicio: '2024-10-01',
    dataFim: '2025-01-31',
    isActive: false,
  },
];

const DOCUMENT_TYPES: DocumentType[] = [
  { id: 'dt-rg', nome: 'RG', descricao: 'Documento de identidade', isRequired: true, validadeMeses: 0, isActive: true },
  { id: 'dt-cpf', nome: 'CPF', descricao: null, isRequired: true, validadeMeses: 0, isActive: true },
  { id: 'dt-vacina', nome: 'Carteira de vacinação', descricao: null, isRequired: true, validadeMeses: 12, isActive: true },
  { id: 'dt-comprovante', nome: 'Comprovante de residência', descricao: null, isRequired: false, validadeMeses: 12, isActive: true },
];

interface SeedStudent {
  nome: string;
  turma: string;
  status: Student['status'];
  cpf: string | null;
  anoLetivo: number;
}

const SEED_STUDENTS: SeedStudent[] = [
  { nome: 'Ana Beatriz Almeida', turma: '3º A', status: 'Ativo', cpf: '123.456.789-00', anoLetivo: 2026 },
  { nome: 'Lucas Almeida', turma: '1º B', status: 'Ativo', cpf: null, anoLetivo: 2026 },
  { nome: 'Bernardo Costa', turma: '2º A', status: 'Ativo', cpf: '234.567.890-11', anoLetivo: 2026 },
  { nome: 'Camila Dias', turma: '3º A', status: 'Ativo', cpf: null, anoLetivo: 2026 },
  { nome: 'Diego Ferreira', turma: '1º A', status: 'Inativo', cpf: '345.678.901-22', anoLetivo: 2026 },
  { nome: 'Eduarda Gomes', turma: '2º B', status: 'Ativo', cpf: null, anoLetivo: 2026 },
  { nome: 'Felipe Martins', turma: '3º B', status: 'Transferido', cpf: '456.789.012-33', anoLetivo: 2026 },
  { nome: 'Gabriela Nunes', turma: '1º B', status: 'Ativo', cpf: null, anoLetivo: 2026 },
  { nome: 'Heitor Oliveira', turma: '2º A', status: 'Ativo', cpf: '567.890.123-44', anoLetivo: 2026 },
  { nome: 'Isabela Prado', turma: '3º A', status: 'Ativo', cpf: null, anoLetivo: 2026 },
  { nome: 'João Vitor Ramos', turma: '1º A', status: 'Ativo', cpf: '678.901.234-55', anoLetivo: 2026 },
  { nome: 'Larissa Souza', turma: '2º B', status: 'Inativo', cpf: null, anoLetivo: 2026 },
];

const ENROLLMENT_STATUSES = ['Pendente', 'DocumentacaoPendente', 'Aprovado', 'Rejeitado', 'Cancelado'] as const;

const NOTIFICATION_SEED: { titulo: string; mensagem: string; tipo: string; isRead: boolean }[] = [
  {
    titulo: 'Documento pendente',
    mensagem: 'O RG de Lucas Almeida está aguardando verificação.',
    tipo: 'Documento Pendente',
    isRead: false,
  },
  {
    titulo: 'Nova matrícula recebida',
    mensagem: 'A matrícula de Bernardo Costa foi enviada e aguarda análise.',
    tipo: 'Matrícula',
    isRead: false,
  },
  {
    titulo: 'Reunião de pais',
    mensagem: 'Reunião do 2º bimestre no dia 12, às 19h, no auditório.',
    tipo: 'Reunião',
    isRead: true,
  },
  {
    titulo: 'Carteira de vacinação vencida',
    mensagem: 'A carteira de vacinação de Larissa Souza vence em 20 dias.',
    tipo: 'Outro',
    isRead: false,
  },
  {
    titulo: 'Bem-vindo ao Ciclo',
    mensagem: 'Este ambiente usa dados de demonstração gravados no seu navegador.',
    tipo: 'Geral',
    isRead: true,
  },
];

function isoDaysFromNow(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return date.toISOString();
}

function isoDateDaysFromNow(days: number): string {
  return isoDaysFromNow(days).slice(0, 10);
}

/** Monta o estado inicial. Datas são relativas a hoje para a demo nunca "envelhecer". */
export function createSeed(): DemoState {
  const students: Student[] = SEED_STUDENTS.map((seed, index) => ({
    id: `student-${index + 1}`,
    nome: seed.nome,
    dataNascimento: `201${(index % 8) + 2}-0${(index % 9) + 1}-1${index % 9}`,
    cpf: seed.cpf,
    turma: seed.turma,
    anoLetivo: seed.anoLetivo,
    status: seed.status,
    observacoes: null,
    foto: null,
    createdAt: isoDaysFromNow(-120 + index),
    parents: [],
  }));

  // Vínculos: o responsável da demo tem dois filhos; os demais alunos têm um responsável.
  students[0].parents = [{ parentId: 'user-parent', parentName: 'Marta Almeida', parentEmail: 'responsavel@demo.ciclo' }];
  students[1].parents = [{ parentId: 'user-parent', parentName: 'Marta Almeida', parentEmail: 'responsavel@demo.ciclo' }];
  for (const student of students.slice(2)) {
    student.parents = [
      { parentId: 'user-parent-2', parentName: 'Paulo Souza', parentEmail: 'paulo.souza@demo.ciclo' },
    ];
  }

  const enrollments: Enrollment[] = students.map((student, index) => {
    const status = ENROLLMENT_STATUSES[index % ENROLLMENT_STATUSES.length];
    return {
      id: `enrollment-${index + 1}`,
      studentId: student.id,
      studentName: student.nome,
      studentFoto: null,
      periodId: PERIODS[0].id,
      periodName: PERIODS[0].nome,
      status,
      motivoRejeicao: status === 'Rejeitado' ? 'Documentação incompleta.' : null,
      createdAt: isoDaysFromNow(-30 + index),
      approvedAt: status === 'Aprovado' ? isoDaysFromNow(-20 + index) : null,
    };
  });

  const documents: Document[] = students.flatMap((student, index) => {
    const statusFor = (offset: number) =>
      (index + offset) % 4 === 0 ? 'Pendente' : (index + offset) % 5 === 0 ? 'Rejeitado' : 'Aprovado';

    const identidade: Document = {
      id: `document-${index + 1}-1`,
      studentId: student.id,
      studentName: student.nome,
      studentFoto: null,
      documentTypeId: DOCUMENT_TYPES[0].id,
      documentTypeName: DOCUMENT_TYPES[0].nome,
      nomeArquivo: `rg-${index + 1}.pdf`,
      status: statusFor(0),
      // RG não vence.
      dataValidade: null,
      motivoRejeicao: statusFor(0) === 'Rejeitado' ? 'Arquivo ilegível.' : null,
      createdAt: isoDaysFromNow(-60 + index),
    };

    const vacina: Document = {
      id: `document-${index + 1}-2`,
      studentId: student.id,
      studentName: student.nome,
      studentFoto: null,
      documentTypeId: DOCUMENT_TYPES[2].id,
      documentTypeName: DOCUMENT_TYPES[2].nome,
      nomeArquivo: `carteira-vacinacao-${index + 1}.pdf`,
      status: statusFor(1),
      // Escalonado a partir de 8 dias: alimenta o card "vencem em 30 dias" sem zerar a lista.
      dataValidade: isoDateDaysFromNow(8 + index * 20),
      motivoRejeicao: statusFor(1) === 'Rejeitado' ? 'Página da vacina ilegível.' : null,
      createdAt: isoDaysFromNow(-55 + index),
    };

    return [identidade, vacina];
  });

  const notifications: Notification[] = NOTIFICATION_SEED.map((seed, index) => ({
    id: `notification-${index + 1}`,
    userNotificationId: `user-notification-${index + 1}`,
    titulo: seed.titulo,
    mensagem: seed.mensagem,
    tipo: seed.tipo,
    referenceId: null,
    isRead: seed.isRead,
    createdAt: isoDaysFromNow(-index),
  }));

  const grades: Record<string, Grade[]> = {
    'student-1': [
      { disciplina: 'Português', nota: 8.5, observacoes: null },
      { disciplina: 'Matemática', nota: 9.0, observacoes: null },
      { disciplina: 'Ciências', nota: 7.5, observacoes: null },
    ],
    'student-2': [
      { disciplina: 'Português', nota: 7.0, observacoes: null },
      { disciplina: 'Matemática', nota: 6.5, observacoes: null },
    ],
  };

  return {
    tenant: TENANT,
    users: USERS,
    students,
    enrollments,
    periods: PERIODS,
    documents,
    documentTypes: DOCUMENT_TYPES,
    notifications,
    parentStudentIds: ['student-1', 'student-2'],
    grades,
    sessionUserId: null,
  };
}
