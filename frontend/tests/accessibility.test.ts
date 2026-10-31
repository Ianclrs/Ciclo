import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { FOCUSABLE_SELECTOR, nextFocusIndex } from '../src/utils/focusTrap.ts';
import { AA_TEXT_MIN, contrastRatio, relativeLuminance } from '../src/utils/contrast.ts';
import { TEXT_CONTRAST_PAIRS } from '../src/config/contrastPairs.ts';

/**
 * Guarda de acessibilidade da Spec 190 (FR-006, FR-007, FR-008).
 *
 * O projeto não tem biblioteca de DOM: as regras puras (foco e contraste) são asseridas
 * direto, e os componentes são verificados por inspeção literal do arquivo — o mesmo padrão
 * das demais guardas de contrato do frontend.
 */

const HERE = dirname(fileURLToPath(import.meta.url));
const SRC = join(HERE, '..', 'src');

function read(relativePath: string): string {
  return readFileSync(join(SRC, relativePath), 'utf8');
}

// ── Fronteira de erro (FR-006) ────────────────────────────────────────────────

test('a fronteira de erro deriva o estado do erro capturado', () => {
  const source = read('components/ErrorBoundary.tsx');
  assert.match(source, /static getDerivedStateFromError/);
  assert.match(source, /t\('errorBoundary\.title'/);
  assert.match(source, /t\('errorBoundary\.body'/);
  assert.match(source, /window\.location\.reload\(\)/);
});

test('a fronteira respeita o fallback do chamador e isola o relato do erro', () => {
  const source = read('components/ErrorBoundary.tsx');

  // O fallback explícito vence o padrão localizado.
  assert.match(source, /this\.props\.fallback/);
  assert.match(source, /role="alert"/);

  // O relato em `componentDidCatch` não pode relançar e reentrar na fronteira.
  const report = source.slice(source.indexOf('componentDidCatch'));
  assert.match(report.slice(0, report.indexOf('render')), /try\s*\{[\s\S]*catch\s*\{/);
});

test('a fronteira é montada como elemento mais externo da aplicação', () => {
  const source = read('App.tsx');
  assert.match(source, /<ErrorBoundary>/);

  // O `ErrorBoundary` precisa abrir antes do `LocaleProvider`: se o próprio provider lançar,
  // a fronteira tem de estar acima dele.
  assert.ok(
    source.indexOf('<ErrorBoundary>') < source.indexOf('<LocaleProvider>'),
    'ErrorBoundary precisa envolver LocaleProvider',
  );
});

// ── Ciclo de foco (FR-007) ───────────────────────────────────────────────────

test('o seletor de focáveis declara os controles reais', () => {
  for (const fragment of ['a[href]', 'button:not([disabled])', 'input:not([disabled])', 'tabindex']) {
    assert.ok(FOCUSABLE_SELECTOR.includes(fragment), `seletor sem ${fragment}`);
  }
});

test('nextFocusIndex avança e volta com wrap-around', () => {
  assert.equal(nextFocusIndex(0, 3, false), 1);
  assert.equal(nextFocusIndex(1, 3, false), 2);
  assert.equal(nextFocusIndex(2, 3, false), 0);

  assert.equal(nextFocusIndex(0, 3, true), 2);
  assert.equal(nextFocusIndex(2, 3, true), 1);
  assert.equal(nextFocusIndex(1, 3, true), 0);
});

test('nextFocusIndex normaliza índice fora da faixa e lista vazia', () => {
  assert.equal(nextFocusIndex(-1, 3, false), 0);
  assert.equal(nextFocusIndex(-1, 3, true), 1);
  assert.equal(nextFocusIndex(7, 3, false), 2);
  assert.equal(nextFocusIndex(0, 0, false), 0);
  assert.equal(nextFocusIndex(0, 0, true), 0);
});

test('o modal declara diálogo, prende o foco e fecha com Escape', () => {
  const source = read('components/Modal.tsx');
  assert.match(source, /role="dialog"/);
  assert.match(source, /aria-modal="true"/);
  assert.match(source, /aria-labelledby=\{titleId\}/);
  assert.match(source, /'Escape'/);
  assert.match(source, /nextFocusIndex/);
  assert.match(source, /previousFocusRef/);
  assert.match(source, /document\.body/);
});

test('input e select ligam rótulo e controle e expõem o erro', () => {
  for (const file of ['components/Input.tsx', 'components/Select.tsx']) {
    const source = read(file);
    assert.match(source, /htmlFor=\{fieldId\}/, `${file} sem htmlFor`);
    assert.match(source, /id=\{fieldId\}/, `${file} sem id`);
    assert.match(source, /useId\(\)/, `${file} sem id gerado`);
  }

  const input = read('components/Input.tsx');
  assert.match(input, /aria-invalid/);
  assert.match(input, /aria-describedby/);
});

test('o botão aceita nome acessível para uso só de ícone', () => {
  const source = read('components/Button.tsx');
  assert.match(source, /ariaLabel\?: string/);
  assert.match(source, /aria-label=\{ariaLabel\}/);
});

// ── Contraste (FR-008) ───────────────────────────────────────────────────────

test('o ajudante de contraste devolve os valores conhecidos', () => {
  assert.equal(AA_TEXT_MIN, 4.5);
  assert.equal(contrastRatio('#000000', '#ffffff'), 21);
  assert.equal(contrastRatio('#ffffff', '#000000'), 21);

  const sameColour = contrastRatio('#374151', '#374151');
  assert.ok(Math.abs(sameColour - 1) < 1e-12, `mesma cor deveria dar 1, deu ${sameColour}`);
  assert.equal(relativeLuminance('#000000'), 0);
  assert.equal(relativeLuminance('#FFFFFF'), 1);
});

test('hex malformado lança RangeError citando o valor', () => {
  for (const invalid of ['#abc', 'rgb(0,0,0)', '', '#GGGGGG']) {
    assert.throws(
      () => relativeLuminance(invalid),
      (error: unknown) => error instanceof RangeError && String(error.message).includes(invalid),
    );
  }
});

test('todo par declarado alcança o mínimo AA', () => {
  assert.ok(TEXT_CONTRAST_PAIRS.length >= 4);

  for (const pair of TEXT_CONTRAST_PAIRS) {
    const ratio = contrastRatio(pair.foreground, pair.background);
    assert.ok(
      ratio >= AA_TEXT_MIN,
      `${pair.name} tem contraste ${ratio.toFixed(2)}, abaixo de ${AA_TEXT_MIN}`,
    );
  }
});
