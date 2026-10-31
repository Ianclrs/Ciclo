import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

/**
 * A transição entre seções é silenciosa quando quebra: o menu continua navegando e nada
 * aparece errado — só deixa de existir o efeito. Ela depende de duas coisas que vivem em
 * arquivos diferentes:
 *
 * 1. a classe `.section-enter` com a animação e o desligamento por `prefers-reduced-motion`;
 * 2. a camada que envolve a seção no `Sidebar` remontada por rota (`key`), porque uma
 *    animação de entrada só roda quando o elemento entra no DOM.
 */

const root = new URL('..', import.meta.url);

function readText(path: string): Promise<string> {
  return readFile(new URL(path, root), 'utf8');
}

test('a seção entra com animação curta e desliza para o lugar', async () => {
  const css = await readText('src/index.css');
  const keyframes = css.match(/@keyframes section-enter\s*\{[^}]*\}[^}]*\}/)?.[0];

  assert.ok(keyframes, '@keyframes `section-enter` ausente');

  // Medido no app real: com `opacity: 0` no início, a área de conteúdo ficava invisível ou
  // translúcida por ~195 ms em toda troca — a piscada. O conteúdo tem de nascer opaco.
  assert.doesNotMatch(
    keyframes,
    /opacity/,
    'a animação de seção não pode mexer em opacidade: a área de conteúdo nasce opaca',
  );
  assert.match(
    keyframes,
    /from\s*\{[^}]*transform:\s*translateY\(\d+px\)/,
    'a seção precisa entrar deslocada',
  );
  assert.match(
    keyframes,
    /to\s*\{[^}]*transform:\s*translateY\(0\)/,
    'o deslocamento precisa terminar no lugar, senão a seção fica desalinhada',
  );
  assert.match(css, /\.section-enter\s*\{[^}]*animation:\s*section-enter\s+\d+ms/, 'a classe não roda a animação');
});

test('quem pediu menos movimento não vê a troca de seção', async () => {
  const css = await readText('src/index.css');
  const block = css.match(/@media \(prefers-reduced-motion: reduce\)\s*\{[\s\S]*?\n\}/)?.[0];

  assert.ok(block, 'bloco de `prefers-reduced-motion` ausente');
  assert.match(block, /\.section-enter\s*\{\s*animation:\s*none;/, 'a seção continua animando com movimento reduzido');
});

test('o Sidebar remonta a seção por rota para a animação reiniciar', async () => {
  const source = await readText('src/components/Sidebar.tsx');

  // Sem `key` por rota o React reaproveita o mesmo nó entre navegações e a animação de
  // entrada nunca reinicia: a troca acontece, mas "seca".
  assert.match(
    source,
    /<div key=\{location\.pathname\} className="section-enter">/,
    'a camada da seção precisa ser remontada por rota',
  );
});
