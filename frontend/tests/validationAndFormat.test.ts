import test from 'node:test';
import assert from 'node:assert/strict';
import { MOTIVO_MAX_LENGTH, MOTIVO_MIN_LENGTH, motivoValidationError } from '../src/domain/validation.ts';
import { t } from '../src/i18n/messages.ts';

const translatePt = (key, params) => t(key, 'pt', params);
import {
  formatCurrency,
  formatDate,
  formatDateTime,
  formatNumber,
  formatPercent,
} from '../src/utils/format.ts';

test('motivo aceito apenas na faixa que o backend aceita', () => {
  assert.equal(motivoValidationError('   ', translatePt), 'Informe o motivo da rejeição.');
  assert.equal(
    motivoValidationError('curto', translatePt),
    `O motivo deve ter ao menos ${MOTIVO_MIN_LENGTH} caracteres.`,
  );
  assert.equal(motivoValidationError('a'.repeat(9), translatePt), `O motivo deve ter ao menos ${MOTIVO_MIN_LENGTH} caracteres.`);
  assert.equal(motivoValidationError('a'.repeat(MOTIVO_MIN_LENGTH), translatePt), null);
  assert.equal(motivoValidationError('a'.repeat(MOTIVO_MAX_LENGTH), translatePt), null);
  assert.equal(
    motivoValidationError('a'.repeat(MOTIVO_MAX_LENGTH + 1), translatePt),
    `O motivo deve ter no máximo ${MOTIVO_MAX_LENGTH} caracteres.`,
  );
});

test('data é formatada em pt-BR', () => {
  // Data local (não UTC) para o resultado não depender do fuso da máquina.
  assert.equal(formatDate(new Date(2026, 9, 22)), '22/10/2026');
  assert.equal(formatDateTime(new Date(2026, 9, 22, 14, 5)), '22/10/2026, 14:05:00');
});

test('sem locale, todo formatador mantém a saída pt-BR anterior (Spec 190)', () => {
  const instant = new Date(2026, 9, 22, 14, 5, 0);

  assert.equal(formatDate(instant), formatDate(instant, 'pt-BR'));
  assert.equal(formatDateTime(instant), formatDateTime(instant, 'pt-BR'));
  assert.equal(formatNumber(1234567.89), '1.234.567,89');
  assert.match(formatCurrency(1234.5, 'BRL'), /1\.234,50/);
  assert.equal(formatPercent(0.256), '26%');
});
