import test from 'node:test';
import assert from 'node:assert/strict';
import { outcomeLabelKey, selectableStudentIds } from '../src/domain/renewalBatch.ts';
import { MESSAGES } from '../src/i18n/messages.ts';
import { LANGUAGE_ORDER } from '../src/i18n/locale.ts';
import type { RenewalOutcome, RenewalPreviewItem } from '../src/types/index.ts';

const item = (studentId: string, eligible: boolean): RenewalPreviewItem => ({
  studentId,
  studentName: `Aluno ${studentId}`,
  eligible,
  blockReason: eligible ? null : 'pending_required_documents',
  carriedDocuments: [],
  missingDocumentTypes: [],
  enrollmentAlreadyExists: false,
});

test('só alunos elegíveis entram no lote', () => {
  const items = [item('a', true), item('b', false), item('c', true)];
  assert.deepEqual(selectableStudentIds(items), ['a', 'c']);
});

test('lista vazia devolve lista vazia', () => {
  assert.deepEqual(selectableStudentIds([]), []);
});

test('lista só com bloqueados devolve lista vazia', () => {
  assert.deepEqual(selectableStudentIds([item('x', false), item('y', false)]), []);
});

test('cada desfecho tem chave própria, com texto nos três idiomas', () => {
  const outcomes: RenewalOutcome[] = [
    'Renewed',
    'SkippedAlreadyExists',
    'BlockedPendingDocuments',
    'Failed',
  ];
  const keys = outcomes.map(outcomeLabelKey);

  assert.equal(new Set(keys).size, outcomes.length, `chaves repetidas: ${keys.join(', ')}`);

  // A chave só serve se o catálogo tiver texto para ela em pt, en e es.
  for (const language of LANGUAGE_ORDER) {
    for (const key of keys) {
      const label = MESSAGES[language][key];
      assert.equal(typeof label, 'string', `${language}/${key} não é texto`);
      assert.ok(label.trim().length > 0, `${language}/${key} está vazio`);
    }
  }
});
