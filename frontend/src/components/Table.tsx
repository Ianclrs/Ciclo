import type { ReactNode } from 'react';

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
}

const SKELETON_ROWS = 5;

export function Table<T>({
  columns,
  data,
  keyExtractor,
  emptyMessage = 'Nenhum resultado encontrado.',
  loading = false,
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
            Array.from({ length: SKELETON_ROWS }, (_, rowIndex) => (
              <tr key={`skeleton-${rowIndex}`} className="border-b border-gray-100 last:border-0">
                {columns.map((_, colIndex) => (
                  <td key={colIndex} className="py-4">
                    <span className="block h-4 w-full max-w-[180px] rounded bg-gray-100 animate-pulse" />
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
