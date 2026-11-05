import { matches } from '../utils/search.ts';
import type {
  AcademicYear,
  Assessment,
  CurriculumComponent,
  Enrollment,
  RenewalBatchItemResult,
  SchoolClass,
} from '../types';
import type { PrivacyRequest, PrivacyRequestType } from '../api/privacy';
import type { DemoState } from './seed';

/**
 * Roteador da API falsa.
 *
 * Cada rota reproduz o contrato que os módulos de `src/api` esperam — paginação, filtros e
 * formato de erro incluídos. É um arquivo sem dependência de React/DOM para poder ser
 * exercitado em teste comum (`node --test`).
 */

export interface MockResult {
  status: number;
  data: unknown;
}

type Params = Record<string, unknown>;
type Body = Record<string, unknown>;

const DEFAULT_PAGE_SIZE = 10;

function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : '';
}

function page(params: Params) {
  const pageNumber = Number(params.page);
  const pageSize = Number(params.pageSize);
  return {
    page: Number.isFinite(pageNumber) && pageNumber > 0 ? pageNumber : 1,
    pageSize: Number.isFinite(pageSize) && pageSize > 0 ? pageSize : DEFAULT_PAGE_SIZE,
  };
}

/** Recorte paginado no formato `PagedResponse` do backend. */
export function paginate<T>(items: T[], pageNumber: number, pageSize: number) {
  const start = (pageNumber - 1) * pageSize;
  return {
    items: items.slice(start, start + pageSize),
    total: items.length,
    page: pageNumber,
    pageSize,
  };
}

function notFound(message = 'Recurso não encontrado na demonstração.'): MockResult {
  return { status: 404, data: { title: message } };
}

function badRequest(message: string): MockResult {
  return { status: 400, data: { title: message } };
}

/**
 * Erro no formato do backend: o código vai em `code` (extensão do ProblemDetails), que é o
 * campo traduzido por `apiErrorMessage`.
 *
 * As rotas acadêmicas usam este formato — e não uma frase em português — porque os bloqueios
 * delas são regra de negócio (ano em uso, turma em uso, nota fora da faixa) e precisam chegar
 * ao usuário no idioma ativo da interface.
 */
function apiError(status: number, code: string): MockResult {
  return { status, data: { code } };
}

function unauthorized(): MockResult {
  return { status: 401, data: { title: 'Sessão de demonstração não iniciada.' } };
}

function newId(prefix: string): string {
  return `${prefix}-${Math.random().toString(36).slice(2, 8)}`;
}

function nowIso(): string {
  return new Date().toISOString();
}

function toIsoDay(offsetDays: number): string {
  const date = new Date();
  date.setDate(date.getDate() + offsetDays);
  return date.toISOString();
}

/** Dias entre hoje e a validade — usado pelo recorte "a vencer". */
function daysUntil(dateIso: string): number {
  const day = 86_400_000;
  return Math.round((new Date(dateIso).getTime() - Date.now()) / day);
}

/** Tipos exigidos pela escola para os quais o aluno não tem documento aprovado e vigente. */
function missingRequiredTypes(state: DemoState, studentId: string): string[] {
  const valid = state.documents.filter(
    (document) =>
      document.studentId === studentId &&
      document.status === 'Aprovado' &&
      (document.dataValidade === null || daysUntil(document.dataValidade) >= 0),
  );

  return state.documentTypes
    .filter((type) => type.isRequired && type.isActive)
    .filter((type) => !valid.some((document) => document.documentTypeId === type.id))
    .map((type) => type.nome);
}

/** Documentos aprovados que a rematrícula reaproveita; o vencido vem marcado. */
function renewalCarriedDocuments(state: DemoState, studentId: string) {
  return state.documents
    .filter((document) => document.studentId === studentId && document.status === 'Aprovado')
    .map((document) => ({
      documentId: document.id,
      documentType: document.documentTypeName,
      dataValidade: document.dataValidade,
      isExpired: document.dataValidade !== null && daysUntil(document.dataValidade) < 0,
    }));
}

export function handleRequest(
  state: DemoState,
  method: string,
  rawUrl: string,
  params: Params = {},
  body: Body = {},
): MockResult {
  const url = rawUrl.split('?')[0].replace(/\/$/, '');
  const verb = method.toUpperCase();
  const currentUser = state.users.find((user) => user.id === state.sessionUserId) ?? null;

  // ---------- auth ----------
  if (verb === 'POST' && url === '/api/auth/login') {
    const email = text(body.email).toLowerCase();
    const user = state.users.find((candidate) => candidate.email.toLowerCase() === email && candidate.isActive);
    if (!user) {
      return { status: 401, data: { title: 'Credenciais inválidas na demonstração.', demoEmails: ['diretor@demo.ciclo', 'responsavel@demo.ciclo'] } };
    }
    state.sessionUserId = user.id;
    return { status: 200, data: { accessToken: `demo-token-${user.id}`, user } };
  }

  if (verb === 'POST' && url === '/api/auth/refresh') {
    if (!currentUser) return unauthorized();
    return { status: 200, data: { accessToken: `demo-token-${currentUser.id}` } };
  }

  if (verb === 'GET' && url === '/api/auth/me') {
    if (!currentUser) return unauthorized();
    return { status: 200, data: currentUser };
  }

  if (verb === 'POST' && url === '/api/auth/revoke') {
    state.sessionUserId = null;
    return { status: 204, data: null };
  }

  if (verb === 'POST' && (url === '/api/auth/forgot-password' || url === '/api/auth/reset-password')) {
    return { status: 204, data: null };
  }

  if (verb === 'POST' && url === '/api/auth/register/parent') {
    // O contrato real devolve `{ accessToken, user }` e já abre a sessão. Devolver só o `id`
    // fazia o cadastro parecer quebrado na demonstração: sem token, `/auth/me` respondia 401,
    // e a rota protegida devolvia a pessoa à tela de login sem mensagem nenhuma.
    const email = text(body.email).toLowerCase();
    const user = {
      id: newId('user'),
      email,
      name: text(body.name),
      role: 'Parent' as const,
      tenantId: state.tenant.id,
      emailConfirmed: true,
      isActive: true,
    };
    state.users.push(user);
    state.sessionUserId = user.id;
    return { status: 201, data: { accessToken: `demo-token-${user.id}`, user } };
  }

  // ---------- tenant ----------
  if (verb === 'GET' && url === '/api/tenants/me') {
    if (!currentUser) return unauthorized();
    return { status: 200, data: state.tenant };
  }

  // ---------- usuários ----------
  if (verb === 'GET' && url === '/api/users') {
    if (!currentUser) return unauthorized();
    return { status: 200, data: state.users };
  }

  if (verb === 'POST' && url === '/api/users/invite') {
    const email = text(body.email);
    const name = text(body.name);
    if (!email || !name) return badRequest('Nome e e-mail são obrigatórios.');
    const user = {
      id: newId('user'),
      email,
      name,
      role: (text(body.role) || 'Staff') as 'Admin' | 'Staff' | 'Parent',
      tenantId: state.tenant.id,
      emailConfirmed: false,
      isActive: true,
    };
    state.users.push(user);
    return { status: 201, data: { user } };
  }

  const userRole = url.match(/^\/api\/users\/([^/]+)\/role$/);
  if (verb === 'PATCH' && userRole) {
    const user = state.users.find((candidate) => candidate.id === userRole[1]);
    if (!user) return notFound();
    user.role = (text(body.role) || user.role) as typeof user.role;
    return { status: 200, data: user };
  }

  const userActive = url.match(/^\/api\/users\/([^/]+)\/active$/);
  if (verb === 'PATCH' && userActive) {
    const user = state.users.find((candidate) => candidate.id === userActive[1]);
    if (!user) return notFound();
    user.isActive = Boolean(body.isActive);
    return { status: 200, data: user };
  }

  // ---------- alunos ----------
  if (verb === 'GET' && url === '/api/students') {
    const search = text(params.search);
    const turma = text(params.turma);
    const status = text(params.status);
    const filtered = state.students.filter(
      (student) =>
        (!search || matches(student.nome, search)) &&
        (!turma || matches(student.turma, turma)) &&
        (!status || student.status === status),
    );
    const { page: pageNumber, pageSize } = page(params);
    return { status: 200, data: paginate(filtered, pageNumber, pageSize) };
  }

  if (verb === 'POST' && url === '/api/students') {
    const student = {
      id: newId('student'),
      nome: text(body.nome),
      dataNascimento: text(body.dataNascimento),
      cpf: text(body.cpf) || null,
      turma: text(body.turma),
      anoLetivo: Number(body.anoLetivo) || new Date().getFullYear(),
      status: 'Ativo',
      observacoes: text(body.observacoes) || null,
      foto: text(body.foto) || null,
      createdAt: nowIso(),
      parents: [],
    };
    if (!student.nome || !student.turma) return badRequest('Nome e turma são obrigatórios.');
    state.students.unshift(student);
    return { status: 201, data: student };
  }

  const studentById = url.match(/^\/api\/students\/([^/]+)$/);
  if (studentById) {
    const student = state.students.find((candidate) => candidate.id === studentById[1]);
    if (!student) return notFound();

    if (verb === 'GET') return { status: 200, data: student };

    if (verb === 'PUT') {
      student.nome = text(body.nome) || student.nome;
      student.turma = text(body.turma) || student.turma;
      student.anoLetivo = Number(body.anoLetivo) || student.anoLetivo;
      student.cpf = text(body.cpf) || null;
      student.observacoes = text(body.observacoes) || null;
      if (body.foto !== undefined) student.foto = text(body.foto) || null;
      return { status: 200, data: student };
    }

    if (verb === 'DELETE') {
      state.students = state.students.filter((candidate) => candidate.id !== student.id);
      return { status: 204, data: null };
    }
  }

  const linkParent = url.match(/^\/api\/students\/([^/]+)\/link-parent$/);
  if (verb === 'POST' && linkParent) {
    const student = state.students.find((candidate) => candidate.id === linkParent[1]);
    if (!student) return notFound();
    const parent = state.users.find((candidate) => candidate.id === text(body.parentId));
    if (!parent) return badRequest('Responsável não encontrado na demonstração.');
    student.parents = [...student.parents, { parentId: parent.id, parentName: parent.name, parentEmail: parent.email }];
    return { status: 200, data: student };
  }

  const unlinkParent = url.match(/^\/api\/students\/([^/]+)\/link-parent\/([^/]+)$/);
  if (verb === 'DELETE' && unlinkParent) {
    const student = state.students.find((candidate) => candidate.id === unlinkParent[1]);
    if (!student) return notFound();
    student.parents = student.parents.filter((parent) => parent.parentId !== unlinkParent[2]);
    return { status: 204, data: null };
  }

  const studentDocuments = url.match(/^\/api\/students\/([^/]+)\/documents$/);
  if (verb === 'GET' && studentDocuments) {
    const documents = state.documents.filter((document) => document.studentId === studentDocuments[1]);
    const { page: pageNumber, pageSize } = page(params);
    return { status: 200, data: paginate(documents, pageNumber, pageSize) };
  }

  // ---------- matrículas ----------
  if (verb === 'GET' && url === '/api/enrollments') {
    const periodId = text(params.periodId);
    const status = text(params.status);
    const filtered = state.enrollments.filter(
      (enrollment) =>
        (!periodId || enrollment.periodId === periodId) && (!status || enrollment.status === status),
    );
    const { page: pageNumber, pageSize } = page(params);
    return { status: 200, data: paginate(filtered, pageNumber, pageSize) };
  }

  if (verb === 'GET' && url === '/api/enrollment-periods') {
    return { status: 200, data: state.periods.filter((period) => period.isActive) };
  }

  // Spec 160 — rematrícula em lote. A prévia não grava nada; só o lote cria matrícula.
  if (verb === 'GET' && url === '/api/enrollments/renewal-preview') {
    const period = state.periods.find((candidate) => candidate.id === text(params.periodId));
    if (!period) return notFound('Período não encontrado na demonstração.');

    // Candidato = aluno cujo ano letivo difere do ano do período de destino, como no backend.
    const candidates = state.students
      .filter((student) => student.anoLetivo !== period.anoLetivo)
      .sort((a, b) => a.nome.localeCompare(b.nome));

    const items = candidates.map((student) => {
      const missingDocumentTypes = missingRequiredTypes(state, student.id);
      const enrollmentAlreadyExists = state.enrollments.some(
        (enrollment) => enrollment.studentId === student.id && enrollment.periodId === period.id,
      );

      // Mesma ordem de precedência de `ResolveBlockReason`.
      const blockReason =
        student.status === 'Inativo'
          ? 'student_inactive'
          : student.status === 'Transferido'
            ? 'student_transferred'
            : enrollmentAlreadyExists
              ? 'enrollment_already_exists'
              : missingDocumentTypes.length > 0
                ? 'pending_required_documents'
                : null;

      return {
        studentId: student.id,
        studentName: student.nome,
        eligible: blockReason === null,
        blockReason,
        carriedDocuments: renewalCarriedDocuments(state, student.id),
        missingDocumentTypes,
        enrollmentAlreadyExists,
      };
    });

    return {
      status: 200,
      data: {
        periodId: period.id,
        periodName: period.nome,
        anoLetivo: period.anoLetivo,
        dataReferencia: nowIso(),
        items,
      },
    };
  }

  if (verb === 'POST' && url === '/api/enrollments/renewal-batch') {
    const period = state.periods.find((candidate) => candidate.id === text(body.enrollmentPeriodId));
    if (!period) return notFound('Período não encontrado na demonstração.');

    const requested = Array.isArray(body.studentIds) ? body.studentIds : [];
    const items: RenewalBatchItemResult[] = [];

    for (const rawId of requested) {
      const studentId = String(rawId);
      const student = state.students.find((candidate) => candidate.id === studentId);

      if (!student) {
        items.push({ studentId, studentName: studentId, outcome: 'Failed', message: 'not_found' });
        continue;
      }

      const alreadyExists = state.enrollments.some(
        (enrollment) => enrollment.studentId === student.id && enrollment.periodId === period.id,
      );
      if (alreadyExists) {
        items.push({
          studentId,
          studentName: student.nome,
          outcome: 'SkippedAlreadyExists',
          message: 'enrollment_already_exists',
        });
        continue;
      }

      if (missingRequiredTypes(state, student.id).length > 0) {
        items.push({
          studentId,
          studentName: student.nome,
          outcome: 'BlockedPendingDocuments',
          message: 'pending_required_documents',
        });
        continue;
      }

      const enrollment: Enrollment = {
        id: newId('enrollment'),
        studentId: student.id,
        studentName: student.nome,
        studentFoto: student.foto,
        periodId: period.id,
        periodName: period.nome,
        status: 'Aprovado',
        motivoRejeicao: null,
        createdAt: nowIso(),
        approvedAt: nowIso(),
      };
      state.enrollments.unshift(enrollment);
      // A aprovação promove o aluno (Spec 110) e é o que o tira da prévia na execução seguinte.
      student.anoLetivo = period.anoLetivo;

      items.push({ studentId, studentName: student.nome, outcome: 'Renewed', message: null });
    }

    return {
      status: 200,
      data: {
        enrollmentPeriodId: period.id,
        anoLetivo: period.anoLetivo,
        items,
        renewed: items.filter((item) => item.outcome === 'Renewed').length,
        skipped: items.filter((item) => item.outcome === 'SkippedAlreadyExists').length,
        blocked: items.filter((item) => item.outcome === 'BlockedPendingDocuments').length,
        failed: items.filter((item) => item.outcome === 'Failed').length,
      },
    };
  }
  const enrollmentById = url.match(/^\/api\/enrollments\/([^/]+)$/);
  if (verb === 'GET' && enrollmentById) {
    const enrollment = state.enrollments.find((candidate) => candidate.id === enrollmentById[1]);
    if (!enrollment) return notFound();
    const missing = state.documentTypes
      .filter((type) => type.isRequired)
      .filter(
        (type) =>
          !state.documents.some(
            (document) =>
              document.studentId === enrollment.studentId &&
              document.documentTypeId === type.id &&
              document.status === 'Aprovado',
          ),
      )
      .map((type) => type.nome);
    return {
      status: 200,
      data: { ...enrollment, carriedDocuments: [], missingDocumentTypes: missing },
    };
  }

  const enrollmentAction = url.match(/^\/api\/enrollments\/([^/]+)\/(submit|approve|reject|cancel|reopen)$/);
  if (verb === 'POST' && enrollmentAction) {
    const enrollment = state.enrollments.find((candidate) => candidate.id === enrollmentAction[1]);
    if (!enrollment) return notFound();

    const next: Record<string, string> = {
      submit: 'Pendente',
      approve: 'Aprovado',
      reject: 'Rejeitado',
      cancel: 'Cancelado',
      reopen: 'Pendente',
    };
    enrollment.status = next[enrollmentAction[2]];
    enrollment.motivoRejeicao = enrollmentAction[2] === 'reject' ? text(body.motivo) || 'Motivo não informado.' : null;
    enrollment.approvedAt = enrollmentAction[2] === 'approve' ? nowIso() : null;
    return { status: 204, data: null };
  }


  // ---------- documentos ----------
  if (verb === 'GET' && url === '/api/document-types') {
    return { status: 200, data: state.documentTypes };
  }

  if (verb === 'GET' && url === '/api/documents/pending') {
    const pending = state.documents.filter((document) => document.status === 'Pendente');
    const { page: pageNumber, pageSize } = page(params);
    return { status: 200, data: paginate(pending, pageNumber, pageSize) };
  }

  if (verb === 'GET' && url === '/api/documents/expiring') {
    const days = Number(params.days) || 30;
    const expiring = state.documents.filter(
      (document) =>
        document.status === 'Aprovado' &&
        document.dataValidade !== null &&
        daysUntil(document.dataValidade) <= days &&
        daysUntil(document.dataValidade) >= 0,
    );
    return { status: 200, data: expiring };
  }

  if (verb === 'GET' && url === '/api/documents') {
    const status = text(params.status);
    const studentId = text(params.studentId);
    const documentTypeId = text(params.documentTypeId);
    const search = text(params.search);
    const filtered = state.documents.filter(
      (document) =>
        (!status || document.status === status) &&
        (!studentId || document.studentId === studentId) &&
        (!documentTypeId || document.documentTypeId === documentTypeId) &&
        (!search || matches(document.studentName, search)),
    );
    const { page: pageNumber, pageSize } = page(params);
    return { status: 200, data: paginate(filtered, pageNumber, pageSize) };
  }

  // Spec 170 — o download passou a usar URL assinada. Sem backend, uma URL `data:` resolve o
  // `fetch` do cliente e o fluxo de demonstração continua baixando o arquivo.
  const documentDownloadUrl = url.match(/^\/api\/documents\/([^/]+)\/download-url$/);
  if (verb === 'GET' && documentDownloadUrl) {
    const document = state.documents.find((candidate) => candidate.id === documentDownloadUrl[1]);
    if (!document) return notFound();

    const placeholder = `%PDF-1.7\n${document.nomeArquivo}\n`;
    return {
      status: 200,
      data: {
        url: `data:application/pdf;base64,${btoa(placeholder)}`,
        expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
      },
    };
  }

  if (verb === 'POST' && url === '/api/documents/upload') {
    const studentId = text(body.studentId) || state.students[0]?.id;
    const student = state.students.find((candidate) => candidate.id === studentId);
    const type = state.documentTypes.find((candidate) => candidate.id === text(body.documentTypeId)) ?? state.documentTypes[0];
    const document = {
      id: newId('document'),
      studentId,
      studentName: student?.nome ?? 'Aluno',
      studentFoto: null,
      documentTypeId: type.id,
      documentTypeName: type.nome,
      nomeArquivo: text(body.nomeArquivo) || 'documento.pdf',
      status: 'Pendente',
      dataValidade: null,
      motivoRejeicao: null,
      createdAt: nowIso(),
    };
    state.documents.unshift(document);
    return { status: 201, data: document };
  }

  const documentVerify = url.match(/^\/api\/documents\/([^/]+)\/verify$/);
  if (verb === 'POST' && documentVerify) {
    const document = state.documents.find((candidate) => candidate.id === documentVerify[1]);
    if (!document) return notFound();
    document.status = body.approved ? 'Aprovado' : 'Rejeitado';
    document.motivoRejeicao = body.approved ? null : text(body.motivoRejeicao) || 'Sem motivo informado.';
    return { status: 204, data: null };
  }

  const documentDownload = url.match(/^\/api\/documents\/([^/]+)\/download$/);
  if (verb === 'GET' && documentDownload) {
    // Blob vazio: a tela só precisa de um arquivo para baixar, não de conteúdo real.
    return { status: 200, data: '' };
  }

  // ---------- notificações ----------
  if (verb === 'GET' && url === '/api/notifications') {
    const unreadOnly = params.unreadOnly === true || params.unreadOnly === 'true';
    const filtered = state.notifications.filter((notification) => !unreadOnly || !notification.isRead);
    const { page: pageNumber, pageSize } = page(params);
    return { status: 200, data: paginate(filtered, pageNumber, pageSize) };
  }

  if (verb === 'GET' && url === '/api/notifications/unread-count') {
    return { status: 200, data: { count: state.notifications.filter((notification) => !notification.isRead).length } };
  }

  if (verb === 'PUT' && url === '/api/notifications/read-all') {
    let updatedCount = 0;
    for (const notification of state.notifications) {
      if (!notification.isRead) {
        notification.isRead = true;
        updatedCount += 1;
      }
    }
    return { status: 200, data: { updatedCount } };
  }

  const notificationRead = url.match(/^\/api\/notifications\/([^/]+)\/read$/);
  if (verb === 'PUT' && notificationRead) {
    const notification = state.notifications.find(
      (candidate) => candidate.userNotificationId === notificationRead[1] || candidate.id === notificationRead[1],
    );
    if (!notification) return notFound();
    notification.isRead = true;
    return { status: 204, data: null };
  }

  if (verb === 'POST' && (url === '/api/notifications' || url === '/api/notifications/broadcast')) {
    const notification = {
      id: newId('notification'),
      userNotificationId: newId('user-notification'),
      titulo: text(body.titulo),
      mensagem: text(body.mensagem),
      tipo: 'Geral',
      referenceId: null,
      isRead: false,
      createdAt: nowIso(),
    };
    state.notifications.unshift(notification);
    const recipientCount = url.endsWith('broadcast') ? state.users.length : 1;
    return { status: 201, data: url.endsWith('broadcast') ? { recipientCount } : notification };
  }

  const notificationByStudent = url.match(/^\/api\/notifications\/by-student\/([^/]+)$/);
  if (verb === 'POST' && notificationByStudent) {
    const student = state.students.find((candidate) => candidate.id === notificationByStudent[1]);
    if (!student) return notFound();
    state.notifications.unshift({
      id: newId('notification'),
      userNotificationId: newId('user-notification'),
      titulo: text(body.titulo),
      mensagem: text(body.mensagem),
      tipo: 'Geral',
      referenceId: student.id,
      isRead: false,
      createdAt: nowIso(),
    });
    return { status: 201, data: { recipientCount: student.parents.length } };
  }

  // ---------- estrutura acadêmica ----------
  if (verb === 'GET' && url === '/api/academic/years') {
    return { status: 200, data: [...state.academicYears].sort((a, b) => b.year - a.year) };
  }

  if (verb === 'POST' && url === '/api/academic/years') {
    const year = Number(body.year);
    const startDate = text(body.startDate);
    const endDate = text(body.endDate);

    if (!Number.isInteger(year) || year <= 0) return apiError(400, 'academic_year_invalid');
    if (!startDate || !endDate || endDate < startDate) {
      return apiError(400, 'academic_year_range_invalid');
    }
    if (state.academicYears.some((candidate) => candidate.year === year)) {
      return apiError(409, 'academic_year_already_exists');
    }

    const created: AcademicYear = {
      id: newId('year'),
      year,
      startDate,
      endDate,
      isActive: Boolean(body.isActive),
    };
    state.academicYears.push(created);
    return { status: 201, data: created };
  }

  const academicYearById = url.match(/^\/api\/academic\/years\/([^/]+)$/);
  if (academicYearById) {
    const year = state.academicYears.find((candidate) => candidate.id === academicYearById[1]);
    if (!year) return apiError(404, 'academic_year_not_found');

    if (verb === 'PUT') {
      const nextYear = Number(body.year);
      const startDate = text(body.startDate);
      const endDate = text(body.endDate);

      if (!Number.isInteger(nextYear) || nextYear <= 0) return apiError(400, 'academic_year_invalid');
      if (!startDate || !endDate || endDate < startDate) {
        return apiError(400, 'academic_year_range_invalid');
      }
      if (
        state.academicYears.some(
          (candidate) => candidate.year === nextYear && candidate.id !== year.id,
        )
      ) {
        return apiError(409, 'academic_year_already_exists');
      }

      year.year = nextYear;
      year.startDate = startDate;
      year.endDate = endDate;
      year.isActive = Boolean(body.isActive);

      // A turma guarda o número do ano resolvido pelo backend: sem esta linha o rótulo da turma
      // continuaria mostrando o número antigo depois de corrigir o ano.
      for (const schoolClass of state.classes) {
        if (schoolClass.academicYearId === year.id) schoolClass.year = year.year;
      }

      return { status: 200, data: year };
    }

    if (verb === 'DELETE') {
      // Mesma regra do backend (`academic_year_in_use`): turma ou aluno preso ao ano bloqueia a
      // exclusão — mover os dois é decisão do administrador, não efeito colateral de um DELETE.
      const inUse =
        state.classes.some((schoolClass) => schoolClass.academicYearId === year.id) ||
        state.students.some((student) => student.anoLetivo === year.year);
      if (inUse) return apiError(409, 'academic_year_in_use');

      state.academicYears = state.academicYears.filter((candidate) => candidate.id !== year.id);
      return { status: 204, data: null };
    }
  }

  if (verb === 'GET' && url === '/api/academic/classes') {
    const academicYearId = text(params.academicYearId);
    const filtered = state.classes
      .filter((schoolClass) => !academicYearId || schoolClass.academicYearId === academicYearId)
      .sort((a, b) => a.name.localeCompare(b.name));
    return { status: 200, data: filtered };
  }

  if (verb === 'POST' && url === '/api/academic/classes') {
    const name = text(body.name);
    if (!name) return apiError(400, 'class_name_required');

    const year = state.academicYears.find((candidate) => candidate.id === text(body.academicYearId));
    if (!year) return apiError(404, 'academic_year_not_found');
    if (
      state.classes.some(
        (candidate) => candidate.academicYearId === year.id && candidate.name === name,
      )
    ) {
      return apiError(409, 'class_name_already_exists');
    }

    const created: SchoolClass = {
      id: newId('class'),
      name,
      academicYearId: year.id,
      year: year.year,
    };
    state.classes.push(created);
    return { status: 201, data: created };
  }

  const classStudents = url.match(/^\/api\/academic\/classes\/([^/]+)\/students$/);
  if (verb === 'GET' && classStudents) {
    const schoolClass = state.classes.find((candidate) => candidate.id === classStudents[1]);
    if (!schoolClass) return apiError(404, 'school_class_not_found');

    // O roster sai do campo `turma` do aluno: na demonstração é ele que diz a que turma cada um
    // pertence, e é o que a tela de notas lista.
    const roster = state.students
      .filter(
        (student) => student.turma === schoolClass.name && student.anoLetivo === schoolClass.year,
      )
      .sort((a, b) => a.nome.localeCompare(b.nome));
    return { status: 200, data: roster };
  }

  const academicClassById = url.match(/^\/api\/academic\/classes\/([^/]+)$/);
  if (academicClassById) {
    const schoolClass = state.classes.find((candidate) => candidate.id === academicClassById[1]);
    if (!schoolClass) return apiError(404, 'school_class_not_found');

    if (verb === 'PUT') {
      const name = text(body.name);
      if (!name) return apiError(400, 'class_name_required');

      const year = state.academicYears.find((candidate) => candidate.id === text(body.academicYearId));
      if (!year) return apiError(404, 'academic_year_not_found');
      if (
        state.classes.some(
          (candidate) =>
            candidate.academicYearId === year.id &&
            candidate.name === name &&
            candidate.id !== schoolClass.id,
        )
      ) {
        return apiError(409, 'class_name_already_exists');
      }

      // O aluno guarda o NOME da turma. Renomear move o rótulo junto, senão o roster esvaziaria
      // e a tela de notas perderia os alunos da turma renomeada.
      for (const student of state.students) {
        if (student.turma === schoolClass.name && student.anoLetivo === schoolClass.year) {
          student.turma = name;
        }
      }

      schoolClass.name = name;
      schoolClass.academicYearId = year.id;
      schoolClass.year = year.year;
      return { status: 200, data: schoolClass };
    }

    if (verb === 'DELETE') {
      const inUse =
        state.assessments.some((assessment) => assessment.schoolClassId === schoolClass.id) ||
        state.students.some(
          (student) => student.turma === schoolClass.name && student.anoLetivo === schoolClass.year,
        );
      if (inUse) return apiError(409, 'school_class_in_use');

      state.classes = state.classes.filter((candidate) => candidate.id !== schoolClass.id);
      return { status: 204, data: null };
    }
  }

  if (verb === 'GET' && url === '/api/academic/components') {
    return { status: 200, data: [...state.components].sort((a, b) => a.name.localeCompare(b.name)) };
  }

  if (verb === 'POST' && url === '/api/academic/components') {
    const name = text(body.name);
    const code = text(body.code);
    if (!name) return apiError(400, 'component_name_required');
    if (!code) return apiError(400, 'component_code_required');
    if (state.components.some((candidate) => candidate.code === code)) {
      return apiError(409, 'component_code_already_exists');
    }

    const created: CurriculumComponent = {
      id: newId('component'),
      name,
      code,
      isActive: Boolean(body.isActive),
    };
    state.components.push(created);
    return { status: 201, data: created };
  }

  const componentById = url.match(/^\/api\/academic\/components\/([^/]+)$/);
  if (componentById) {
    const component = state.components.find((candidate) => candidate.id === componentById[1]);
    if (!component) return apiError(404, 'component_not_found');

    if (verb === 'PUT') {
      const name = text(body.name);
      const code = text(body.code);
      if (!name) return apiError(400, 'component_name_required');
      if (!code) return apiError(400, 'component_code_required');
      if (
        state.components.some((candidate) => candidate.code === code && candidate.id !== component.id)
      ) {
        return apiError(409, 'component_code_already_exists');
      }

      component.name = name;
      component.code = code;
      component.isActive = Boolean(body.isActive);
      return { status: 200, data: component };
    }

    if (verb === 'DELETE') {
      if (
        state.assessments.some(
          (assessment) => assessment.curriculumComponentId === component.id,
        )
      ) {
        return apiError(409, 'component_in_use');
      }

      state.components = state.components.filter((candidate) => candidate.id !== component.id);
      return { status: 204, data: null };
    }
  }

  if (verb === 'GET' && url === '/api/academic/assessments') {
    const schoolClassId = text(params.schoolClassId);
    const curriculumComponentId = text(params.curriculumComponentId);
    const filtered = state.assessments
      .filter(
        (assessment) =>
          (!schoolClassId || assessment.schoolClassId === schoolClassId) &&
          (!curriculumComponentId ||
            assessment.curriculumComponentId === curriculumComponentId),
      )
      .sort((a, b) => a.date.localeCompare(b.date));
    return { status: 200, data: filtered };
  }

  if (verb === 'POST' && url === '/api/academic/assessments') {
    const title = text(body.title);
    const weight = Number(body.weight);
    if (!title) return apiError(400, 'assessment_title_required');
    if (!Number.isFinite(weight) || weight <= 0) return apiError(400, 'assessment_weight_invalid');

    const schoolClass = state.classes.find((candidate) => candidate.id === text(body.schoolClassId));
    if (!schoolClass) return apiError(404, 'school_class_not_found');
    const component = state.components.find(
      (candidate) => candidate.id === text(body.curriculumComponentId),
    );
    if (!component) return apiError(404, 'component_not_found');
    if (
      state.assessments.some(
        (candidate) =>
          candidate.schoolClassId === schoolClass.id &&
          candidate.curriculumComponentId === component.id &&
          candidate.title === title,
      )
    ) {
      return apiError(409, 'assessment_title_already_exists');
    }

    const created: Assessment = {
      id: newId('assessment'),
      schoolClassId: schoolClass.id,
      curriculumComponentId: component.id,
      title,
      weight,
      date: text(body.date),
      isRecovery: Boolean(body.isRecovery),
    };
    state.assessments.push(created);
    return { status: 201, data: created };
  }

  const assessmentGrade = url.match(/^\/api\/academic\/assessments\/([^/]+)\/grades\/([^/]+)$/);
  if (verb === 'PUT' && assessmentGrade) {
    const assessment = state.assessments.find((candidate) => candidate.id === assessmentGrade[1]);
    if (!assessment) return apiError(404, 'not_found');

    const student = state.students.find((candidate) => candidate.id === assessmentGrade[2]);
    if (!student) return apiError(404, 'not_found');

    const value = Number(body.value);
    if (!Number.isFinite(value) || value < 0 || value > 10) return apiError(400, 'grade_out_of_range');

    const schoolClass = state.classes.find((candidate) => candidate.id === assessment.schoolClassId);
    if (!schoolClass || student.turma !== schoolClass.name || student.anoLetivo !== schoolClass.year) {
      // Nota de aluno de outra turma corromperia a média: é conflito, e o backend responde 409.
      return apiError(409, 'student_not_in_class');
    }

    // Idempotente no servidor: relançar a mesma nota atualiza o registro, e a chave é o par.
    state.assessmentGrades[`${assessment.id}:${student.id}`] = value;

    const component = state.components.find(
      (candidate) => candidate.id === assessment.curriculumComponentId,
    );
    // Mesmo DTO do backend (`GradeDto`): a disciplina é o nome do componente.
    return {
      status: 200,
      data: { disciplina: component?.name ?? '', nota: value, observacoes: null },
    };
  }

  const assessmentById = url.match(/^\/api\/academic\/assessments\/([^/]+)$/);
  if (assessmentById) {
    const assessment = state.assessments.find((candidate) => candidate.id === assessmentById[1]);
    if (!assessment) return apiError(404, 'not_found');

    if (verb === 'PUT') {
      const title = text(body.title);
      const weight = Number(body.weight);
      if (!title) return apiError(400, 'assessment_title_required');
      if (!Number.isFinite(weight) || weight <= 0) return apiError(400, 'assessment_weight_invalid');

      const schoolClass = state.classes.find((candidate) => candidate.id === text(body.schoolClassId));
      if (!schoolClass) return apiError(404, 'school_class_not_found');
      const component = state.components.find(
        (candidate) => candidate.id === text(body.curriculumComponentId),
      );
      if (!component) return apiError(404, 'component_not_found');

      assessment.title = title;
      assessment.weight = weight;
      assessment.schoolClassId = schoolClass.id;
      assessment.curriculumComponentId = component.id;
      assessment.date = text(body.date);
      assessment.isRecovery = Boolean(body.isRecovery);
      return { status: 200, data: assessment };
    }

    if (verb === 'DELETE') {
      const hasGrades = Object.keys(state.assessmentGrades).some((key) =>
        key.startsWith(`${assessment.id}:`),
      );
      if (hasGrades) return apiError(409, 'assessment_has_grades');

      state.assessments = state.assessments.filter((candidate) => candidate.id !== assessment.id);
      return { status: 204, data: null };
    }
  }

  // ---------- privacidade (LGPD) ----------
  if (verb === 'GET' && url === '/api/privacy/requests') {
    if (!currentUser) return unauthorized();
    return { status: 200, data: state.privacyRequests };
  }

  if (verb === 'GET' && url === '/api/privacy/consents') {
    const subjectId = text(params.subjectId);
    return {
      status: 200,
      data: state.consents.filter((consent) => consent.subjectId === subjectId),
    };
  }

  if (verb === 'POST' && url === '/api/privacy/requests') {
    const created: PrivacyRequest = {
      id: newId('privacy-request'),
      type: (text(body.type) || 'Export') as PrivacyRequestType,
      status: 'Submitted',
      subjectStudentId: text(body.studentId),
      requestedByUserId: currentUser?.id ?? 'user-parent',
      reason: text(body.reason) || null,
      resolution: null,
      legalHold: false,
      retainUntil: null,
      submittedAt: nowIso(),
      decidedAt: null,
      completedAt: null,
    };
    state.privacyRequests.unshift(created);
    return { status: 201, data: created };
  }

  const privacyDecision = url.match(/^\/api\/privacy\/requests\/([^/]+)\/decision$/);
  if (verb === 'POST' && privacyDecision) {
    const request = state.privacyRequests.find(
      (candidate) => candidate.id === privacyDecision[1],
    );
    if (!request) return notFound();

    const approved = text(body.decision) === 'approve';
    request.status = approved ? 'Approved' : 'Rejected';
    request.resolution = text(body.resolution) || null;
    request.legalHold = Boolean(body.legalHold);
    request.retainUntil = text(body.retainUntil) || null;
    request.decidedAt = nowIso();
    return { status: 200, data: request };
  }

  const privacyComplete = url.match(/^\/api\/privacy\/requests\/([^/]+)\/complete$/);
  if (verb === 'POST' && privacyComplete) {
    const request = state.privacyRequests.find(
      (candidate) => candidate.id === privacyComplete[1],
    );
    if (!request) return notFound();

    request.status = 'Completed';
    request.completedAt = nowIso();
    // Contrato de `PrivacyRequestCompletion`: a exportação do titular é outra rota.
    return { status: 200, data: { request, export: null } };
  }

  const studentExport = url.match(/^\/api\/privacy\/students\/([^/]+)\/export$/);
  if (verb === 'GET' && studentExport) {
    const student = state.students.find((candidate) => candidate.id === studentExport[1]);
    if (!student) return notFound();

    // `Blob` de verdade: a tela entrega o corpo direto para `URL.createObjectURL`.
    return {
      status: 200,
      data: new Blob(
        [
          JSON.stringify(
            {
              titular: student.nome,
              exportadoEm: nowIso(),
              documentos: state.documents.filter((document) => document.studentId === student.id),
              consentimentos: state.consents.filter((consent) => consent.subjectId === student.id),
            },
            null,
            2,
          ),
        ],
        { type: 'application/json' },
      ),
    };
  }

  // ---------- responsável ----------
  if (verb === 'GET' && url === '/api/parent/dashboard') {
    const children = state.students.filter((student) => state.parentStudentIds.includes(student.id));
    const childIds = children.map((child) => child.id);
    return {
      status: 200,
      data: {
        totalChildren: children.length,
        unreadNotifications: state.notifications.filter((notification) => !notification.isRead).length,
        pendingDocuments: state.documents.filter(
          (document) => childIds.includes(document.studentId) && document.status === 'Pendente',
        ).length,
        activeEnrollments: state.enrollments.filter(
          (enrollment) => childIds.includes(enrollment.studentId) && enrollment.status === 'Aprovado',
        ).length,
        children: children.map((child) => ({
          studentId: child.id,
          nome: child.nome,
          turma: child.turma,
          anoLetivo: child.anoLetivo,
          enrollmentStatus:
            state.enrollments.find((enrollment) => enrollment.studentId === child.id)?.status ?? null,
          pendingDocuments: state.documents.filter(
            (document) => document.studentId === child.id && document.status === 'Pendente',
          ).length,
          studentFoto: child.foto,
        })),
      },
    };
  }

  if (verb === 'GET' && url === '/api/parent/children') {
    return {
      status: 200,
      data: state.students.filter((student) => state.parentStudentIds.includes(student.id)),
    };
  }

  const childById = url.match(/^\/api\/parent\/children\/([^/]+)$/);
  if (verb === 'GET' && childById) {
    const student = state.students.find((candidate) => candidate.id === childById[1]);
    if (!student) return notFound();
    return {
      status: 200,
      data: {
        student,
        documents: state.documents.filter((document) => document.studentId === student.id),
        currentEnrollment:
          state.enrollments.find((enrollment) => enrollment.studentId === student.id) ?? null,
        grades: state.grades[student.id] ?? [],
      },
    };
  }

  // Boletim do filho: mesma fórmula do backend (`GradeCalculation`) — média regular ponderada
  // sem as recuperações, recuperação nunca reduz a média, limiares 6 (aprovado) e 4 (recuperação).
  const childReportCard = url.match(/^\/api\/parent\/children\/([^/]+)\/report-card$/);
  if (verb === 'GET' && childReportCard) {
    const student = state.students.find((candidate) => candidate.id === childReportCard[1]);
    if (!student) return notFound();

    const schoolClass = state.classes.find(
      (item) => item.name === student.turma && item.year === student.anoLetivo,
    );
    const daTurma = schoolClass
      ? state.assessments.filter((item) => item.schoolClassId === schoolClass.id)
      : [];

    const round2 = (value: number) => Math.round(value * 100) / 100;

    const componentes = state.components
      .filter((component) => daTurma.some((item) => item.curriculumComponentId === component.id))
      .map((component) => {
        const avaliacoes = daTurma
          .filter((item) => item.curriculumComponentId === component.id)
          .map((assessment) => ({
            avaliacao: assessment.title,
            peso: assessment.weight,
            nota: state.assessmentGrades[`${assessment.id}:${student.id}`] ?? null,
            data: assessment.date,
            recuperacao: assessment.isRecovery,
          }));

        let pesoRegular = 0;
        let somaRegular = 0;
        let notaRecuperacao: number | null = null;

        for (const avaliacao of avaliacoes) {
          if (avaliacao.nota === null) continue;
          if (avaliacao.recuperacao) {
            notaRecuperacao = Math.max(notaRecuperacao ?? 0, avaliacao.nota);
          } else {
            pesoRegular += avaliacao.peso;
            somaRegular += avaliacao.nota * avaliacao.peso;
          }
        }

        const mediaRegular = pesoRegular > 0 ? round2(somaRegular / pesoRegular) : null;
        const mediaFinal =
          notaRecuperacao === null
            ? mediaRegular
            : mediaRegular === null
              ? notaRecuperacao
              : round2(Math.max(mediaRegular, notaRecuperacao));

        const situacao =
          mediaFinal === null
            ? 'sem_nota'
            : mediaFinal >= 6
              ? 'aprovado'
              : mediaFinal >= 4
                ? 'recuperacao'
                : 'reprovado';

        return {
          componente: component.name,
          mediaRegular,
          notaRecuperacao,
          mediaFinal,
          situacao,
          avaliacoes,
        };
      });

    return {
      status: 200,
      data: {
        alunoId: student.id,
        turma: student.turma,
        anoLetivo: student.anoLetivo,
        componentes,
      },
    };
  }

  const childDocuments = url.match(/^\/api\/parent\/children\/([^/]+)\/documents$/);
  if (verb === 'GET' && childDocuments) {
    return { status: 200, data: state.documents.filter((document) => document.studentId === childDocuments[1]) };
  }

  const childGrades = url.match(/^\/api\/parent\/children\/([^/]+)\/grades$/);
  if (verb === 'GET' && childGrades) {
    return { status: 200, data: state.grades[childGrades[1]] ?? [] };
  }

  return notFound(`Rota não coberta pela demonstração: ${verb} ${url}`);
}

/** Reexportado para teste: dias restantes até uma data, sem depender do relógio da máquina. */
export { daysUntil, toIsoDay };
