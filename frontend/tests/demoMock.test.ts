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

test('download na demonstração devolve URL de dados que o fetch resolve (Spec 170)', () => {
  const state = fresh();
  const document = state.documents[0];

  const result = handleRequest(state, 'GET', `/api/documents/${document.id}/download-url`, {}, null);

  assert.equal(result.status, 200);
  const data = result.data as { url: string; expiresAt: string };
  assert.match(data.url, /^data:application\/pdf;base64,/);
  assert.ok(Date.parse(data.expiresAt) > Date.now(), 'expiração deveria estar no futuro');

  const missing = handleRequest(state, 'GET', '/api/documents/nao-existe/download-url', {}, null);
  assert.equal(missing.status, 404);
});

test('cadastro de responsável na demonstração devolve sessão, não só o id', () => {
  const state = fresh();
  const result = handleRequest(state, 'POST', '/api/auth/register/parent', {}, {
    schoolSlug: 'colegio-modelo',
    name: 'Marta Almeida',
    email: 'Marta@Teste.local',
    password: 'Senha1!abc',
  });

  assert.equal(result.status, 201);
  const data = result.data as { accessToken: string; user: { id: string; role: string; email: string } };
  // O contrato de `registerParent` em `src/api/auth.ts` é `{ accessToken, user }`. Devolver só
  // o `id` deixava o fluxo sem sessão e a rota protegida devolvia a pessoa ao login em silêncio.
  assert.match(data.accessToken, /^demo-token-/);
  assert.equal(data.user.role, 'Parent');
  assert.equal(data.user.email, 'marta@teste.local', 'o e-mail deve ser normalizado');
  assert.equal(state.sessionUserId, data.user.id);

  // A sessão criada responde em `/auth/me`, que é o passo seguinte do fluxo.
  const me = handleRequest(state, 'GET', '/api/auth/me');
  assert.equal(me.status, 200);
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

// ── Estrutura acadêmica ────────────────────────────────────────────────────

test('as rotas acadêmicas deixaram de cair no 404 genérico', () => {
  const state = fresh();

  for (const route of [
    '/api/academic/years',
    '/api/academic/classes',
    '/api/academic/components',
    '/api/academic/assessments',
  ]) {
    const result = handleRequest(state, 'GET', route);
    assert.equal(result.status, 200, `${route} ainda não é coberta pela demonstração`);
  }
});

test('a turma da semente tem roster e mais de uma avaliação', () => {
  const state = fresh();
  const classes = handleRequest(state, 'GET', '/api/academic/classes').data as {
    id: string;
    name: string;
  }[];
  assert.ok(classes.length > 0, 'a demonstração precisa de turmas');

  const turma = classes.find((item) => item.name === '3º A');
  assert.ok(turma, 'a semente precisa da turma 3º A para a tela de notas');

  const roster = handleRequest(state, 'GET', `/api/academic/classes/${turma.id}/students`).data as {
    nome: string;
  }[];
  assert.ok(roster.length >= 2, 'a turma precisa de alunos no roster');

  const assessments = handleRequest(state, 'GET', '/api/academic/assessments', {
    schoolClassId: turma.id,
  }).data as { id: string; isRecovery: boolean }[];
  assert.ok(assessments.length >= 2, 'a turma precisa de mais de uma avaliação para trocar de prova');
  assert.ok(
    assessments.some((assessment) => assessment.isRecovery),
    'falta a avaliação de recuperação',
  );
});

test('lançar nota é idempotente e recusa valor fora da faixa', () => {
  const state = fresh();

  const turma = state.classes.find((item) => item.name === '3º A');
  assert.ok(turma);
  const assessment = state.assessments.find(
    (item) => item.schoolClassId === turma.id && item.curriculumComponentId === 'component-mat',
  );
  assert.ok(assessment);
  const student = state.students.find((item) => item.turma === '3º A');
  assert.ok(student);

  const route = `/api/academic/assessments/${assessment.id}/grades/${student.id}`;
  const primeiro = handleRequest(state, 'PUT', route, {}, { value: 8.5 });
  assert.equal(primeiro.status, 200);
  // Mesmo DTO do backend: a disciplina é o nome do componente.
  assert.deepEqual(primeiro.data, { disciplina: 'Matemática', nota: 8.5, observacoes: null });

  // Relançar atualiza o mesmo registro: a chave é o par avaliação/aluno (a semente já tem
  // notas, então o que importa é a chave não se multiplicar).
  const chave = `${assessment.id}:${student.id}`;
  const antesDeRelancar = Object.keys(state.assessmentGrades).length;
  handleRequest(state, 'PUT', route, {}, { value: 9 });
  assert.equal(state.assessmentGrades[chave], 9);
  assert.ok(
    Object.keys(state.assessmentGrades).length <= antesDeRelancar + 1,
    'relançar a nota duplicou a chave',
  );

  const fora = handleRequest(state, 'PUT', route, {}, { value: 11 });
  assert.equal(fora.status, 400);
  assert.equal((fora.data as { code: string }).code, 'grade_out_of_range');
});

test('nota de aluno de outra turma é conflito, não gravação', () => {
  const state = fresh();

  const turma = state.classes.find((item) => item.name === '3º A');
  assert.ok(turma);
  const assessment = state.assessments.find((item) => item.schoolClassId === turma.id);
  assert.ok(assessment);
  const deOutraTurma = state.students.find((item) => item.turma === '1º B');
  assert.ok(deOutraTurma);

  const antes = { ...state.assessmentGrades };
  const result = handleRequest(
    state,
    'PUT',
    `/api/academic/assessments/${assessment.id}/grades/${deOutraTurma.id}`,
    {},
    { value: 7 },
  );

  assert.equal(result.status, 409);
  assert.equal((result.data as { code: string }).code, 'student_not_in_class');
  assert.deepEqual(state.assessmentGrades, antes, 'nota de outra turma não pode ser gravada');
});

// ── Privacidade e boletim do responsável ───────────────────────────────────

test('consentimentos são por aluno e a solicitação nasce aguardando decisão', () => {
  const state = fresh();
  const [ana] = state.students;

  const dela = handleRequest(state, 'GET', '/api/privacy/consents', {
    subjectId: ana.id,
    subjectType: 'Student',
  }).data as { subjectId: string }[];
  const deOutro = handleRequest(state, 'GET', '/api/privacy/consents', {
    subjectId: 'nao-existe',
  }).data as unknown[];

  assert.ok(dela.length > 0, 'a semente precisa de consentimentos para o filho');
  assert.ok(dela.every((consent) => consent.subjectId === ana.id));
  assert.equal(deOutro.length, 0);

  const criada = handleRequest(state, 'POST', '/api/privacy/requests', {}, {
    studentId: ana.id,
    type: 'Anonymization',
  });
  assert.equal(criada.status, 201);
  assert.equal((criada.data as { status: string }).status, 'Submitted');
  assert.equal(state.privacyRequests.length, 2);
});

test('exportação do titular responde um Blob que a tela consegue baixar', () => {
  const state = fresh();
  const result = handleRequest(state, 'GET', `/api/privacy/students/${state.students[0].id}/export`);

  assert.equal(result.status, 200);
  // A tela entrega o corpo direto para `URL.createObjectURL`: precisa ser Blob de verdade.
  assert.ok(result.data instanceof Blob, 'a exportação precisa ser um Blob');
});

test('o boletim aplica a fórmula do backend por componente', () => {
  const state = fresh();
  const boletim = (studentId: string) =>
    handleRequest(state, 'GET', `/api/parent/children/${studentId}/report-card`).data as {
      componentes: { componente: string; mediaRegular: number | null; mediaFinal: number | null; situacao: string }[];
    };

  const ana = boletim('student-1');
  const matematica = ana.componentes.find((entry) => entry.componente === 'Matemática');
  assert.ok(matematica);
  // Prova 1 (7,5 · peso 2) é a única regular; a recuperação (7) não reduz a média.
  assert.equal(matematica.mediaRegular, 7.5);
  assert.equal(matematica.mediaFinal, 7.5);
  assert.equal(matematica.situacao, 'aprovado');

  // Camila fica abaixo de 6: a situação precisa sair "em recuperação".
  const camila = boletim('student-4');
  assert.ok(camila.componentes.some((entry) => entry.situacao === 'recuperacao'));
});

test('exclusão bloqueada devolve o código de conflito do backend', () => {
  const state = fresh();

  const emUso = state.academicYears.find((item) => item.year === 2026);
  assert.ok(emUso);
  const ano = handleRequest(state, 'DELETE', `/api/academic/years/${emUso.id}`);
  assert.equal(ano.status, 409);
  assert.equal((ano.data as { code: string }).code, 'academic_year_in_use');

  // 2025 não tem turma nem aluno: é o caminho de sucesso da exclusão.
  const livre = state.academicYears.find((item) => item.year === 2025);
  assert.ok(livre);
  assert.equal(handleRequest(state, 'DELETE', `/api/academic/years/${livre.id}`).status, 204);

  const comAluno = state.classes.find((item) => item.name === '3º A');
  assert.ok(comAluno);
  const turma = handleRequest(state, 'DELETE', `/api/academic/classes/${comAluno.id}`);
  assert.equal(turma.status, 409);
  assert.equal((turma.data as { code: string }).code, 'school_class_in_use');
});

test('criar ano letivo valida faixa e duplicidade com código traduzível', () => {
  const state = fresh();

  const invertido = handleRequest(state, 'POST', '/api/academic/years', {}, {
    year: 2028,
    startDate: '2028-12-01',
    endDate: '2028-02-01',
  });
  assert.equal(invertido.status, 400);
  assert.equal((invertido.data as { code: string }).code, 'academic_year_range_invalid');

  const duplicado = handleRequest(state, 'POST', '/api/academic/years', {}, {
    year: 2026,
    startDate: '2026-02-01',
    endDate: '2026-12-15',
  });
  assert.equal(duplicado.status, 409);
  assert.equal((duplicado.data as { code: string }).code, 'academic_year_already_exists');

  const criado = handleRequest(state, 'POST', '/api/academic/years', {}, {
    year: 2028,
    startDate: '2028-02-01',
    endDate: '2028-12-15',
    isActive: true,
  });
  assert.equal(criado.status, 201);
  assert.equal(state.academicYears.length, 3);
});

// ── Rematrícula em lote ────────────────────────────────────────────────────

interface PreviewItem {
  studentId: string;
  studentName: string;
  eligible: boolean;
  blockReason: string | null;
  missingDocumentTypes: string[];
  enrollmentAlreadyExists: boolean;
}

function previewOf(state: ReturnType<typeof createSeed>, periodId: string) {
  return handleRequest(state, 'GET', '/api/enrollments/renewal-preview', { periodId }).data as {
    periodName: string;
    anoLetivo: number;
    items: PreviewItem[];
  };
}

test('a prévia de rematrícula lista candidatos e respeita os bloqueios', () => {
  const state = fresh();
  const preview = previewOf(state, 'period-2027');

  assert.equal(preview.anoLetivo, 2027);
  assert.ok(preview.items.length > 0, 'o período 2027 precisa de candidatos');
  assert.ok(
    preview.items.some((item) => item.eligible),
    'sem elegível, o caminho de sucesso da rematrícula nunca aparece',
  );
  assert.ok(preview.items.some((item) => item.blockReason === 'student_inactive'));
  assert.ok(preview.items.some((item) => item.blockReason === 'student_transferred'));

  // "Elegível" é exatamente "sem motivo de bloqueio" — nenhuma regra paralela na tela.
  for (const item of preview.items) {
    assert.equal(item.eligible, item.blockReason === null);
  }
});

test('aluno do próprio ano do período não é candidato', () => {
  const state = fresh();
  const aluno = state.students[0];
  aluno.anoLetivo = 2027;

  const preview = previewOf(state, 'period-2027');
  assert.ok(!preview.items.some((item) => item.studentId === aluno.id));
});

test('o lote renova o elegível, bloqueia a pendência e promove o aluno', () => {
  const state = fresh();
  const preview = previewOf(state, 'period-2027');

  const elegivel = preview.items.find((item) => item.eligible);
  assert.ok(elegivel);
  const pendente = preview.items.find((item) => item.blockReason === 'pending_required_documents');
  assert.ok(pendente);

  const lote = handleRequest(state, 'POST', '/api/enrollments/renewal-batch', {}, {
    enrollmentPeriodId: 'period-2027',
    studentIds: [elegivel.studentId, pendente.studentId],
  }).data as {
    renewed: number;
    blocked: number;
    items: { outcome: string }[];
  };

  assert.equal(lote.renewed, 1);
  assert.equal(lote.blocked, 1);
  // Um item por aluno: nada de sucesso/falha global.
  assert.equal(lote.items.length, 2);

  // A aprovação promove o aluno, e promovido ele sai da prévia.
  assert.ok(!previewOf(state, 'period-2027').items.some((item) => item.studentId === elegivel.studentId));

  // Repetir o mesmo aluno não duplica a matrícula: vira "já existia".
  const repetido = handleRequest(state, 'POST', '/api/enrollments/renewal-batch', {}, {
    enrollmentPeriodId: 'period-2027',
    studentIds: [elegivel.studentId],
  }).data as { skipped: number; renewed: number };
  assert.equal(repetido.skipped, 1);
  assert.equal(repetido.renewed, 0);
});
