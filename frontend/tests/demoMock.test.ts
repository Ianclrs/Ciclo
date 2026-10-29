import test from 'node:test';
import assert from 'node:assert/strict';
import { handleRequest } from '../src/mocks/handlers.ts';
import { createSeed } from '../src/mocks/seed.ts';

/**
 * A demo substitui o backend no deploy sem API: se o roteador errar, a tela mostra erro ou
 * lista vazia e ninguém percebe que é defeito do mock. Estes testes travam os contratos que
 * os módulos de `src/api` consomem.
 */

function fresh() {
  return createSeed();
}

test('login da demonstração devolve token e usuário do papel informado', () => {
  const state = fresh();
  const result = handleRequest(state, 'POST', '/api/auth/login', {}, { email: 'diretor@demo.ciclo' });

  assert.equal(result.status, 200);
  const data = result.data as { accessToken: string; user: { role: string; name: string } };
  assert.match(data.accessToken, /^demo-token-/);
  assert.equal(data.user.role, 'Admin');
  assert.equal(state.sessionUserId, 'user-admin');
});

test('login com e-mail desconhecido é 401 (não entra com dados de outro papel)', () => {
  const state = fresh();
  const result = handleRequest(state, 'POST', '/api/auth/login', {}, { email: 'ninguem@demo.ciclo' });

  assert.equal(result.status, 401);
  assert.equal(state.sessionUserId, null);
});

test('lista de alunos pagina e totaliza como o backend', () => {
  const state = fresh();
  const first = handleRequest(state, 'GET', '/api/students', { page: 1, pageSize: 5 });
  const second = handleRequest(state, 'GET', '/api/students', { page: 2, pageSize: 5 });

  const firstPage = first.data as { items: unknown[]; total: number; page: number; pageSize: number };
  const secondPage = second.data as { items: unknown[] };

  assert.equal(firstPage.items.length, 5);
  assert.equal(firstPage.total, state.students.length);
  assert.equal(firstPage.page, 1);
  assert.equal(secondPage.items.length, 5);
  assert.notEqual((firstPage.items[0] as { id: string }).id, (secondPage.items[0] as { id: string }).id);
});

test('filtro de alunos por status e por nome funciona sem acento', () => {
  const state = fresh();
  const inativos = handleRequest(state, 'GET', '/api/students', { status: 'Inativo' });
  const porNome = handleRequest(state, 'GET', '/api/students', { search: 'larissa' });

  assert.ok((inativos.data as { total: number }).total > 0);
  assert.equal((porNome.data as { total: number }).total, 1);
});

test('aprovar matrícula muda o status e grava a data de aprovação', () => {
  const state = fresh();
  const pendente = state.enrollments.find((enrollment) => enrollment.status === 'Pendente');
  assert.ok(pendente);

  const result = handleRequest(state, 'POST', `/api/enrollments/${pendente.id}/approve`);

  assert.equal(result.status, 204);
  assert.equal(pendente.status, 'Aprovado');
  assert.ok(pendente.approvedAt);
});

test('rejeitar matrícula guarda o motivo informado', () => {
  const state = fresh();
  const alvo = state.enrollments[0];

  handleRequest(state, 'POST', `/api/enrollments/${alvo.id}/reject`, {}, { motivo: 'Falta o RG.' });

  assert.equal(alvo.status, 'Rejeitado');
  assert.equal(alvo.motivoRejeicao, 'Falta o RG.');
});

test('contador de não lidas e "marcar todas" ficam consistentes', () => {
  const state = fresh();
  const antes = handleRequest(state, 'GET', '/api/notifications/unread-count').data as { count: number };
  assert.ok(antes.count > 0);

  const marcadas = handleRequest(state, 'PUT', '/api/notifications/read-all').data as { updatedCount: number };
  const depois = handleRequest(state, 'GET', '/api/notifications/unread-count').data as { count: number };

  assert.equal(marcadas.updatedCount, antes.count);
  assert.equal(depois.count, 0);
});

test('filtro de não lidas devolve apenas as pendentes', () => {
  const state = fresh();
  const result = handleRequest(state, 'GET', '/api/notifications', { unreadOnly: true });
  const data = result.data as { items: { isRead: boolean }[]; total: number };

  assert.ok(data.total > 0);
  assert.ok(data.items.every((notification) => !notification.isRead));
});

test('documentos "a vencer" respeita a janela de dias', () => {
  const state = fresh();
  const trinta = handleRequest(state, 'GET', '/api/documents/expiring', { days: 30 }).data as unknown[];
  const novecentos = handleRequest(state, 'GET', '/api/documents/expiring', { days: 900 }).data as unknown[];

  assert.ok(trinta.length > 0);
  assert.ok(novecentos.length >= trinta.length);
});

test('dashboard do responsável conta apenas os filhos vinculados', () => {
  const state = fresh();
  const result = handleRequest(state, 'GET', '/api/parent/dashboard');
  const data = result.data as { totalChildren: number; children: { nome: string }[]; activeEnrollments: number };

  assert.equal(data.totalChildren, 2);
  assert.equal(data.children.length, 2);
  assert.ok(data.children.some((child) => child.nome === 'Ana Beatriz Almeida'));
  assert.ok(data.activeEnrollments <= 2);
});

test('criar aluno entra na lista e aparece na primeira página', () => {
  const state = fresh();
  const antes = state.students.length;

  const created = handleRequest(state, 'POST', '/api/students', {}, { nome: 'Novo Aluno', turma: '4º A' });
  const lista = handleRequest(state, 'GET', '/api/students', { page: 1, pageSize: 5 }).data as {
    items: { nome: string }[];
    total: number;
  };

  assert.equal(created.status, 201);
  assert.equal(state.students.length, antes + 1);
  assert.equal(lista.total, antes + 1);
  assert.equal(lista.items[0].nome, 'Novo Aluno');
});

test('rota não coberta responde 404 em vez de devolver lista vazia silenciosa', () => {
  const state = fresh();
  const result = handleRequest(state, 'GET', '/api/qualquer-coisa');

  assert.equal(result.status, 404);
  assert.match((result.data as { title: string }).title, /não coberta pela demonstração/);
});
