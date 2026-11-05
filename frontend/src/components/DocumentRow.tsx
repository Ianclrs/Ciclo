import { FileText } from 'lucide-react';
import { StatusBadge } from './StatusBadge';
import { accentChip, type Accent } from './accentStyles';
import { useLocale } from '../i18n/LocaleContext';
import { formatDate } from '../utils/format';
import type { Document } from '../types';

/** Cor do chip do arquivo por status: o mesmo vocabulário da tela administrativa. */
const STATUS_ACCENT: Record<string, Accent> = {
  Pendente: 'amber',
  Aprovado: 'green',
  Rejeitado: 'red',
};

/**
 * Linha de documento do responsável.
 *
 * Vive num componente porque as duas telas que listam documentos do aluno mostram exatamente a
 * mesma informação. A duplicação anterior também repetia um `divide-y` SEM cor: no Tailwind v4
 * a divisória herda `currentColor`, então saía escura, quase preta, entre os itens.
 *
 * Renderiza um `<li>` — deve ser usado dentro de uma lista.
 */
export function DocumentRow({ document }: { document: Document }) {
  const { locale, t } = useLocale();

  return (
    <li className="flex items-start gap-3 rounded-xl px-2 py-2.5 transition-colors hover:bg-white/60">
      <span
        className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ring-1 ring-inset ${accentChip[STATUS_ACCENT[document.status] ?? 'indigo']}`}
      >
        <FileText size={16} aria-hidden="true" />
      </span>

      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-semibold text-gray-900">{document.nomeArquivo}</p>
        <p className="mt-0.5 flex flex-wrap items-center gap-x-1.5 text-xs text-gray-500">
          <span>{document.documentTypeName}</span>
          <span aria-hidden="true" className="text-gray-300">·</span>
          <span>{formatDate(document.createdAt, locale)}</span>
        </p>
        {/* A7: o motivo já vinha no DTO e não era exibido — sem ele o responsável sabe que o
            documento foi recusado, mas não o que corrigir para reenviar. */}
        {document.motivoRejeicao && (
          <p className="mt-1 text-xs text-red-600">
            {t('docs.rejectReason', { reason: document.motivoRejeicao })}
          </p>
        )}
      </div>

      <StatusBadge
        status={document.status}
        variant={
          document.status === 'Aprovado'
            ? 'success'
            : document.status === 'Pendente'
              ? 'warning'
              : 'danger'
        }
      />
    </li>
  );
}
