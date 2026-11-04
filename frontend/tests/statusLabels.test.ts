import test from 'node:test';
import assert from 'node:assert/strict';
import { ENROLLMENT_STATUSES } from '../src/domain/enrollmentStatus.ts';
import { statusLabelKey } from '../src/domain/statusLabels.ts';
import { MESSAGES } from '../src/i18n/messages.ts';
import { LANGUAGE_ORDER } from '../src/i18n/locale.ts';

/**
 * Guarda do vocabulário de status (Spec 190).
 *
 * O defeito que originou este teste: `StatusBadge` conhecia apenas `DocumentacaoPendente`, então
 * `Pendente`, `Aprovado`, `Rascunho`, `Ativo` e `Inativo` apareciam em português nas telas de
 * documentos, matrículas e alunos mesmo com a interface em inglês. Nenhum teste falhava porque o
 * valor cru era exibido sem erro — o problema era de tradução, não de execução.
 */

/** Valores que a API devolve para aluno. */
const STUDENT_STATUSES = ['Ativo', 'Inativo', 'Transferido'];

/** Valores que a API devolve para documento. */
const DOCUMENT_STATUSES = ['Pendente', 'Aprovado', 'Rejeitado'];

const KNOWN_STATUSES = [...ENROLLMENT_STATUSES, ...STUDENT_STATUSES, ...DOCUMENT_STATUSES];

test('todo status conhecido tem chave de catálogo', () => {
  for (const status of KNOWN_STATUSES) {
    assert.notEqual(statusLabelKey(status), null, `${status} ficaria cru na tela`);
  }
});

test('valor fora do vocabulário devolve null em vez de texto inventado', () => {
  assert.equal(statusLabelKey('Desconhecido'), null);
  assert.equal(statusLabelKey(''), null);
});

test('toda chave mapeada existe nos três idiomas e não está vazia', () => {
  for (const status of KNOWN_STATUSES) {
    const key = statusLabelKey(status);
    assert.ok(key !== null, `${status} não tem chave`);

    for (const language of LANGUAGE_ORDER) {
      const value = MESSAGES[language][key];
      assert.equal(typeof value, 'string', `${language}/${key} não existe`);
      assert.notEqual(value.trim(), '', `${language}/${key} está vazio`);
    }
  }
});

test('a etiqueta de documentos usa o mesmo dono do vocabulário', () => {
  // `Pendente` vale tanto para documento quanto para matrícula: uma chave só evita que as duas
  // telas divirjam na mesma palavra.
  assert.equal(statusLabelKey('Pendente'), 'enrollment.status.pending');
  assert.equal(statusLabelKey('DocumentacaoPendente'), 'enrollment.status.documentsRequired');
});
