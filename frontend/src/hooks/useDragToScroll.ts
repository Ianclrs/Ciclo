import { useEffect, useRef } from 'react';

/**
 * Rolagem por arrasto com o mouse — o "agarrar e puxar" dos mapas e das galerias.
 *
 * Existe porque a tabela de documentos esconde a barra horizontal: sem ela, um mouse comum
 * perderia a única forma de alcançar as colunas da direita. O arrasto devolve esse acesso sem
 * trazer a barra de volta.
 *
 * Três cuidados que o gesto exige:
 * - **Controles continuam clicando.** O arrasto não começa sobre botão, link ou campo: quem
 *   pressiona "Aprovar" quer aprovar, não puxar a tabela. Sem essa exclusão, o botão dentro da
 *   tabela ficaria inalcançável com o mouse.
 * - **Clique não vira arrasto.** Só a partir de `DRAG_THRESHOLD` px o gesto assume o controle.
 *   Abaixo disso continua sendo clique, e o clique imediatamente após um arrasto é cancelado,
 *   para não acionar o que estava sob o cursor quando o botão foi solto.
 * - **Sem transbordo, nada acontece.** Tabela que cabe na tela não entra em modo de arrasto —
 *   o gesto não é sequestrado à toa.
 *
 * Efeito colateral aceito: enquanto o arrasto está ativo, o texto da tabela não é selecionável
 * (a seleção nativa brigaria com a rolagem). `removeAllRanges` apaga o que o navegador já tinha
 * começado a selecionar nos primeiros pixels do movimento.
 */

/** Distância em pixels que separa um clique de um arrasto. */
const DRAG_THRESHOLD = 4;

/**
 * Elementos que recebem o clique em vez de iniciar o arrasto. Vive fora do hook porque é a
 * lista que garante que os controles da tabela continuem utilizáveis.
 */
const INTERACTIVE = 'button, a, input, select, textarea, label, [role="button"]';

/**
 * Devolve a ref do contêiner que rola. Quando `enabled` é falso, nenhum ouvinte é registrado e
 * o elemento se comporta como uma rolagem comum.
 */
export function useDragToScroll<T extends HTMLElement>(enabled: boolean) {
  const ref = useRef<T>(null);

  useEffect(() => {
    const element = ref.current;
    if (!element || !enabled) return;

    let pressed = false;
    let moved = false;
    let swallowClick = false;
    let startX = 0;
    let startScrollLeft = 0;

    const onMouseDown = (event: MouseEvent) => {
      // Só o botão principal arrasta: o direito abre o menu do sistema.
      if (event.button !== 0) return;

      const target = event.target as HTMLElement | null;
      if (target?.closest(INTERACTIVE)) return;

      if (element.scrollWidth <= element.clientWidth) return;

      pressed = true;
      moved = false;
      swallowClick = false;
      startX = event.clientX;
      startScrollLeft = element.scrollLeft;
    };

    const onMouseMove = (event: MouseEvent) => {
      if (!pressed) return;

      const delta = event.clientX - startX;
      if (!moved && Math.abs(delta) < DRAG_THRESHOLD) return;

      if (!moved) {
        // Primeiro quadro do arrasto: encerra a seleção que o navegador já iniciou e impede
        // que o texto seja selecionado enquanto a tabela desliza.
        window.getSelection()?.removeAllRanges();
        element.style.userSelect = 'none';
      }
      moved = true;

      element.scrollLeft = startScrollLeft - delta;
      event.preventDefault();
    };

    const onMouseUp = () => {
      if (!pressed) return;
      pressed = false;
      element.style.userSelect = '';

      // O `click` é disparado logo depois do `mouseup`: marcar aqui permite cancelá-lo lá.
      if (moved) {
        swallowClick = true;
        moved = false;
      }
    };

    const onClickCapture = (event: MouseEvent) => {
      if (!swallowClick) return;
      swallowClick = false;
      event.preventDefault();
      event.stopPropagation();
    };

    element.addEventListener('mousedown', onMouseDown);
    // `mousemove`/`mouseup` no window: o gesto continua válido quando o cursor sai da tabela.
    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
    element.addEventListener('click', onClickCapture, true);

    return () => {
      element.removeEventListener('mousedown', onMouseDown);
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
      element.removeEventListener('click', onClickCapture, true);
      element.style.userSelect = '';
    };
  }, [enabled]);

  return ref;
}
