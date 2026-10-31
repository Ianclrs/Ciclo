/**
 * Catálogo de texto do produto (Spec 190, FR-002).
 *
 * Dono único do texto de estado (carregando, erro, vazio, ação) em PT, EN e ES. É uma constante
 * do projeto, não uma biblioteca (AD-190-7): o catálogo cobre as onze chaves que a spec nomeia e
 * o teste de paridade recusa qualquer chave ou tradução faltante.
 *
 * `t` recebe o idioma explicitamente (AD-190-1): nenhuma leitura de storage, de rede ou de
 * variável mutável — a mesma chamada produz o mesmo texto em qualquer execução.
 */

import { DEFAULT_LANGUAGE, type Language } from './locale.ts';

export type MessageKey =
  | 'common.loading'
  | 'common.error'
  | 'common.empty'
  | 'common.retry'
  | 'common.reload'
  | 'common.close'
  | 'errorBoundary.title'
  | 'errorBoundary.body'
  | 'field.required'
  | 'list.emptyTitle'
  | 'language.label';

const PT: Record<MessageKey, string> = {
  'common.loading': 'Carregando…',
  'common.error': 'Não foi possível carregar os dados.',
  'common.empty': 'Nada por aqui ainda.',
  'common.retry': 'Tentar novamente',
  'common.reload': 'Recarregar a página',
  'common.close': 'Fechar',
  'errorBoundary.title': 'Algo deu errado.',
  'errorBoundary.body': 'A página encontrou um erro inesperado. Recarregue para continuar.',
  'field.required': '{field} é obrigatório.',
  'list.emptyTitle': 'Nenhum registro encontrado.',
  'language.label': 'Idioma',
};

const EN: Record<MessageKey, string> = {
  'common.loading': 'Loading…',
  'common.error': 'The data could not be loaded.',
  'common.empty': 'Nothing here yet.',
  'common.retry': 'Try again',
  'common.reload': 'Reload the page',
  'common.close': 'Close',
  'errorBoundary.title': 'Something went wrong.',
  'errorBoundary.body': 'The page hit an unexpected error. Reload to continue.',
  'field.required': '{field} is required.',
  'list.emptyTitle': 'No records found.',
  'language.label': 'Language',
};

const ES: Record<MessageKey, string> = {
  'common.loading': 'Cargando…',
  'common.error': 'No fue posible cargar los datos.',
  'common.empty': 'Aún no hay nada aquí.',
  'common.retry': 'Intentar de nuevo',
  'common.reload': 'Recargar la página',
  'common.close': 'Cerrar',
  'errorBoundary.title': 'Algo salió mal.',
  'errorBoundary.body': 'La página encontró un error inesperado. Recargue para continuar.',
  'field.required': '{field} es obligatorio.',
  'list.emptyTitle': 'No se encontraron registros.',
  'language.label': 'Idioma',
};

/** Congelado: uma tela não pode "corrigir" o catálogo em tempo de execução. */
export const MESSAGES: Record<Language, Record<MessageKey, string>> = Object.freeze({
  pt: Object.freeze(PT),
  en: Object.freeze(EN),
  es: Object.freeze(ES),
});

/**
 * Texto da chave no idioma pedido, com substituição de `{name}`.
 *
 * Um placeholder sem parâmetro correspondente é devolvido literalmente: um `{field}` visível é
 * um defeito que o teste pega, enquanto `undefined` na tela passaria despercebido.
 */
export function t(
  key: MessageKey,
  language: Language,
  params?: Record<string, string | number>,
): string {
  const table = MESSAGES[language] ?? MESSAGES[DEFAULT_LANGUAGE];
  const template = table[key] ?? MESSAGES[DEFAULT_LANGUAGE][key];

  return template.replace(/\{(\w+)\}/g, (placeholder, name: string) =>
    params !== undefined && name in params ? String(params[name]) : placeholder,
  );
}
