export type Accent = 'violet' | 'blue' | 'green' | 'amber' | 'red';

/**
 * Chip do ícone: pastel com anel interno. O anel existe porque um `*-100` liso sobre o vidro
 * translúcido perde o contorno — sem ele o ícone parece flutuar sem caixa.
 */
export const accentChip: Record<Accent, string> = {
  violet: 'bg-violet-100 text-violet-700 ring-violet-200',
  blue: 'bg-blue-100 text-blue-700 ring-blue-200',
  green: 'bg-green-100 text-green-700 ring-green-200',
  amber: 'bg-amber-100 text-amber-700 ring-amber-200',
  red: 'bg-red-100 text-red-700 ring-red-200',
};

/** Barra de proporção, na mesma família de cor do chip. */
export const accentBar: Record<Accent, string> = {
  violet: 'bg-violet-500',
  blue: 'bg-blue-500',
  green: 'bg-green-500',
  amber: 'bg-amber-500',
  red: 'bg-red-500',
};
