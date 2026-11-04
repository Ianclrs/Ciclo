import { Badge } from './Badge';
import { uniformBadgeClass, type BadgeVariant } from './badgeStyles';
import { useLocale } from '../i18n/LocaleContext';
import { statusLabelKey } from '../domain/statusLabels';

/**
 * Etiqueta de status com largura mínima comum: todos os status ficam do mesmo tamanho,
 * com o texto centralizado, independentemente do comprimento do rótulo.
 *
 * O vocabulário (valor da API → chave do catálogo) mora em `domain/statusLabels.ts`: esta
 * etiqueta aparece em documentos, matrículas e alunos, e um mapa local por tela divergiria.
 */
export function StatusBadge({ status, variant = 'default' }: { status: string; variant?: BadgeVariant }) {
  const { t } = useLocale();
  const labelKey = statusLabelKey(status);

  return (
    <Badge variant={variant} className={uniformBadgeClass}>
      {labelKey ? t(labelKey) : status}
    </Badge>
  );
}
