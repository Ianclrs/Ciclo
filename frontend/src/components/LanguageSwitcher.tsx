import { useLocale } from '../i18n/LocaleContext';
import { LANGUAGE_NAMES, LANGUAGE_ORDER, type Language } from '../i18n/locale';

/** Rótulo curto do controle; o nome completo do idioma fica no `title` e no texto acessível. */
const SHORT_LABEL: Record<Language, string> = { pt: 'PT', en: 'EN', es: 'ES' };

/**
 * Seletor de idioma da interface (Spec 190, FR-001).
 *
 * Antes dele o `LocaleContext` expunha `setLanguage` e nenhuma tela chamava: o idioma só podia
 * ser trocado editando o `localStorage`. Fica na topbar porque é preferência pessoal do usuário,
 * não configuração da escola — o campo equivalente em Configurações define apenas o padrão do
 * colégio, usado quando não existe escolha salva.
 *
 * Três botões em vez de um `select`: a troca é de um clique, o estado atual fica visível sem
 * abrir nada e cada botão declara `aria-pressed`. `lang` marca o idioma do próprio rótulo, para
 * que o leitor de tela pronuncie "Português" em português.
 */
export function LanguageSwitcher() {
  const { language, setLanguage, t } = useLocale();

  return (
    <div
      role="group"
      aria-label={t('language.switchAria')}
      className="flex items-center gap-0.5 rounded-xl border border-stone-300 bg-white/70 p-0.5"
    >
      {LANGUAGE_ORDER.map((option) => {
        const active = option === language;

        return (
          <button
            key={option}
            type="button"
            lang={option}
            onClick={() => setLanguage(option)}
            aria-pressed={active}
            title={LANGUAGE_NAMES[option]}
            className={`rounded-lg px-2 py-1 text-xs font-semibold transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 ${
              active
                ? 'bg-stone-800 text-white'
                : 'text-stone-600 hover:bg-stone-200/80'
            }`}
          >
            {SHORT_LABEL[option]}
          </button>
        );
      })}
    </div>
  );
}
