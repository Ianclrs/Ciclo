import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import {
  formatDateOnly,
  formatGrade,
  parseGradeInput,
  situacaoLabelKey,
  situacaoVariant,
} from '../src/domain/academic.ts';
import { t } from '../src/i18n/messages.ts';

/** Tradutor de pt injetado no domínio, que não conhece o catálogo. */
const translatePt = (key: Parameters<typeof t>[0], params?: Record<string, string | number>) =>
  t(key, 'pt', params);

const source = (relative: string) =>
  readFileSync(fileURLToPath(new URL(`../src/${relative}`, import.meta.url)), 'utf8');

test('situação do componente tem rótulo e cor para todo o vocabulário do backend', () => {
  assert.equal(situacaoLabelKey('aprovado'), 'academic.status.approved');
  assert.equal(situacaoLabelKey('recuperacao'), 'academic.status.recovery');
  assert.equal(situacaoLabelKey('reprovado'), 'academic.status.failed');
  assert.equal(situacaoLabelKey('sem_nota'), 'academic.status.noGrade');
  assert.equal(situacaoLabelKey('outro'), 'academic.status.none');

  assert.equal(situacaoVariant('aprovado'), 'success');
  assert.equal(situacaoVariant('recuperacao'), 'warning');
  assert.equal(situacaoVariant('reprovado'), 'danger');
  assert.equal(situacaoVariant('sem_nota'), 'default');
});

test('nota fora de 0 a 10 é recusada antes de chegar à API', () => {
  const above = parseGradeInput('10,1', translatePt);
  assert.equal(above.ok, false);
  assert.match(above.ok === false ? above.error : '', /entre 0 e 10/);

  assert.equal(parseGradeInput('-1', translatePt).ok, false);
  assert.equal(parseGradeInput('', translatePt).ok, false);
  assert.equal(parseGradeInput('abc', translatePt).ok, false);

  assert.deepEqual(parseGradeInput('0', translatePt), { ok: true, value: 0 });
  assert.deepEqual(parseGradeInput('10', translatePt), { ok: true, value: 10 });

  // Teclado numérico em pt-BR produz vírgula: o envio precisa de ponto.
  assert.deepEqual(parseGradeInput('7,5', translatePt), { ok: true, value: 7.5 });
  assert.deepEqual(parseGradeInput(' 8 ', translatePt), { ok: true, value: 8 });
});

test('a mensagem de erro da nota acompanha o idioma pedido', () => {
  const pt = parseGradeInput('abc', translatePt);
  const en = parseGradeInput('abc', (key, params) => t(key, 'en', params));

  assert.equal(pt.ok, false);
  assert.equal(en.ok, false);
  // Mesma chave, idiomas diferentes: o texto não pode ser o mesmo.
  assert.notEqual(pt.ok === false ? pt.error : '', en.ok === false ? en.error : '');
});

test('nota exibida usa o separador do locale e travessão quando não há valor', () => {
  assert.equal(formatGrade(null), '—');
  assert.equal(formatGrade(7.5, 'pt-BR'), '7,5');
  assert.equal(formatGrade(7.5, 'en-US'), '7.5');
  assert.equal(formatGrade(10, 'pt-BR'), '10');
});

test('data de DateOnly não desloca um dia em fuso negativo', () => {
  // `new Date('2026-03-10')` é meia-noite UTC; no Brasil (UTC-3) exibiria 09/03.
  assert.equal(formatDateOnly('2026-03-10'), '10/03/2026');
  // O formato segue o locale: em inglês a ordem é mês/dia, nunca dia/mês.
  assert.notEqual(formatDateOnly('2026-03-10', 'en-US'), '10/03/2026');
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
