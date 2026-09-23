interface PaginationProps {
  page: number;
  pageSize: number;
  total: number;
  onPageChange: (page: number) => void;
}

/** Páginas exibidas: primeira, última, atual e vizinhas. `null` representa as reticências. */
function visiblePages(current: number, totalPages: number): (number | null)[] {
  const wanted = [1, current - 1, current, current + 1, totalPages];
  const pages = [...new Set(wanted)].filter((p) => p >= 1 && p <= totalPages).sort((a, b) => a - b);

  const result: (number | null)[] = [];
  let previous = 0;
  for (const p of pages) {
    if (previous !== 0 && p - previous > 1) result.push(null);
    result.push(p);
    previous = p;
  }
  return result;
}

const navButton =
  'px-3 py-1.5 text-sm rounded-lg border border-gray-300 text-gray-700 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors';

export function Pagination({ page, pageSize, total, onPageChange }: PaginationProps) {
  if (total === 0) return null;

  const totalPages = Math.ceil(total / pageSize);
  const firstItem = (page - 1) * pageSize + 1;
  const lastItem = Math.min(page * pageSize, total);

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 mt-5 pt-4 border-t border-gray-100">
      <p className="text-sm text-gray-500">
        Mostrando <span className="font-medium text-gray-700">{firstItem}–{lastItem}</span> de{' '}
        <span className="font-medium text-gray-700">{total}</span>
      </p>

      {totalPages > 1 && (
        <div className="flex items-center gap-1">
          <button type="button" onClick={() => onPageChange(page - 1)} disabled={page <= 1} className={navButton}>
            Anterior
          </button>

          {visiblePages(page, totalPages).map((p, index) =>
            p === null ? (
              <span key={`gap-${index}`} className="px-2 text-sm text-gray-400">
                …
              </span>
            ) : (
              <button
                key={p}
                type="button"
                onClick={() => onPageChange(p)}
                aria-current={p === page ? 'page' : undefined}
                className={`px-3 py-1.5 text-sm rounded-lg border transition-colors ${
                  p === page
                    ? 'bg-indigo-600 text-white border-indigo-600'
                    : 'border-gray-300 text-gray-700 hover:bg-gray-50'
                }`}
              >
                {p}
              </button>
            ),
          )}

          <button
            type="button"
            onClick={() => onPageChange(page + 1)}
            disabled={page >= totalPages}
            className={navButton}
          >
            Próximo
          </button>
        </div>
      )}
    </div>
  );
}
