import type { ReactNode } from 'react';
import { useLocale } from '../i18n/LocaleContext';

/**
 * Terceiro estado de tela (Spec 190, FR-005): resultado vazio.
 *
 * Antes cada listagem inventava o próprio texto vazio — ou não tinha nenhum e mostrava uma
 * tabela sem linhas. O texto vem do catálogo no idioma ativo; `title`/`message` existem para o
 * caso em que a tela precisa ser mais específica que o padrão.
 *
 * `role="status"` faz o resultado vazio ser anunciado por leitor de tela, em vez de parecer
 * uma tela quebrada.
 */
export function EmptyState({
  title,
  message,
  action,
}: {
  title?: string;
  message?: string;
  action?: ReactNode;
}) {
  const { t } = useLocale();

  return (
    <div role="status" className="flex flex-col items-center gap-2 py-10 text-center">
      <h3 className="text-sm font-semibold text-gray-800">{title ?? t('list.emptyTitle')}</h3>
      <p className="max-w-md text-sm text-gray-600">{message ?? t('common.empty')}</p>
      {action}
    </div>
  );
}
