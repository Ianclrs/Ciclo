import test from 'node:test';
import assert from 'node:assert/strict';
import { matches } from '../src/utils/search.ts';

test('busca ignora maiúsculas e acentos', () => {
  assert.equal(matches('José da Silva', 'jose'), true);
  assert.equal(matches('Matrícula 2025', 'MATRICULA'), true);
  assert.equal(matches('Ação', 'acao'), true);
});

test('busca aceita trecho no meio do texto', () => {
  assert.equal(matches('Maria Aparecida', 'apare'), true);
  assert.equal(matches('maria@escola.test', 'ESCOLA'), true);
});

test('busca vazia não filtra nada', () => {
  assert.equal(matches('qualquer coisa', ''), true);
  assert.equal(matches('qualquer coisa', '   '), true);
});

test('busca sem correspondência é falsa', () => {
  assert.equal(matches('José', 'maria'), false);
});
