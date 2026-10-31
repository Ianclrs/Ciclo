/**
 * Dono único do locale ativo (Spec 190).
 *
 * Antes desta spec o locale era a constante `LOCALE = 'pt-BR'` dentro de `utils/format.ts`:
 * nenhuma tela podia escolher idioma e nenhuma preferência podia ser expressa. Aqui ficam as
 * três tabelas do design e a regra de resolução (AD-190-2): o país refina a região do próprio
 * idioma, e uma preferência por outro idioma descarta a região do país.
 *
 * Nada aqui depende de React: são funções puras, testáveis com `node --test` sem DOM.
 */

export type Language = 'pt' | 'en' | 'es';

export const LANGUAGE_ORDER: readonly Language[] = ['pt', 'en', 'es'];

export const DEFAULT_LANGUAGE: Language = 'pt';

export const DEFAULT_LOCALE = 'pt-BR';

export const LANGUAGE_STORAGE_KEY = 'dscode.language';

export interface ResolvedLocale {
  language: Language;
  locale: string;
}

export interface LocaleResolutionInput {
  tenantCountry?: string | null;
  userLanguage?: string | null;
}

const COUNTRY_LANGUAGES: Record<string, Language> = { BR: 'pt', PT: 'pt', US: 'en' };

const COUNTRY_LOCALES: Record<string, string> = { BR: 'pt-BR', PT: 'pt-PT', US: 'en-US' };

const LANGUAGE_LOCALES: Record<Language, string> = { pt: 'pt-BR', en: 'en-US', es: 'es-ES' };

/** Predicado único de aceitação de idioma: qualquer outro valor é ignorado pela resolução. */
export function isLanguage(value: unknown): value is Language {
  return typeof value === 'string' && (LANGUAGE_ORDER as readonly string[]).includes(value);
}

/** País → idioma. País ausente ou fora da tabela cai em `BR`/`pt`, como `getCountry()`. */
export function languageForCountry(code: string | null | undefined): Language {
  return COUNTRY_LANGUAGES[code ?? 'BR'] ?? DEFAULT_LANGUAGE;
}

export function defaultLocaleForLanguage(language: Language): string {
  return LANGUAGE_LOCALES[language];
}

/**
 * Regra de resolução (AD-190-2):
 * - preferência igual ao idioma do país ⇒ a região do país vale (`PT` + `pt` = `pt-PT`);
 * - preferência por outro idioma ⇒ a região do país é descartada (`BR` + `en` = `en-US`).
 */
export function resolveLocale(input: LocaleResolutionInput): ResolvedLocale {
  const country = input.tenantCountry ?? 'BR';
  const countryLanguage = languageForCountry(country);
  const preferred = isLanguage(input.userLanguage) ? input.userLanguage : countryLanguage;

  const locale = preferred === countryLanguage
    ? COUNTRY_LOCALES[country] ?? LANGUAGE_LOCALES[preferred]
    : LANGUAGE_LOCALES[preferred];

  return { language: preferred, locale };
}

/**
 * Preferência persistida, ou `null`. Storage bloqueado (modo privado, origem sem permissão)
 * não pode interromper a renderização: qualquer falha vira `null` e o tenant decide.
 */
export function readStoredLanguage(): Language | null {
  try {
    const stored = globalThis.localStorage?.getItem(LANGUAGE_STORAGE_KEY) ?? null;
    return isLanguage(stored) ? stored : null;
  } catch {
    // Sem storage a interface segue com o padrão do tenant — a preferência é conveniência.
    return null;
  }
}

/** Grava a preferência. Falha de escrita é tolerada: a sessão já está no idioma novo. */
export function storeLanguage(language: Language): void {
  try {
    globalThis.localStorage?.setItem(LANGUAGE_STORAGE_KEY, language);
  } catch {
    // Storage bloqueado: a preferência vale para a sessão e não sobrevive ao recarregamento.
  }
}
