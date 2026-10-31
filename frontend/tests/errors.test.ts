import test from 'node:test';
import assert from 'node:assert/strict';
import { apiErrorCode, apiErrorMessage, codeMessage } from '../src/api/errors.ts';

/** Erro no formato que o axios entrega (ProblemDetails em `data`). */
function apiError(data: unknown) {
  return { isAxiosError: true, response: { status: 400, data } };
}

test('código conhecido vira mensagem específica', () => {
  assert.equal(
    apiErrorMessage(apiError({ title: 'student_not_found' }), 'fallback'),
    'Aluno não encontrado.',
  );
  assert.equal(
    apiErrorMessage(apiError({ title: 'pending_required_documents' }), 'fallback'),
    'Há documentos obrigatórios pendentes. Regularize antes de aprovar.',
  );
});

test('o `code` do ProblemDetails tem precedência sobre `title`', () => {
  // M3: o contrato passou a expor `code`; `title` fica para humanos e pode mudar de redação.
  const err = apiError({ code: 'student_not_found', title: 'Aluno não encontrado' });
  assert.equal(apiErrorCode(err), 'student_not_found');
  assert.equal(apiErrorMessage(err, 'fallback'), 'Aluno não encontrado.');
});

test('código em `error` também é reconhecido', () => {
  assert.equal(apiErrorCode(apiError({ error: 'invalid_extension' })), 'invalid_extension');
  assert.equal(
    apiErrorMessage(apiError({ error: 'invalid_extension' }), 'fallback'),
    'Formato de arquivo não permitido. Use PDF, JPG ou PNG.',
  );
});

test('título legível da API é preservado', () => {
  const title = 'A data de nascimento deve estar no passado.';
  assert.equal(apiErrorMessage(apiError({ title }), 'fallback'), title);
});

test('identificador desconhecido não vaza para o usuário', () => {
  const message = apiErrorMessage(apiError({ title: 'some_new_code' }), 'Não foi possível carregar.');
  assert.equal(message, 'Não foi possível carregar.');
  assert.ok(!message.includes('some_new_code'));
});

test('erro que não vem da API usa o texto da tela', () => {
  assert.equal(apiErrorMessage(new Error('boom'), 'Falha ao carregar.'), 'Falha ao carregar.');
  assert.equal(apiErrorMessage(null, 'Falha ao carregar.'), 'Falha ao carregar.');
});

test('resposta sem corpo usa o texto da tela', () => {
  assert.equal(apiErrorMessage({ isAxiosError: true, response: { status: 500 } }, 'Falha ao carregar.'), 'Falha ao carregar.');
});

test('a mensagem acompanha o idioma pedido (Spec 190)', () => {
  assert.equal(codeMessage('student_not_found', 'fallback', 'pt'), 'Aluno não encontrado.');
  assert.equal(codeMessage('student_not_found', 'fallback', 'en'), 'Student not found.');
  assert.equal(codeMessage('student_not_found', 'fallback', 'es'), 'Alumno no encontrado.');

  assert.equal(
    apiErrorMessage(apiError({ code: 'enrollment_period_closed' }), 'fallback', 'en'),
    'The enrollment period is closed.',
  );
  assert.equal(
    apiErrorMessage(apiError({ code: 'grade_out_of_range' }), 'fallback', 'es'),
    'La nota debe estar entre 0 y 10.',
  );
});

test('sem idioma, a saída continua sendo a de pt', () => {
  assert.equal(codeMessage('student_not_found', 'fallback'), 'Aluno não encontrado.');
  assert.equal(
    apiErrorMessage(apiError({ code: 'student_not_found' }), 'fallback'),
    codeMessage('student_not_found', 'fallback', 'pt'),
  );
});

test('as regras de código desconhecido valem em qualquer idioma', () => {
  const readable = 'A data de nascimento deve estar no passado.';
  for (const language of ['pt', 'en', 'es'] as const) {
    assert.equal(codeMessage(readable, 'fallback', language), readable);
    assert.equal(codeMessage('some_new_code', 'fallback', language), 'fallback');
  }
});
