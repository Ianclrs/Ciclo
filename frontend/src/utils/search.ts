/**
 * Comparação de texto para busca em tela.
 *
 * Ignora maiúsculas e acentos porque os dados são digitados por pessoas: procurar
 * "jose" precisa encontrar "José", e "matricula" precisa encontrar "Matrícula".
 */
export function matches(haystack: string, needle: string): boolean {
  const term = normalize(needle);
  if (term === '') return true;
  return normalize(haystack).includes(term);
}

function normalize(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .trim();
}
