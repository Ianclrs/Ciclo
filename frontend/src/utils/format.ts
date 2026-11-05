/**
 * Dono único da formatação de data, hora, número, moeda e percentual (Spec 190, FR-003).
 *
 * Antes existia só `LOCALE = 'pt-BR'` fixo aqui: a mesma data aparecia igual em toda a
 * aplicação, mas nenhuma tela podia escolher o formato do colégio, e não havia formatação de
 * número/moeda compartilhada. O locale agora é argumento explícito (AD-190-1) e o padrão é
 * exatamente o de antes, então todo chamador atual continua com a mesma saída (AD-190-5).
 */

export const DEFAULT_LOCALE = 'pt-BR';

/** Data sem hora. Entrada inválida devolve string vazia, nunca `Invalid Date` na tela. */
export function formatDate(value: string | Date, locale: string = DEFAULT_LOCALE): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  return new Intl.DateTimeFormat(locale ?? DEFAULT_LOCALE, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(date);
}

/**
 * Data com hora e segundos.
 *
 * Os segundos estão nas opções de propósito: a saída anterior (padrão do `toLocaleString`)
 * inclui segundos, e a spec exige que a chamada sem locale não mude de formato (NFR-002).
 */
export function formatDateTime(value: string | Date, locale: string = DEFAULT_LOCALE): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  return new Intl.DateTimeFormat(locale ?? DEFAULT_LOCALE, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(date);
}

/**
 * Só a hora (HH:mm) no locale pedido.
 *
 * Existe porque o painel mostrava `toLocaleTimeString('pt-BR')` direto na tela — o último
 * literal de locale em `src/pages/` que a spec 190 elimina (NFR-001).
 */
export function formatTime(value: string | Date, locale: string = DEFAULT_LOCALE): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  return new Intl.DateTimeFormat(locale ?? DEFAULT_LOCALE, {
    hour: '2-digit',
    minute: '2-digit',
  }).format(date);
}

export function formatNumber(
  value: number,
  locale: string = DEFAULT_LOCALE,
  options?: Intl.NumberFormatOptions,
): string {
  if (Number.isNaN(value)) {
    return '';
  }
  return new Intl.NumberFormat(locale ?? DEFAULT_LOCALE, options).format(value);
}

export function formatCurrency(
  value: number,
  currency: string,
  locale: string = DEFAULT_LOCALE,
): string {
  return formatNumber(value, locale, { style: 'currency', currency });
}

export function formatPercent(
  value: number,
  locale: string = DEFAULT_LOCALE,
  options?: Intl.NumberFormatOptions,
): string {
  return formatNumber(value, locale, { style: 'percent', ...options });
}

/**
 * Fração parte/total, com 0 quando não há base.
 *
 * Existe porque os cartões de indicador recebem uma fração de 0 a 1 para a barra de
 * proporção, e a divisão por zero produziria `NaN`/`Infinity` — a barra sairia cheia quando
 * a base é vazia, afirmando um progresso que não existe.
 */
export function share(part: number, total: number): number {
  return total > 0 ? part / total : 0;
}
