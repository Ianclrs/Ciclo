import test from 'node:test';
import assert from 'node:assert/strict';
import { describeOutcome, selectableStudentIds } from '../src/domain/renewalBatch.ts';
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

test('cada desfecho tem rótulo próprio, não vazio e distinto', () => {
  const outcomes: RenewalOutcome[] = [
    'Renewed',
    'SkippedAlreadyExists',
    'BlockedPendingDocuments',
    'Failed',
  ];
  const labels = outcomes.map(describeOutcome);

  for (const label of labels) {
    assert.equal(typeof label, 'string');
    assert.ok(label.trim().length > 0, 'rótulo do desfecho não pode ser vazio');
  }

  assert.equal(new Set(labels).size, outcomes.length, `rótulos repetidos: ${labels.join(', ')}`);
  assert.equal(describeOutcome('Renewed'), 'Renovado');
  assert.equal(describeOutcome('SkippedAlreadyExists'), 'Já existia');
  assert.equal(describeOutcome('BlockedPendingDocuments'), 'Documentação pendente');
  assert.equal(describeOutcome('Failed'), 'Falhou');
});
