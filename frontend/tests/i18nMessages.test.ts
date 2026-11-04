import test from 'node:test';
import assert from 'node:assert/strict';
import { MESSAGES, t, type MessageKey } from '../src/i18n/messages.ts';
import { LANGUAGE_ORDER } from '../src/i18n/locale.ts';
import { ERROR_MESSAGES } from '../src/api/errorMessages.ts';
import { codeMessage } from '../src/api/errors.ts';

/**
 * As onze chaves que a Spec 190 declarou. Continuam obrigatórias: o catálogo cresceu para o
 * shell e a tela de configurações, mas nada que a spec exigia pode desaparecer.
 */
const LEGACY_KEYS: MessageKey[] = [
  'common.loading',
  'common.error',
  'common.empty',
  'common.retry',
  'common.reload',
  'common.close',
  'errorBoundary.title',
  'errorBoundary.body',
  'field.required',
  'list.emptyTitle',
  'language.label',
];

/** Chaves do catálogo em uso. `pt` é a referência: os outros dois têm de bater com ela. */
function messageKeys(): MessageKey[] {
  return Object.keys(MESSAGES.pt) as MessageKey[];
}

function placeholders(value: string): string[] {
  return [...value.matchAll(/\{(\w+)\}/g)].map((match) => match[1]).sort();
}

test('o catálogo mantém as onze chaves originais da spec', () => {
  for (const language of LANGUAGE_ORDER) {
    for (const key of LEGACY_KEYS) {
      assert.ok(key in MESSAGES[language], `${language} perdeu a chave ${key}`);
    }
  }
});

test('os três idiomas declaram exatamente o mesmo conjunto de chaves', () => {
  const reference = messageKeys().sort();

  for (const language of LANGUAGE_ORDER) {
    assert.deepEqual(
      Object.keys(MESSAGES[language]).sort(),
      reference,
      `chaves divergentes em ${language}`,
    );
  }
});

test('nenhum valor é vazio ou só espaço', () => {
  for (const language of LANGUAGE_ORDER) {
    for (const key of messageKeys()) {
      const value = MESSAGES[language][key];
      assert.equal(typeof value, 'string', `${language}/${key} não é texto`);
      assert.notEqual(value.trim(), '', `${language}/${key} está vazio`);
    }
  }
});

test('os placeholders de cada chave são idênticos nos três idiomas', () => {
  for (const key of messageKeys()) {
    const reference = placeholders(MESSAGES.pt[key]);
    for (const language of LANGUAGE_ORDER) {
      assert.deepEqual(
        placeholders(MESSAGES[language][key]),
        reference,
        `${language}/${key} tem placeholders diferentes`,
      );
    }
  }
});

test('o catálogo é congelado: uma tela não pode reescrever o texto', () => {
  assert.equal(Object.isFrozen(MESSAGES), true);
  assert.equal(Object.isFrozen(MESSAGES.pt), true);
  assert.equal(Object.isFrozen(MESSAGES.en), true);
  assert.equal(Object.isFrozen(MESSAGES.es), true);
});

test('t substitui os placeholders informados', () => {
  assert.equal(t('field.required', 'pt', { field: 'E-mail' }), 'E-mail é obrigatório.');
  assert.equal(t('field.required', 'en', { field: 'E-mail' }), 'E-mail is required.');
  assert.equal(t('field.required', 'es', { field: 'Correo' }), 'Correo es obligatorio.');
});

test('placeholder sem parâmetro permanece literal, nunca vira undefined', () => {
  assert.equal(t('field.required', 'pt'), '{field} é obrigatório.');
  assert.equal(t('field.required', 'en', { outro: 'x' }), '{field} is required.');
});

test('t devolve o texto do idioma pedido', () => {
  assert.equal(t('common.retry', 'pt'), 'Tentar novamente');
  assert.equal(t('common.retry', 'en'), 'Try again');
  assert.equal(t('common.retry', 'es'), 'Intentar de nuevo');
});

test('idioma inválido em runtime cai no padrão em vez de devolver undefined', () => {
  const malformed = 'fr' as unknown as 'pt';
  assert.equal(t('common.close', malformed), MESSAGES.pt['common.close']);
});

test('todo código de erro tem as três traduções, nenhuma vazia', () => {
  const codes = Object.keys(ERROR_MESSAGES);
  assert.ok(codes.length > 0, 'o mapa de erros está vazio');

  for (const code of codes) {
    for (const language of LANGUAGE_ORDER) {
      const value = ERROR_MESSAGES[code][language];
      assert.equal(typeof value, 'string', `${code} não tem texto em ${language}`);
      assert.notEqual(value.trim(), '', `${code} está vazio em ${language}`);
    }
  }
});

test('nenhuma tradução devolve o identificador cru', () => {
  for (const [code, translations] of Object.entries(ERROR_MESSAGES)) {
    for (const language of LANGUAGE_ORDER) {
      assert.notEqual(translations[language], code, `${code}/${language} devolve o código`);
    }
  }
});

test('codeMessage resolve cada código nos três idiomas', () => {
  for (const [code, translations] of Object.entries(ERROR_MESSAGES)) {
    for (const language of LANGUAGE_ORDER) {
      assert.equal(
        codeMessage(code, 'fallback', language),
        translations[language],
        `${code} em ${language}`,
      );
    }
  }
});
