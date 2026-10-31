import test from 'node:test';
import assert from 'node:assert/strict';
import {
  DEFAULT_LANGUAGE,
  DEFAULT_LOCALE,
  LANGUAGE_ORDER,
  LANGUAGE_STORAGE_KEY,
  defaultLocaleForLanguage,
  isLanguage,
  languageForCountry,
  readStoredLanguage,
  resolveLocale,
  storeLanguage,
} from '../src/i18n/locale.ts';

/** Substitui o storage global pelo dublê do teste (Node não tem `localStorage`). */
function stubStorage(storage: Record<string, unknown> | undefined) {
  (globalThis as Record<string, unknown>).localStorage = storage;
}

test('as constantes do módulo são as declaradas no design', () => {
  assert.deepEqual([...LANGUAGE_ORDER], ['pt', 'en', 'es']);
  assert.equal(DEFAULT_LANGUAGE, 'pt');
  assert.equal(DEFAULT_LOCALE, 'pt-BR');
  assert.equal(LANGUAGE_STORAGE_KEY, 'dscode.language');
});

test('languageForCountry cobre BR, PT, US e cai em pt no resto', () => {
  assert.equal(languageForCountry('BR'), 'pt');
  assert.equal(languageForCountry('PT'), 'pt');
  assert.equal(languageForCountry('US'), 'en');
  assert.equal(languageForCountry('FR'), 'pt');
  assert.equal(languageForCountry(''), 'pt');
  assert.equal(languageForCountry(null), 'pt');
  assert.equal(languageForCountry(undefined), 'pt');
});

test('defaultLocaleForLanguage devolve a região padrão de cada idioma', () => {
  assert.equal(defaultLocaleForLanguage('pt'), 'pt-BR');
  assert.equal(defaultLocaleForLanguage('en'), 'en-US');
  assert.equal(defaultLocaleForLanguage('es'), 'es-ES');
});

test('resolveLocale reproduz a tabela de resolução inteira', () => {
  const table: Array<[string | null, string | null, string, string]> = [
    ['BR', null, 'pt', 'pt-BR'],
    ['BR', 'pt', 'pt', 'pt-BR'],
    ['BR', 'en', 'en', 'en-US'],
    ['BR', 'es', 'es', 'es-ES'],
    ['PT', null, 'pt', 'pt-PT'],
    ['PT', 'pt', 'pt', 'pt-PT'],
    ['PT', 'en', 'en', 'en-US'],
    ['US', null, 'en', 'en-US'],
    ['US', 'pt', 'pt', 'pt-BR'],
    ['FR', 'es', 'es', 'es-ES'],
    ['', null, 'pt', 'pt-BR'],
    [null, null, 'pt', 'pt-BR'],
  ];

  for (const [tenantCountry, userLanguage, language, locale] of table) {
    assert.deepEqual(
      resolveLocale({ tenantCountry, userLanguage }),
      { language, locale },
      `país=${String(tenantCountry)} preferência=${String(userLanguage)}`,
    );
  }
});

test('preferência não suportada é ignorada e o país decide', () => {
  assert.deepEqual(resolveLocale({ tenantCountry: 'PT', userLanguage: 'fr' }), {
    language: 'pt',
    locale: 'pt-PT',
  });
  assert.deepEqual(resolveLocale({ tenantCountry: 'US', userLanguage: '' }), {
    language: 'en',
    locale: 'en-US',
  });
  assert.deepEqual(resolveLocale({ tenantCountry: 'BR', userLanguage: null }), {
    language: 'pt',
    locale: 'pt-BR',
  });
});

test('isLanguage aceita só os três idiomas', () => {
  for (const accepted of ['pt', 'en', 'es']) {
    assert.equal(isLanguage(accepted), true);
  }
  for (const rejected of ['fr', '', 'PT', null, undefined, 0, {}, []]) {
    assert.equal(isLanguage(rejected), false, `deveria rejeitar ${JSON.stringify(rejected)}`);
  }
});

test('readStoredLanguage devolve a preferência gravada', () => {
  stubStorage({ getItem: (key: string) => (key === LANGUAGE_STORAGE_KEY ? 'es' : null) });
  assert.equal(readStoredLanguage(), 'es');
});

test('readStoredLanguage devolve null para valor não suportado', () => {
  stubStorage({ getItem: () => 'fr' });
  assert.equal(readStoredLanguage(), null);
});

test('readStoredLanguage devolve null quando o storage lança', () => {
  stubStorage({
    getItem: () => {
      throw new Error('SecurityError');
    },
  });
  assert.equal(readStoredLanguage(), null);
});

test('readStoredLanguage devolve null quando não existe storage', () => {
  stubStorage(undefined);
  assert.equal(readStoredLanguage(), null);
});

test('storeLanguage grava na chave declarada', () => {
  const written: Array<[string, string]> = [];
  stubStorage({ setItem: (key: string, value: string) => written.push([key, value]) });

  storeLanguage('en');

  assert.deepEqual(written, [[LANGUAGE_STORAGE_KEY, 'en']]);
});

test('storeLanguage não lança quando a escrita falha', () => {
  stubStorage({
    setItem: () => {
      throw new Error('QuotaExceededError');
    },
  });

  assert.doesNotThrow(() => storeLanguage('es'));
});
