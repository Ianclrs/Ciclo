export type BadgeVariant = 'default' | 'success' | 'warning' | 'danger' | 'info';

/** Classes de cor por variante. Vive fora de Badge.tsx para não quebrar o Fast Refresh daquele arquivo. */
export const badgeVariantClass: Record<BadgeVariant, string> = {
  default: 'bg-gray-100 text-gray-700',
  success: 'bg-green-100 text-green-700',
  warning: 'bg-yellow-100 text-yellow-700',
  danger: 'bg-red-100 text-red-700',
  info: 'bg-blue-100 text-blue-700',
};

/**
 * Largura mínima comum das etiquetas de coluna: todas ficam do mesmo tamanho, com o
 * texto centralizado. Rótulos mais longos que a largura mínima crescem em vez de serem cortados.
 */
export const uniformBadgeClass = 'min-w-24 justify-center';
