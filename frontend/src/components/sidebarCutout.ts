/**
 * Geometria do recorte da sidebar — o "furo" por onde o shader aparece.
 *
 * Vive fora do componente porque é cálculo puro: `Sidebar.tsx` mede o item ativo e
 * entrega o retângulo medido; aqui ele vira a camada de máscara. Assim a regra pode
 * ser testada sem browser.
 */

export interface CutoutRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface CutoutMask {
  /** Camada de máscara que o CSS SUBTRAI do vidro. */
  image: string;
  /**
   * Caixa em que a imagem precisa ser posicionada (`mask-size` / `mask-position`).
   * É mais ALTA que o item: a abertura do lado direito cresce para fora do retângulo,
   * por cima e por baixo dele.
   */
  box: CutoutRect;
}

/** Raio da curva da esquerda — o mesmo `rounded-lg` do item de menu. */
export const CUTOUT_RADIUS = 8;

/**
 * Quanto a curva do lado direito avança para fora do item, em px. É o que faz a
 * abertura encostar nas seções vizinhas, acima e abaixo.
 */
export const CUTOUT_FLARE = 12;

function image(svg: string): string {
  return `url("data:image/svg+xml,${encodeURIComponent(svg)}")`;
}

function canvas(width: number, height: number, shape: string): string {
  return image(
    `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}"` +
      ` viewBox="0 0 ${width} ${height}">${shape}</svg>`,
  );
}

/**
 * Máscara do furo, usada como a camada que SUBTRAI a área (`mask-composite: exclude`).
 *
 * O caminho tem duas curvas de naturezas opostas, e é isso que dá o desenho:
 *
 * - à ESQUERDA, os cantos são arredondados para dentro do item (raio `CUTOUT_RADIUS`),
 *   continuando a pílula do menu;
 * - à DIREITA, as curvas viram para FORA: saem da aresta de cima/baixo tangencialmente,
 *   sobem (e descem) e terminam na borda da sidebar. O vão fica mais alto justamente
 *   onde encosta na borda, invadindo a faixa das seções vizinhas.
 *
 * Por isso a imagem é mais alta que o item (`height + 2 × flare`) e o corpo do furo
 * começa em `y = flare` dentro dela. O CSS usa exatamente o tamanho da imagem em
 * `mask-size`: sem essa igualdade o desenho seria escalado e as curvas distorcidas.
 *
 * As duas curvas da direita usam `sweep-flag 0` (sentido anti-horário) — com `1` a
 * curva entraria para dentro do recorte, que é o efeito oposto ao pretendido.
 */
export function cutoutMask(
  rect: CutoutRect,
  radius: number = CUTOUT_RADIUS,
  flare: number = CUTOUT_FLARE,
): CutoutMask {
  const width = Math.max(0, Math.round(rect.width));
  const height = Math.max(0, Math.round(rect.height));
  const x = Math.round(rect.x);
  const y = Math.round(rect.y);

  // Área sem tamanho não é recorte: devolve uma caixa vazia e o chamador desliga o furo.
  if (width === 0 || height === 0) {
    return { image: canvas(0, 0, `<rect width="0" height="0"/>`), box: { x, y, width: 0, height: 0 } };
  }

  // Acima da metade do lado o SVG desenha elipse; os limites preservam as curvas circulares.
  const r = Math.max(0, Math.min(radius, width / 2, height / 2));
  const f = Math.max(0, Math.min(flare, width / 2));
  const canvasHeight = height + f * 2;
  const bottom = f + height;

  // Sentido horário a partir do canto superior esquerdo: aresta de cima até a curva da
  // direita, desce pela borda da sidebar, curva de volta, aresta de baixo, curvas à esquerda.
  const d =
    `M${r} ${f}` +
    `H${width - f}` +
    `A${f} ${f} 0 0 0 ${width} 0` +
    `V${canvasHeight}` +
    `A${f} ${f} 0 0 0 ${width - f} ${bottom}` +
    `H${r}` +
    `A${r} ${r} 0 0 1 0 ${bottom - r}` +
    `V${f + r}` +
    `A${r} ${r} 0 0 1 ${r} ${f}` +
    `Z`;

  return {
    image: canvas(width, canvasHeight, `<path d="${d}"/>`),
    box: { x, y: y - f, width, height: canvasHeight },
  };
}
