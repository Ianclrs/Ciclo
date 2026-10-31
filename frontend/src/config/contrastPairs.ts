/**
 * Pares de texto e fundo declarados pela interface (Spec 190, FR-008 / AD-190-8).
 *
 * Os valores são hex literais, não nomes de classe: a asserção de contraste não pode depender
 * de como o CSS compila um utilitário. Cada par corresponde a uma combinação real usada nas
 * telas — corpo do texto sobre a página, título sobre o card, texto de perigo sobre a faixa
 * de perigo e texto de erro sobre o card.
 */

export interface ContrastPair {
  name: string;
  foreground: string;
  background: string;
}

export const TEXT_CONTRAST_PAIRS: readonly ContrastPair[] = [
  { name: 'body-on-page', foreground: '#374151', background: '#FFFFFF' },
  { name: 'heading-on-card', foreground: '#111827', background: '#FFFFFF' },
  { name: 'danger-on-danger-surface', foreground: '#991B1B', background: '#FEF2F2' },
  { name: 'error-text-on-card', foreground: '#DC2626', background: '#FFFFFF' },
];
