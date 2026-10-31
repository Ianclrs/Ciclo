import test from 'node:test';
import assert from 'node:assert/strict';
import {
  DEFAULT_LOCALE,
  formatCurrency,
  formatDate,
  formatDateTime,
  formatNumber,
  formatPercent,
} from '../src/utils/format.ts';

/** Instante e valor fixos: a saída não pode depender da máquina nem do relógio. */
const instant = new Date(2026, 9, 22, 14, 5, 0);
const amount = 1234.5;

test('o locale padrão continua pt-BR', () => {
  assert.equal(DEFAULT_LOCALE, 'pt-BR');
});

test('a data muda de ordem entre pt-BR e en-US', () => {
  assert.equal(formatDate(instant, 'pt-BR'), '22/10/2026');
  assert.equal(formatDate(instant, 'en-US'), '10/22/2026');
  assert.notEqual(formatDate(instant, 'pt-BR'), formatDate(instant, 'en-US'));
});

test('pt-PT mantém dd/mm/aaaa na data — a região aparece em número e moeda', () => {
  // Os dois português usam a mesma ordem de data na precisão de dia; a diferença regional
  // existe e é verificada abaixo em `formatNumber` e `formatCurrency`.
  assert.equal(formatDate(instant, 'pt-PT'), formatDate(instant, 'pt-BR'));
});

test('o número difere nos três locales', () => {
  const ptBr = formatNumber(1234567.89, 'pt-BR');
  const enUs = formatNumber(1234567.89, 'en-US');
  const ptPt = formatNumber(1234567.89, 'pt-PT');

  assert.equal(ptBr, '1.234.567,89');
  assert.equal(enUs, '1,234,567.89');
  assert.notEqual(ptBr, enUs);
  assert.notEqual(ptBr, ptPt);
  assert.notEqual(enUs, ptPt);
});

test('a moeda difere nos três locales para o mesmo valor', () => {
  const ptBr = formatCurrency(amount, 'BRL', 'pt-BR');
  const enUs = formatCurrency(amount, 'BRL', 'en-US');
  const ptPt = formatCurrency(amount, 'BRL', 'pt-PT');

  assert.match(ptBr, /1\.234,50/);
  assert.match(enUs, /1,234\.50/);
  assert.match(ptPt, /1234,50/);
  assert.notEqual(ptBr, enUs);
  assert.notEqual(ptBr, ptPt);
  assert.notEqual(enUs, ptPt);
});

test('percentual usa a regra do locale e aceita opções', () => {
  assert.equal(formatPercent(0.256, 'pt-BR'), '26%');
  assert.equal(formatPercent(0.256, 'pt-BR', { maximumFractionDigits: 1 }), '25,6%');
});

test('data e hora inválidas devolvem string vazia', () => {
  assert.equal(formatDate('not-a-date'), '');
  assert.equal(formatDateTime('not-a-date'), '');
  assert.equal(formatDate('not-a-date', 'en-US'), '');
});

test('número inválido devolve string vazia em todos os formatadores', () => {
  assert.equal(formatNumber(Number.NaN), '');
  assert.equal(formatCurrency(Number.NaN, 'BRL'), '');
  assert.equal(formatPercent(Number.NaN), '');
});

test('moeda com código inválido propaga o RangeError do Intl em vez de mascarar', () => {
  // Erro de programação deve aparecer em desenvolvimento, não virar texto vazio na tela.
  assert.throws(() => formatCurrency(amount, 'BR'), RangeError);
});

test('a chamada sem locale reproduz exatamente a saída pt-BR', () => {
  assert.equal(formatDate(instant), formatDate(instant, 'pt-BR'));
  assert.equal(formatDateTime(instant), formatDateTime(instant, 'pt-BR'));
  assert.equal(formatNumber(amount), formatNumber(amount, 'pt-BR'));
  assert.equal(formatCurrency(amount, 'BRL'), formatCurrency(amount, 'BRL', 'pt-BR'));
  assert.equal(formatPercent(0.256), formatPercent(0.256, 'pt-BR'));
});
