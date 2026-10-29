import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { CUTOUT_FLARE, CUTOUT_RADIUS, cutoutMask } from '../src/components/sidebarCutout.ts';

/**
 * O recorte da sidebar é silencioso quando quebra: o menu continua funcionando, o build
 * passa, e a única diferença é que o item ativo volta a ficar sem o furo no vidro. Ele
 * depende de acoplamentos que vivem em arquivos diferentes:
 *
 * 1. a regra CSS que SUBTRAI a segunda camada de máscara (sem `exclude` não há furo);
 * 2. a superfície que recebe `data-hole` e as medidas `--hole-*` + `--hole-mask`;
 * 3. o `aria-current` do link ativo, que é como a medição encontra o item;
 * 4. o SVG do furo com o mesmo tamanho da caixa publicada, senão as curvas distorcem.
 */

const root = new URL('..', import.meta.url);

function readText(path: string): Promise<string> {
  return readFile(new URL(path, root), 'utf8');
}

function decodeMask(url: string): string {
  const encoded = url.match(/^url\("data:image\/svg\+xml,(.*)"\)$/)?.[1];
  assert.ok(encoded, `máscara fora do formato esperado: ${url}`);
  return decodeURIComponent(encoded);
}

test('o CSS subtrai a camada do furo em vez de somar máscaras', async () => {
  const css = await readText('src/index.css');
  const rule = css.match(/\.sidebar-cutout\[data-hole='on'\]\s*\{[^}]*\}/)?.[0];

  assert.ok(rule, "regra `.sidebar-cutout[data-hole='on']` ausente");
  assert.match(rule, /mask-composite:\s*exclude/, 'sem `exclude` as camadas se somam e não há furo');

  const layers = rule.match(/[^-]mask-image:\s*([^;]+);/)?.[1] ?? '';
  assert.match(layers, /linear-gradient\(#000, #000\)/, 'falta a camada que cobre o elemento inteiro');
  assert.match(layers, /var\(--hole-mask\)/, 'falta a camada do furo (retângulo arredondado)');

  for (const measure of ['--hole-x', '--hole-y', '--hole-w', '--hole-h']) {
    assert.match(rule, new RegExp(measure), `${measure} é necessária para posicionar o furo`);
  }
});

test('a sidebar usa a tinta dos cards, sem gradiente na borda', async () => {
  const source = await readText('src/components/Sidebar.tsx');
  const card = await readText('src/components/Card.tsx');

  // A tinta da sidebar é a MESMA do card de vidro. É essa igualdade que o teste guarda:
  // se um dos dois mudar de tom, a sidebar deixa de pertencer à família das superfícies.
  assert.match(card, /glass:\s*'bg-white\/70/, 'o card de vidro não usa mais a tinta de referência');
  assert.equal(
    (source.match(/sidebar-cutout bg-white\/70/g) ?? []).length,
    2,
    'as duas superfícies (desktop e mobile) usam a tinta dos cards',
  );

  // Gradiente de opacidade borrava a borda direita de TODAS as seções — inclusive as não
  // selecionadas — e o vinco da seleção perdia contraste. A borda é reta, como a de um card.
  assert.doesNotMatch(source, /linear-gradient|sidebar-glass/, 'a tinta não pode ser um gradiente');
  assert.doesNotMatch(source, /shadow-lg/, 'sombra na sidebar desenha uma barra de separação');
  assert.doesNotMatch(source, /backdrop-blur/, 'desfoque chapado cria emenda na borda');
});

test('o furo desliza na troca de item e não anima durante rolagem', async () => {
  const css = await readText('src/index.css');

  assert.match(
    css,
    /transition:\s*-webkit-mask-position[^;]*mask-position/,
    'sem transição de `mask-position` o furo salta em vez de deslizar',
  );
  assert.match(
    css,
    /\[data-hole='on'\]\[data-hole-instant\]\s*\{\s*transition:\s*none;/,
    'rolagem e resize precisam da passagem instantânea',
  );
  assert.match(
    css,
    /@media \(prefers-reduced-motion: reduce\)\s*\{\s*\.sidebar-cutout\[data-hole='on'\]\s*\{\s*transition:\s*none;/,
    'quem pediu menos movimento não deve ver o furo deslizando',
  );
});

test('a sidebar mede o item ativo e publica as medidas na superfície', async () => {
  const source = await readText('src/components/Sidebar.tsx');

  assert.match(source, /data-sidebar-surface/, 'a camada de vidro precisa ser localizável');
  assert.match(source, /data-hole/, 'sem `data-hole` a regra de recorte nunca é ativada');
  for (const measure of ['--hole-x', '--hole-y', '--hole-w', '--hole-h']) {
    assert.match(source, new RegExp(`setProperty\\('${measure}'`), `${measure} não é publicada`);
  }
  assert.match(source, /setProperty\('--hole-mask'/, 'a máscara do furo não é publicada');
  assert.match(source, /data-hole-instant/, 'a passagem instantânea nunca é marcada');

  // Posição e altura vêm da caixa calculada (mais alta que o item): usar o retângulo
  // medido cortaria a abertura que cresce para fora.
  assert.match(source, /mask\.box\.y/, 'a posição do furo tem de vir da caixa da máscara');
  assert.match(source, /mask\.box\.height/, 'a altura do furo tem de vir da caixa da máscara');

  // O recorte tem de alcançar a borda direita da sidebar, e a largura sai do MENU —
  // a largura da superfície anima ao abrir/fechar e produziria um furo de tamanho errado.
  assert.match(source, /navBox\.right/, 'a borda direita do recorte vem da borda do menu');
  assert.doesNotMatch(source, /width: activeBox\.width/, 'medir a largura do item deixaria o furo curto');

  assert.match(
    source,
    /\[aria-current="page"\]/,
    'a medição procura o item ativo por `aria-current`',
  );
  assert.match(source, /aria-current=\{active \? 'page' : undefined\}/, 'o link ativo não marca `aria-current`');
});

test('o item ativo não pinta fundo: o destaque é o próprio furo', async () => {
  const source = await readText('src/components/Sidebar.tsx');
  const palette = await readText('src/components/accentStyles.ts');
  const colorMap = source.match(/const colorMap[^=]*=\s*\{[\s\S]*?\n\};/)?.[0];

  assert.ok(colorMap, 'colorMap não encontrado');
  const activeStyles = [...colorMap.matchAll(/active:\s*'([^']*)'/g)].map((m) => m[1]);
  const accentCount = [...(palette.match(/export type Accent = ([^;]+);/)?.[1] ?? '').matchAll(/'(\w+)'/g)].length;
  assert.equal(activeStyles.length, accentCount, 'toda cor da paleta precisa de estado ativo no menu');

  for (const style of activeStyles) {
    assert.doesNotMatch(
      style,
      /\bbg-/,
      `o estilo ativo "${style}" pinta fundo e esconde o recorte`,
    );
  }
});

test('cada seção do menu usa uma cor própria, existente na paleta', async () => {
  const app = await readText('src/App.tsx');
  const palette = await readText('src/components/accentStyles.ts');

  const accents = new Set(
    [...(palette.match(/export type Accent = ([^;]+);/)?.[1] ?? '').matchAll(/'(\w+)'/g)].map((m) => m[1]),
  );
  const sections = [...app.matchAll(/\{ label: '([^']+)', path: '\/admin[^']*'[\s\S]{0,120}?color: '(\w+)'/g)]
    .map((m) => ({ label: m[1], color: m[2] }));

  assert.ok(sections.length >= 6, `esperava o menu de administração completo, achei ${sections.length} seções`);

  for (const { label, color } of sections) {
    // Uma cor fora da paleta não tem entrada em `colorMap` e quebraria a renderização do item.
    assert.ok(accents.has(color), `"${label}" usa a cor "${color}", que não existe na paleta`);
  }

  // O defeito que originou este teste: "Usuários" repetia o violeta do "Dashboard".
  const colors = sections.map((section) => section.color);
  assert.equal(
    new Set(colors).size,
    colors.length,
    `duas seções compartilham a mesma cor: ${colors.join(', ')}`,
  );
});

test('as curvas da direita viram para fora e as da esquerda para dentro', () => {
  const { image } = cutoutMask({ x: 12, y: 44, width: 208, height: 40 });
  const svg = decodeMask(image);
  const r = CUTOUT_RADIUS;
  const f = CUTOUT_FLARE;

  // A imagem é mais alta que o item: a abertura cresce para fora, acima e abaixo.
  assert.match(svg, new RegExp(`width="208" height="${40 + f * 2}"`));
  assert.match(svg, new RegExp(`viewBox="0 0 208 ${40 + f * 2}"`));

  // Curvas da direita: sweep 0 (anti-horário) = vão crescendo para fora, invadindo a
  // faixa das seções vizinhas. Curvas da esquerda: sweep 1, como um rounded-lg.
  const rightCurves = svg.match(new RegExp(`A${f} ${f} 0 0 0`, 'g')) ?? [];
  assert.equal(rightCurves.length, 2, 'as duas curvas da direita precisam virar para fora');
  assert.doesNotMatch(svg, new RegExp(`A${f} ${f} 0 0 1`), 'curva da direita para dentro é o efeito oposto');
  assert.match(svg, new RegExp(`A${r} ${r} 0 0 1`, 'g'));

  // A curva da direita termina na borda da sidebar (x = largura) e sobe até y = 0.
  assert.match(svg, new RegExp(`A${f} ${f} 0 0 0 208 0`));
});

test('a caixa publicada cobre a abertura, que sai do retângulo do item', () => {
  const { box } = cutoutMask({ x: 12, y: 44, width: 208, height: 40 });

  assert.deepEqual(box, {
    x: 12,
    y: 44 - CUTOUT_FLARE,
    width: 208,
    height: 40 + CUTOUT_FLARE * 2,
  });
});

test('curva e abertura são limitadas pela largura disponível', () => {
  const { image } = cutoutMask({ x: 0, y: 0, width: 12, height: 12 });
  const svg = decodeMask(image);

  // Lado de 12px: nem raio nem abertura podem passar de 6.
  assert.match(svg, /A6 6 0 0 1/, 'raio limitado à metade da largura');
  assert.match(svg, /A6 6 0 0 0/, 'abertura limitada à metade da largura');
});

test('medida fracionária é arredondada antes de virar máscara', () => {
  const { image, box } = cutoutMask({ x: 11.6, y: 44.2, width: 207.5, height: 39.6 });

  assert.match(decodeMask(image), /width="208"/);
  assert.equal(box.x, 12);
  assert.equal(box.y, 44 - CUTOUT_FLARE);
});

test('área sem tamanho não desenha furo', () => {
  const { image, box } = cutoutMask({ x: 12, y: 12, width: 0, height: 0 });

  assert.deepEqual(box, { x: 12, y: 12, width: 0, height: 0 });
  assert.match(decodeMask(image), /<rect width="0" height="0"\/>/);
});
