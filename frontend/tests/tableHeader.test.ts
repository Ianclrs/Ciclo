import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

/**
 * Guarda do alinhamento do cabeçalho das tabelas.
 *
 * Dois defeitos reais, medidos no app (larguras de 1440 px e 1024 px):
 *
 * 1. O `th` centraliza por padrão no navegador e o `td` não. A coluna "Arquivo" — a única sem
 *    `className` — ficava com o rótulo 100 px à direita do ícone que ele nomeia.
 * 2. Rótulo sem `whitespace-nowrap` quebrava em duas linhas quando a janela encolhia
 *    ("Tipo de documento" em 104 px de coluna), deixando a linha de cabeçalho mais alta que as
 *    linhas de dados.
 *
 * A tentação ao corrigir (1) é jogar `text-left` na classe base. Isso NÃO funciona:
 * `text-left` e `text-center` são a mesma propriedade e a ordem da folha de estilo decide —
 * medido, `text-left` ganhava e as colunas centradas passavam a ficar com o rótulo à esquerda
 * de um conteúdo centralizado. Por isso o alinhamento padrão é condicional.
 */

const table = readFileSync(fileURLToPath(new URL('../src/components/Table.tsx', import.meta.url)), 'utf8');

test('o rótulo do cabeçalho não quebra em duas linhas', () => {
  assert.match(table, /whitespace-nowrap/, 'o rótulo volta a quebrar quando a coluna encolhe');
});

test('o alinhamento padrão do cabeçalho respeita a coluna', () => {
  assert.match(
    table,
    /DECLARED_ALIGNMENT\.test\(col\.className \?\? ''\) \? '' : 'text-left'/,
    'o alinhamento padrão deixou de ser condicional: emitir text-left junto com o text-center da coluna inverte o resultado',
  );

  // A base não pode fixar alinhamento: quem decide é a coluna ou a condicional acima.
  const baseClass = /className=\{`whitespace-nowrap pb-3 ([^$`]*)/.exec(table);
  assert.ok(baseClass, 'a classe base do cabeçalho mudou de forma inesperada');
  assert.doesNotMatch(
    baseClass[1],
    /text-(left|center|right)/,
    'a classe base fixa alinhamento e sobrepõe a escolha da coluna',
  );
});
