/**
 * Cálculo determinístico de contraste WCAG 2.1 (Spec 190, FR-008).
 *
 * Revisar contraste "no olho" não é verificável; a razão calculada a partir dos tokens
 * declarados é. As fórmulas são as da recomendação: luminância relativa com a correção de
 * gamma por canal e razão entre a mais clara e a mais escura.
 */

/** Mínimo da WCAG 2.1 nível AA para texto normal. */
export const AA_TEXT_MIN = 4.5;

const HEX_PATTERN = /^#[0-9a-fA-F]{6}$/;

function channel(component: number): number {
  const value = component / 255;
  return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
}

/**
 * Luminância relativa de uma cor `#rrggbb`.
 *
 * Um valor malformado (`#abc`, `rgb(0,0,0)`, vazio) lança `RangeError` citando a entrada:
 * devolver uma razão silenciosamente errada esconderia justamente o defeito que a guarda
 * deveria pegar.
 */
export function relativeLuminance(hex: string): number {
  if (!HEX_PATTERN.test(hex)) {
    throw new RangeError(`Invalid hex colour: ${hex}`);
  }

  const red = channel(Number.parseInt(hex.slice(1, 3), 16));
  const green = channel(Number.parseInt(hex.slice(3, 5), 16));
  const blue = channel(Number.parseInt(hex.slice(5, 7), 16));

  return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
}

/** Razão de contraste entre dois `#rrggbb` (de 1 a 21). */
export function contrastRatio(foreground: string, background: string): number {
  const first = relativeLuminance(foreground);
  const second = relativeLuminance(background);

  const lighter = Math.max(first, second);
  const darker = Math.min(first, second);

  return (lighter + 0.05) / (darker + 0.05);
}
