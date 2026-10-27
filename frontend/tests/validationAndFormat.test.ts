import test from 'node:test';
import assert from 'node:assert/strict';
import { MOTIVO_MAX_LENGTH, MOTIVO_MIN_LENGTH, motivoValidationError } from '../src/domain/validation.ts';
import { formatDate, formatDateTime } from '../src/utils/format.ts';

test('motivo aceito apenas na faixa que o backend aceita', () => {
  assert.equal(motivoValidationError('   '), 'Informe o motivo da rejeição.');
  assert.equal(
    motivoValidationError('curto'),
    `O motivo deve ter ao menos ${MOTIVO_MIN_LENGTH} caracteres.`,
  );
  assert.equal(motivoValidationError('a'.repeat(9)), `O motivo deve ter ao menos ${MOTIVO_MIN_LENGTH} caracteres.`);
  assert.equal(motivoValidationError('a'.repeat(MOTIVO_MIN_LENGTH)), null);
  assert.equal(motivoValidationError('a'.repeat(MOTIVO_MAX_LENGTH)), null);
  assert.equal(
    motivoValidationError('a'.repeat(MOTIVO_MAX_LENGTH + 1)),
    `O motivo deve ter no máximo ${MOTIVO_MAX_LENGTH} caracteres.`,
  );
});

test('data é formatada em pt-BR', () => {
  // Data local (não UTC) para o resultado não depender do fuso da máquina.
  assert.equal(formatDate(new Date(2026, 9, 22)), '22/10/2026');
  assert.equal(formatDateTime(new Date(2026, 9, 22, 14, 5)), '22/10/2026, 14:05:00');
});
