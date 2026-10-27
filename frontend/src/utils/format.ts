const LOCALE = 'pt-BR';

/**
 * Formatação de data única do app (A10). Antes cada tela chamava
 * `toLocaleDateString()` sem locale e o resultado seguia o idioma do
 * navegador — a mesma data aparecia diferente entre telas e máquinas.
 */
export function formatDate(value: string | Date): string {
  return new Date(value).toLocaleDateString(LOCALE);
}

export function formatDateTime(value: string | Date): string {
  return new Date(value).toLocaleString(LOCALE);
}
