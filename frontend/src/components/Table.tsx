import type { ReactNode } from 'react';
import { Skeleton } from './Skeleton';
import { useLocale } from '../i18n/LocaleContext';
import { useDragToScroll } from '../hooks/useDragToScroll';

interface Column<T> {
  /**
   * Texto do cabeçalho. Aceita nó React porque uma coluna pode carregar um controle: a tabela
   * de rematrícula em lote põe aqui o "selecionar todos os elegíveis".
   */
  header: ReactNode;
  accessor: (row: T) => ReactNode;
  className?: string;
}

interface TableProps<T> {
  columns: Column<T>[];
  data: T[];
  keyExtractor: (row: T) => string;
  emptyMessage?: string;
  /** Mantém o cabeçalho visível e mostra linhas de esqueleto no lugar dos dados. */
  loading?: boolean;
  /**
   * Quantas linhas de esqueleto desenhar. Deve ser o tamanho da página: com um número menor,
   * a tabela carrega curta e cresce de uma vez quando os dados chegam — medido como pulo de
   * altura visível na troca de seção.
   */
  loadingRows?: number;
  /**
   * Esconde a barra de rolagem horizontal sem desligar a rolagem.
   *
   * A rolagem continua por roda/gesto, Shift+roda e teclado — o que sai é a barra nativa, que
   * sobre o vidro do card pesa no desenho. Fica como opção da página, e não fixo na tabela,
   * porque é ela que sabe se o conteúdo cabe: escondida a barra, um mouse sem roda horizontal
   * perde a única pista de que há colunas à direita.
   */
  hideScrollbar?: boolean;
  /**
   * Permite arrastar a tabela com o mouse para alcançar o que não cabe na tela.
   *
   * É o par do `hideScrollbar`: sem a barra, o arrasto é o caminho que resta para um mouse
   * comum. Fica como opção separada porque são decisões diferentes — dá para esconder a barra
   * sem habilitar o arrasto, e dá para arrastar sem esconder a barra.
   */
  dragToScroll?: boolean;
}

const DEFAULT_LOADING_ROWS = 5;

/**
 * Uma coluna que já declarou o próprio alinhamento mantém a decisão dela.
 *
 * `text-left` e `text-center` são a MESMA propriedade CSS: emitir as duas faz a ordem da folha
 * de estilo decidir o vencedor, e não a coluna. Medido no app: com as duas classes no `th`,
 * `text-left` ganhava e as colunas centradas ficavam com o rótulo à esquerda de um conteúdo
 * centralizado — um desalinhamento novo no lugar do que se queria corrigir.
 */
const DECLARED_ALIGNMENT = /(^|\s)text-(left|center|right|start|end|justify)(\s|$)/;

/**
 * Aparência do arrasto. Os controles da tabela recuperam o cursor próprio: sem isso, o botão
 * de aprovar mostraria a mãozinha de arrasto e pareceria parte da área de rolagem.
 */
const GRAB_CURSOR =
  'cursor-grab active:cursor-grabbing [&_a]:cursor-pointer [&_button]:cursor-pointer [&_select]:cursor-pointer [&_input]:cursor-text';

export function Table<T>({
  columns,
  data,
  keyExtractor,
  emptyMessage,
  loading = false,
  loadingRows = DEFAULT_LOADING_ROWS,
  hideScrollbar = false,
  dragToScroll = false,
}: TableProps<T>) {
  const { t } = useLocale();
  const empty = emptyMessage ?? t('list.emptyTitle');
  const scrollRef = useDragToScroll<HTMLDivElement>(dragToScroll);

  const containerClass = [
    'overflow-x-auto',
    hideScrollbar ? 'no-scrollbar' : '',
    dragToScroll ? GRAB_CURSOR : '',
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <div ref={scrollRef} className={containerClass}>
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-gray-200">
            {columns.map((col, index) => (
              <th
                // Índice como chave: colunas podem ter cabeçalho vazio (ex.: coluna de ações).
                key={index}
                scope="col"
                /**
                 * O `th` centraliza por padrão no navegador — o `td` não. Sem decisão
                 * explícita, a coluna "Arquivo" ficava com o rótulo no MEIO e o conteúdo à
                 * ESQUERDA: medido no app, 100 px entre o rótulo e o ícone que ele nomeia. A
                 * classe entra só quando a coluna NÃO declarou alinhamento próprio, porque
                 * `text-left` e `text-center` são a mesma propriedade e emitir as duas faz a
                 * folha de estilo decidir, não a coluna.
                 *
                 * `whitespace-nowrap` impede o rótulo de quebrar em duas linhas quando a
                 * janela encolhe: em metade da tela "Tipo de documento" virava duas linhas e
                 * a linha de cabeçalho ficava mais alta que as de dados.
                 */
                className={`whitespace-nowrap pb-3 text-xs font-semibold uppercase tracking-wide text-gray-500 ${
                  DECLARED_ALIGNMENT.test(col.className ?? '') ? '' : 'text-left'
                } ${col.className || ''}`}
              >
                {col.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {loading &&
            Array.from({ length: loadingRows }, (_, rowIndex) => (
              <tr key={`skeleton-${rowIndex}`} className="border-b border-gray-100 last:border-0">
                {columns.map((_, colIndex) => (
                  <td key={colIndex} className="py-4">
                    <Skeleton className="h-4 w-full max-w-[180px]" />
                  </td>
                ))}
              </tr>
            ))}

          {!loading &&
            data.map((row) => (
              <tr
                key={keyExtractor(row)}
                className="border-b border-gray-100 last:border-0 hover:bg-gray-50 transition-colors"
              >
                {columns.map((col, index) => (
                  <td key={index} className={`py-4 align-middle ${col.className || ''}`}>
                    {col.accessor(row)}
                  </td>
                ))}
              </tr>
            ))}

          {!loading && data.length === 0 && (
            <tr>
              <td colSpan={columns.length} className="py-12 text-center text-gray-500">
                {empty}
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
