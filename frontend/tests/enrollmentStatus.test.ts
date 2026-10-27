import test from 'node:test';
import assert from 'node:assert/strict';
import { actionLabel, actionsFor } from '../src/domain/enrollmentStatus.ts';

test('reabertura aparece apenas para Admin', () => {
  assert.deepEqual(actionsFor('Aprovado', 'Admin'), ['reopen']);
  assert.deepEqual(actionsFor('Rejeitado', 'Admin'), ['reopen']);
  assert.deepEqual(actionsFor('Cancelado', 'Admin'), ['reopen']);

  // Staff e responsável não veem o botão: o backend responde 403 para eles.
  assert.deepEqual(actionsFor('Aprovado', 'Staff'), []);
  assert.deepEqual(actionsFor('Aprovado', 'Parent'), []);
  assert.deepEqual(actionsFor('Aprovado'), []);
});

test('as ações do fluxo normal não mudaram com a reabertura', () => {
  assert.deepEqual(actionsFor('Rascunho', 'Admin'), ['submit']);
  assert.deepEqual(actionsFor('Pendente', 'Staff'), ['approve', 'reject', 'cancel']);
  assert.deepEqual(actionsFor('DocumentacaoPendente', 'Staff'), ['approve', 'reject', 'cancel']);
});

test('rótulo da reabertura', () => {
  assert.equal(actionLabel('reopen'), 'Reabrir');
});
