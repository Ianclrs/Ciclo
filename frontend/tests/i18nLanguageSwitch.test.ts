import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { LANGUAGE_NAMES, LANGUAGE_ORDER, defaultLocaleForLanguage, languageForLocale } from '../src/i18n/locale.ts';

/**
 * Guarda da troca de idiomas (Spec 190, FR-001).
 *
 * O defeito que originou este teste não era o catálogo — era a ausência de gatilho: o
 * `LocaleContext` expunha `setLanguage` e nenhuma tela chamava, então a interface nunca saía de
 * `pt`. Estas asserções cobrem o caminho completo: locale da API vira idioma, o controle existe
 * e está montado, e a tela de configurações semeia o padrão do colégio.
 */

const HERE = dirname(fileURLToPath(import.meta.url));
const SRC = join(HERE, '..', 'src');

function source(relativePath: string): string {
  return readFileSync(join(SRC, relativePath), 'utf8');
}

test('locale completo vira idioma, inclusive região diferente', () => {
  assert.equal(languageForLocale('pt-BR'), 'pt');
  assert.equal(languageForLocale('pt-PT'), 'pt');
  assert.equal(languageForLocale('en-US'), 'en');
  assert.equal(languageForLocale('es-ES'), 'es');
});

test('locale ausente ou fora da tabela devolve null, nunca um idioma por engano', () => {
  assert.equal(languageForLocale(null), null);
  assert.equal(languageForLocale(undefined), null);
  assert.equal(languageForLocale(''), null);
  assert.equal(languageForLocale('fr-FR'), null);
  assert.equal(languageForLocale('   '), null);
});

test('cada idioma tem nome nativo e o locale dele volta para o mesmo idioma', () => {
  for (const language of LANGUAGE_ORDER) {
    const name = LANGUAGE_NAMES[language];
    assert.equal(typeof name, 'string', `${language} não tem nome de exibição`);
    assert.notEqual(name.trim(), '', `${language} tem nome vazio`);
    assert.equal(
      languageForLocale(defaultLocaleForLanguage(language)),
      language,
      `${language} não sobrevive ao caminho idioma → locale → idioma`,
    );
  }

  const names = LANGUAGE_ORDER.map((language) => LANGUAGE_NAMES[language]);
  assert.equal(new Set(names).size, names.length, 'dois idiomas compartilham o mesmo nome');
});

test('o seletor troca o idioma e expõe o estado atual', () => {
  const switcher = source('components/LanguageSwitcher.tsx');

  assert.match(switcher, /setLanguage\(option\)/, 'o seletor não troca o idioma');
  assert.match(switcher, /aria-pressed=\{active\}/, 'o seletor não expõe o idioma ativo');
  assert.match(switcher, /LANGUAGE_ORDER\.map/, 'o seletor não deriva as opções do dono único');
});

test('o seletor está montado na topbar, não só definido', () => {
  const topbar = source('components/TopbarActions.tsx');

  assert.match(topbar, /import \{ LanguageSwitcher \}/, 'a topbar não importa o seletor');
  assert.match(topbar, /<LanguageSwitcher \/>/, 'a topbar não renderiza o seletor');
});

test('o menu lateral lê o catálogo em vez de guardar texto fixo', () => {
  const sidebar = source('components/Sidebar.tsx');
  const app = source('App.tsx');

  assert.match(sidebar, /t\(item\.labelKey\)/, 'a sidebar não traduz o rótulo do item');
  assert.doesNotMatch(app, /label: '[A-Za-zÀ-ÿ]/, 'o menu voltou a guardar texto literal');
});

test('o idioma da página acompanha o da interface', () => {
  const context = source('i18n/LocaleContext.tsx');

  assert.match(
    context,
    /document\.documentElement\.lang = resolved\.locale/,
    'o <html lang> ficou preso ao idioma inicial',
  );
});

test('a tela de configurações aplica o idioma da escola e troca ao escolher', () => {
  const page = source('pages/admin/TenantSettings.tsx');

  assert.match(page, /readStoredLanguage\(\)/, 'a tela não verifica se já existe escolha salva');
  assert.match(page, /languageForLocale\(/, 'a tela ignora o locale da escola');
  assert.match(page, /setLanguage\(nextLanguage\)/, 'o campo não troca o idioma da interface');
  assert.match(
    page,
    /applySchoolLocale\(loaded\.locale\)/,
    'a tela não semeia o idioma na carga da configuração',
  );
  assert.match(
    page,
    /applySchoolLocale\(event\.target\.value\)/,
    'escolher outro idioma no campo não troca a interface',
  );
});
