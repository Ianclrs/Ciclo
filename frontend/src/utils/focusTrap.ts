/**
 * Aritmética pura do ciclo de foco do modal (Spec 190, FR-007 / AD-190-6).
 *
 * O projeto não tem biblioteca de DOM, então a regra de navegação por teclado é uma função
 * pura testável direto, e o modal apenas a aplica sobre os elementos focáveis.
 */

export const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Índice do próximo elemento focável.
 *
 * `length <= 0` devolve `0` (o chamador trata como "nada para focar"); índice fora da faixa é
 * normalizado pelo módulo, inclusive na direção negativa.
 */
export function nextFocusIndex(currentIndex: number, length: number, shift: boolean): number {
  if (length <= 0) {
    return 0;
  }

  const step = shift ? currentIndex - 1 : currentIndex + 1;
  return ((step % length) + length) % length;
}
