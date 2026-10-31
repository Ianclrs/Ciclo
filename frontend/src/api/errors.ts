import axios from 'axios';
import { ERROR_MESSAGES } from './errorMessages.ts';
import { DEFAULT_LANGUAGE, type Language } from '../i18n/locale.ts';

/**
 * Dono único da extração e da exibição do código de erro da API (Spec 190, FR-004).
 *
 * O backend devolve o código em `title` (ProblemDetails) ou em `error` (contratos mais
 * antigos). Antes desta spec cada tela repetia o próprio `switch` sobre `title` e caía num
 * texto genérico ("Erro.") quando o código não estava mapeado — o usuário não sabia o que
 * corrigir (A8).
 *
 * O mapa em si vive em `errorMessages.ts`, com as três traduções por código (AD-190-4). Aqui
 * ficam apenas a extração do código e as regras de exibição, que não dependem do idioma.
 */

/** Código de erro devolvido pela API, se houver. */
export function apiErrorCode(error: unknown): string | null {
  if (!axios.isAxiosError(error)) {
    return null;
  }
  // `code` é o contrato (extensão do ProblemDetails, M3). `title` e `error` continuam aceitos
  // porque respostas antigas e o Identity ainda usam esses campos — a ordem é do mais novo ao
  // mais legado, para que a transição não quebre nenhuma tela.
  const data = error.response?.data as { code?: unknown; title?: unknown; error?: unknown } | undefined;
  if (typeof data?.code === 'string' && data.code.trim() !== '') {
    return data.code;
  }
  if (typeof data?.title === 'string' && data.title.trim() !== '') {
    return data.title;
  }
  if (typeof data?.error === 'string' && data.error.trim() !== '') {
    return data.error;
  }
  return null;
}

/**
 * Mensagem para o usuário. Código conhecido → texto no idioma pedido; título já legível da API
 * (ex.: falha de validação) → ele mesmo; caso contrário, o texto de apoio da tela. Nunca
 * devolve o código cru ao usuário.
 *
 * O idioma é opcional e cai em `pt`, então a aridade anterior continua válida (NFR-002).
 */
export function codeMessage(
  code: string | null,
  fallback: string,
  language: Language = DEFAULT_LANGUAGE,
): string {
  if (code === null) {
    return fallback;
  }

  const entry = ERROR_MESSAGES[code];
  // Código sem tradução no idioma pedido usa o `pt` do mesmo código: uma lacuna de tradução
  // não pode esconder a mensagem nem vazar o identificador.
  const known = entry?.[language] ?? entry?.[DEFAULT_LANGUAGE];
  if (known !== undefined && known !== '') {
    return known;
  }

  // ProblemDetails com frase legível já é apresentável; identificador não é.
  return code.includes(' ') ? code : fallback;
}

export function apiErrorMessage(
  error: unknown,
  fallback: string,
  language: Language = DEFAULT_LANGUAGE,
): string {
  return codeMessage(apiErrorCode(error), fallback, language);
}
