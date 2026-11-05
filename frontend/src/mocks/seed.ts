import type {
  AcademicYear,
  Assessment,
  CurriculumComponent,
  Document,
  DocumentType,
  Enrollment,
  EnrollmentPeriod,
  Grade,
  ManagedUser,
  Notification,
  SchoolClass,
  Student,
  TenantSummary,
} from '../types';
import type { Consent, PrivacyRequest } from '../api/privacy';
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
  /** Estrutura acadêmica: o que a tela de lançamento de notas consome. */
  academicYears: AcademicYear[];
  classes: SchoolClass[];
  components: CurriculumComponent[];
  assessments: Assessment[];
  /** Notas lançadas, por `${assessmentId}:${studentId}` — a chave do índice único do backend. */
  assessmentGrades: Record<string, number>;
  /** Consentimentos LGPD registrados por aluno (Spec 210). */
  consents: Consent[];
  privacyRequests: PrivacyRequest[];
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
  // Período do ANO SEGUINTE: é o que dá candidatos à rematrícula em lote (a prévia considera
  // apenas alunos cujo ano letivo difere do ano do período de destino).
  {
    id: 'period-2027',
    nome: 'Rematrícula 2027',
    anoLetivo: 2027,
    dataInicio: '2026-10-01',
    dataFim: '2027-01-31',
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

/**
 * Anos letivos da demonstração. O segundo fica inativo de propósito: é o que dá sentido às
 * etiquetas "Ativo/Inativo" e à proporção de anos ativos nos indicadores da estrutura.
 */
const ACADEMIC_YEARS: AcademicYear[] = [
  { id: 'year-2026', year: 2026, startDate: '2026-02-01', endDate: '2026-12-15', isActive: true },
  { id: 'year-2025', year: 2025, startDate: '2025-02-03', endDate: '2025-12-16', isActive: false },
];

const ACTIVE_YEAR = ACADEMIC_YEARS[0];

/**
 * Turmas da demonstração. Os nomes espelham o campo `turma` dos alunos da semente — é dele que
 * o roster de cada turma sai, então toda turma tem aluno de verdade para a tela de notas lançar.
 */
const CLASSES: SchoolClass[] = ['1º A', '1º B', '2º A', '2º B', '3º A', '3º B'].map((name, index) => ({
  id: `class-${index + 1}`,
  name,
  academicYearId: ACTIVE_YEAR.id,
  year: ACTIVE_YEAR.year,
}));

/** Um componente inativo entre os ativos: mostra a proporção de ativos no indicador. */
const COMPONENTS: CurriculumComponent[] = [
  { id: 'component-por', name: 'Português', code: 'POR', isActive: true },
  { id: 'component-mat', name: 'Matemática', code: 'MAT', isActive: true },
  { id: 'component-cie', name: 'Ciências', code: 'CIE', isActive: true },
  { id: 'component-his', name: 'História', code: 'HIS', isActive: false },
];

interface SeedAssessment {
  class: string;
  component: string;
  title: string;
  weight: number;
  date: string;
  isRecovery: boolean;
}

/**
 * Avaliações: a turma "3º A" leva três (uma de recuperação) para que trocar de avaliação na
 * tela de notas seja exercitável, e "1º B" leva uma. As demais ficam sem avaliação de
 * propósito — é o estado vazio da tela.
 */
const SEED_ASSESSMENTS: SeedAssessment[] = [
  // A turma que a tela de notas abre por padrão é a primeira em ordem alfabética: sem uma
  // avaliação aqui, a demonstração abriria sempre no estado vazio.
  { class: '1º A', component: 'component-por', title: 'Prova 1', weight: 2, date: '2026-04-08', isRecovery: false },
  { class: '3º A', component: 'component-mat', title: 'Prova 1', weight: 2, date: '2026-04-10', isRecovery: false },
  { class: '3º A', component: 'component-por', title: 'Trabalho de leitura', weight: 1, date: '2026-05-15', isRecovery: false },
  { class: '3º A', component: 'component-mat', title: 'Recuperação do 1º bimestre', weight: 1, date: '2026-07-01', isRecovery: true },
  { class: '1º B', component: 'component-cie', title: 'Prova de ciências', weight: 1, date: '2026-04-20', isRecovery: false },
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

    // CPF aprovado para todos: sem ele, todo aluno ficaria com pendência e a prévia de
    // rematrícula nunca teria um elegível — o caminho de sucesso não apareceria na demonstração.
    const cpf: Document = {
      id: `document-${index + 1}-3`,
      studentId: student.id,
      studentName: student.nome,
      studentFoto: null,
      documentTypeId: DOCUMENT_TYPES[1].id,
      documentTypeName: DOCUMENT_TYPES[1].nome,
      nomeArquivo: `cpf-${index + 1}.pdf`,
      status: 'Aprovado',
      dataValidade: null,
      motivoRejeicao: null,
      createdAt: isoDaysFromNow(-58 + index),
    };

    return [identidade, vacina, cpf];
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

  // Estrutura acadêmica: as avaliações apontam para a turma pelo nome, como o resto da semente.
  const assessments: Assessment[] = SEED_ASSESSMENTS.map((seed, index) => {
    const schoolClass = CLASSES.find((item) => item.name === seed.class);
    if (!schoolClass) throw new Error(`turma da semente ausente: ${seed.class}`);

    return {
      id: `assessment-${index + 1}`,
      schoolClassId: schoolClass.id,
      curriculumComponentId: seed.component,
      title: seed.title,
      weight: seed.weight,
      date: seed.date,
      isRecovery: seed.isRecovery,
    };
  });

  // Notas da turma "3º A": sem elas toda componente do boletim apareceria como "sem nota" e a
  // aba de notas do responsável nunca mostraria situação nenhuma.
  const terceiroA = assessments.filter(
    (assessment) => assessment.schoolClassId === CLASSES.find((item) => item.name === '3º A')?.id,
  );
  const NOTAS_POR_TITULO: Record<string, number> = {
    'Prova 1': 7.5,
    'Trabalho de leitura': 9,
    'Recuperação do 1º bimestre': 7,
  };
  const assessmentGrades: Record<string, number> = {};
  for (const assessment of terceiroA) {
    const nota = NOTAS_POR_TITULO[assessment.title];
    if (nota !== undefined) {
      assessmentGrades[`${assessment.id}:student-1`] = nota;
      // Camila fica abaixo da média sem recuperação: exercita a situação "em recuperação".
      assessmentGrades[`${assessment.id}:student-4`] = nota - 2;
    }
  }

  const consents: Consent[] = students.slice(0, 2).flatMap((student, index) => [
    {
      id: `consent-${index + 1}-imagem`,
      subjectType: 'Student' as const,
      subjectId: student.id,
      purpose: 'Uso de imagem em atividades pedagógicas',
      legalBasis: 'Consentimento',
      status: 'Granted' as const,
      grantedAt: isoDaysFromNow(-120 + index * 10),
      revokedAt: null,
    },
    {
      id: `consent-${index + 1}-saude`,
      subjectType: 'Student' as const,
      subjectId: student.id,
      purpose: 'Compartilhamento com plano de saúde',
      legalBasis: 'Consentimento',
      status: 'Revoked' as const,
      grantedAt: isoDaysFromNow(-300),
      revokedAt: isoDaysFromNow(-40),
    },
  ]);

  // Uma solicitação aberta: é o que desabilita o pedido do mesmo tipo e mostra o aviso na tela.
  const privacyRequests: PrivacyRequest[] = [
    {
      id: 'privacy-request-1',
      type: 'Export',
      status: 'Submitted',
      subjectStudentId: 'student-2',
      requestedByUserId: 'user-parent',
      reason: null,
      resolution: null,
      legalHold: false,
      retainUntil: null,
      submittedAt: isoDaysFromNow(-3),
      decidedAt: null,
      completedAt: null,
    },
  ];

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
    academicYears: ACADEMIC_YEARS,
    classes: CLASSES,
    components: COMPONENTS,
    assessments,
    assessmentGrades,
    consents,
    privacyRequests,
    sessionUserId: null,
  };
}
