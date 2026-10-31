import type { ReactNode } from 'react';
import { Skeleton } from './Skeleton';

interface Column<T> {
  header: string;
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
}

const DEFAULT_LOADING_ROWS = 5;

export function Table<T>({
  columns,
  data,
  keyExtractor,
  emptyMessage = 'Nenhum resultado encontrado.',
  loading = false,
  loadingRows = DEFAULT_LOADING_ROWS,
}: TableProps<T>) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-gray-200">
            {columns.map((col, index) => (
              <th
                // Índice como chave: colunas podem ter cabeçalho vazio (ex.: coluna de ações).
                key={index}
                scope="col"
                className={`pb-3 text-xs font-semibold uppercase tracking-wide text-gray-500 ${col.className || ''}`}
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
                {emptyMessage}
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
