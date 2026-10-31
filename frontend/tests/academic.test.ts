import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import {
  formatDateOnly,
  formatGrade,
  parseGradeInput,
  situacaoLabel,
  situacaoVariant,
} from '../src/domain/academic.ts';

const source = (relative: string) =>
  readFileSync(fileURLToPath(new URL(`../src/${relative}`, import.meta.url)), 'utf8');

test('situação do componente tem rótulo e cor para todo o vocabulário do backend', () => {
  assert.equal(situacaoLabel('aprovado'), 'Aprovado');
  assert.equal(situacaoLabel('recuperacao'), 'Em recuperação');
  assert.equal(situacaoLabel('reprovado'), 'Reprovado');
  assert.equal(situacaoLabel('sem_nota'), 'Sem nota');
  assert.equal(situacaoLabel('outro'), 'Sem situação');

  assert.equal(situacaoVariant('aprovado'), 'success');
  assert.equal(situacaoVariant('recuperacao'), 'warning');
  assert.equal(situacaoVariant('reprovado'), 'danger');
  assert.equal(situacaoVariant('sem_nota'), 'default');
});

test('nota fora de 0 a 10 é recusada antes de chegar à API', () => {
  const above = parseGradeInput('10,1');
  assert.equal(above.ok, false);
  assert.match(above.ok === false ? above.error : '', /entre 0 e 10/);

  assert.equal(parseGradeInput('-1').ok, false);
  assert.equal(parseGradeInput('').ok, false);
  assert.equal(parseGradeInput('abc').ok, false);

  assert.deepEqual(parseGradeInput('0'), { ok: true, value: 0 });
  assert.deepEqual(parseGradeInput('10'), { ok: true, value: 10 });

  // Teclado numérico em pt-BR produz vírgula: o envio precisa de ponto.
  assert.deepEqual(parseGradeInput('7,5'), { ok: true, value: 7.5 });
  assert.deepEqual(parseGradeInput(' 8 '), { ok: true, value: 8 });
});

test('nota exibida usa a vírgula do pt-BR e travessão quando não há valor', () => {
  assert.equal(formatGrade(null), '—');
  assert.equal(formatGrade(7.5), '7,5');
  assert.equal(formatGrade(10), '10');
});

test('data de DateOnly não desloca um dia em fuso negativo', () => {
  // `new Date('2026-03-10')` é meia-noite UTC; no Brasil (UTC-3) exibiria 09/03.
  assert.equal(formatDateOnly('2026-03-10'), '10/03/2026');
  assert.equal(formatDateOnly('2026-03-10T00:00:00'), '10/03/2026');
});

test('as rotas administrativas e o boletim estão ligados às telas novas', () => {
  const app = source('App.tsx');
  assert.match(app, /path="academico" element=\{<AcademicStructure \/>\}/, 'a rota /admin/academico não está registrada');
  assert.match(app, /path="notas" element=\{<GradeEntry \/>\}/, 'a rota /admin/notas não está registrada');

  const parent = source('api/parent.ts');
  assert.match(parent, /children\/\$\{id\}\/report-card/, 'o boletim não chama a rota report-card');

  const detail = source('pages/parent/ChildDetail.tsx');
  assert.match(detail, /getChildReportCard/, 'a aba de notas não consome o boletim');
  assert.doesNotMatch(detail, /child\.grades/, 'a aba de notas voltou a usar a lista resumida');
});

test('a tela de notas não fica órfã: sai da estrutura acadêmica', () => {
  // A rota existe sozinha, mas o menu já usa todas as cores da paleta. Sem este link a tela
  // seria alcançável apenas digitando a URL.
  const structure = source('pages/admin/AcademicStructure.tsx');
  assert.match(structure, /navigate\('\/admin\/notas'\)/, 'a tela de notas ficou sem caminho de entrada');
});

test('o lançamento de notas valida a faixa no formulário e usa o cliente da API', () => {
  const page = source('pages/admin/GradeEntry.tsx');
  assert.match(page, /parseGradeInput/, 'a tela não valida a nota antes do envio');
  assert.match(page, /upsertGrade/, 'a tela não grava a nota pela API');
  assert.match(page, /listClassStudents/, 'a tela não carrega o roster da turma');
});
