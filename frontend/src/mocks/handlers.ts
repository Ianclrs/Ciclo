import { matches } from '../utils/search.ts';
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
    return { status: 201, data: { id: newId('user') } };
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
