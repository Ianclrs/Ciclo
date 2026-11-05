import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

/**
 * Guarda da rolagem horizontal da tabela de documentos.
 *
 * A tabela é larga (arquivo, foto, aluno, tipo, status, validade, ações) e transborda em telas
 * médias. A tela esconde a barra nativa — que sobre o card de vidro pesa no desenho — e oferece
 * o arrasto com o mouse como caminho alternativo até as colunas da direita.
 *
 * O `Table` é compartilhado com Alunos e Matrículas: as duas opções são independentes e
 * opcionais justamente para que nada mude nas outras duas telas.
 */

const source = (relative: string) =>
  readFileSync(fileURLToPath(new URL(`../src/${relative}`, import.meta.url)), 'utf8');

test('a tabela de documentos esconde a barra e habilita o arrasto', () => {
  const page = source('pages/admin/DocumentVerification.tsx');

  assert.match(
    page,
    /<Table[\s\S]{0,240}hideScrollbar/,
    'a tabela de documentos voltou a mostrar a barra de rolagem horizontal',
  );
  assert.match(
    page,
    /<Table[\s\S]{0,240}dragToScroll/,
    'a tabela esconde a barra mas não oferece o arrasto: o conteúdo à direita fica inalcançável',
  );
});

test('esconder a barra e arrastar são opções independentes da tabela compartilhada', () => {
  const table = source('components/Table.tsx');

  assert.match(table, /hideScrollbar = false/, 'esconder a barra deixou de ser opcional');
  assert.match(table, /dragToScroll = false/, 'o arrasto deixou de ser opcional');
  assert.match(table, /'overflow-x-auto'/, 'a rolagem horizontal da tabela foi removida');
  assert.match(table, /hideScrollbar \? 'no-scrollbar' : ''/, 'o utilitário de barra oculta não é aplicado');
  assert.match(
    table,
    /const scrollRef = useDragToScroll<HTMLDivElement>/,
    'o contêiner de rolagem não está ligado ao arrasto',
  );

  // O utilitário que esconde a barra sem desligar a rolagem continua sendo o do CSS global.
  assert.match(source('index.css'), /\.no-scrollbar \{/);
  assert.match(source('index.css'), /scrollbar-width: none/);
});

test('o arrasto não sequestra os controles nem transforma clique em rolagem', () => {
  const hook = source('hooks/useDragToScroll.ts');

  assert.match(
    hook,
    /const INTERACTIVE = 'button, a, input, select, textarea, label/,
    'sem excluir os controles, o botão de aprovar fica inalcançável com o mouse',
  );
  assert.match(hook, /target\?\.closest\(INTERACTIVE\)/);
  assert.match(hook, /DRAG_THRESHOLD/, 'não há distância mínima: qualquer tremor vira arrasto');
  assert.match(
    hook,
    /element\.scrollWidth <= element\.clientWidth/,
    'tabela que cabe na tela entra em modo de arrasto',
  );
  assert.match(hook, /swallowClick/, 'o clique disparado após o arrasto não é cancelado');
  assert.match(hook, /element\.scrollLeft = startScrollLeft - delta/, 'o arrasto não move a rolagem');
  assert.match(hook, /element\.style\.userSelect = ''/, 'a seleção de texto não é restaurada ao soltar');
});

test('as outras tabelas mantêm a barra e o comportamento padrão', () => {
  for (const page of ['pages/admin/StudentList.tsx', 'pages/admin/EnrollmentList.tsx']) {
    const text = source(page);

    assert.doesNotMatch(text, /hideScrollbar/, `${page} escondeu a barra sem que isso tenha sido decidido`);
    assert.doesNotMatch(text, /dragToScroll/, `${page} ganhou arrasto sem que isso tenha sido decidido`);
  }
});
