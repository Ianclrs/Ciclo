import { Badge } from './Badge';
import { uniformBadgeClass, type BadgeVariant } from './badgeStyles';

/**
 * Rótulos de apresentação para valores de status que não são legíveis como chegam da API.
 * O valor continua trafegando como o nome do enum — isto é apenas o que o usuário lê.
 */
const STATUS_LABELS: Record<string, string> = {
  DocumentacaoPendente: 'Documentação',
};

/**
 * Etiqueta de status com largura mínima comum: todos os status ficam do mesmo tamanho,
 * com o texto centralizado, independentemente do comprimento do rótulo.
 */
export function StatusBadge({ status, variant = 'default' }: { status: string; variant?: BadgeVariant }) {
  return (
    <Badge variant={variant} className={uniformBadgeClass}>
      {STATUS_LABELS[status] ?? status}
    </Badge>
  );
}
