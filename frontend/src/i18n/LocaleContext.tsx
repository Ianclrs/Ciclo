import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { readStoredLanguage, resolveLocale, storeLanguage, type Language } from './locale.ts';
import { t as translate, type MessageKey } from './messages.ts';

export interface LocaleContextValue {
  language: Language;
  locale: string;
  t: (key: MessageKey, params?: Record<string, string | number>) => string;
  setLanguage: (language: Language) => void;
}

const LocaleContext = createContext<LocaleContextValue | null>(null);

/**
 * Fonte do locale ativo para uma árvore de renderização (Spec 190, FR-001).
 *
 * É o ÚNICO lugar com estado ambiente de idioma: todo módulo puro (catálogo, formatadores,
 * mapa de erros) recebe `language`/`locale` como argumento (AD-190-1), então continua testável
 * sem DOM.
 */
export function LocaleProvider({ country = null, children }: { country?: string | null; children: ReactNode }) {
  const [preference, setPreference] = useState<Language | null>(readStoredLanguage);

  const resolved = useMemo(
    () => resolveLocale({ tenantCountry: country, userLanguage: preference }),
    [country, preference],
  );

  const value = useMemo<LocaleContextValue>(() => ({
    language: resolved.language,
    locale: resolved.locale,
    t: (key, params) => translate(key, resolved.language, params),
    setLanguage: (language) => {
      // Estado primeiro, storage depois: uma escrita bloqueada não pode deixar a interface no
      // idioma anterior (FR-001, modo de falha).
      setPreference(language);
      storeLanguage(language);
    },
  }), [resolved]);

  return <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>;
}

export function useLocale(): LocaleContextValue {
  const context = useContext(LocaleContext);
  if (context === null) {
    throw new Error('useLocale must be used within LocaleProvider');
  }
  return context;
}
