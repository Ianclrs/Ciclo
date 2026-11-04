import type { MessageKey } from '../i18n/messages';

/**
 * Espelho da validação que o backend aplica ao motivo de rejeição.
 *
 * `DocumentService` e `EnrollmentService` recusam com
 * `motivo_required_10_to_500_chars` quando o texto é vazio ou tem menos de 10
 * ou mais de 500 caracteres. As telas só exigiam "não vazio", então o usuário
 * digitava 3 caracteres, o botão liberava e a API devolvia 400 genérico (A10).
 *
 * O texto do erro vem do catálogo, recebido por parâmetro: o domínio não conhece o idioma ativo
 * e continua testável sem DOM.
 */
export const MOTIVO_MIN_LENGTH = 10;
export const MOTIVO_MAX_LENGTH = 500;

/** Tradutor injetado, no mesmo contrato de `parseGradeInput`. */
export type Translate = (key: MessageKey, params?: Record<string, string | number>) => string;

/** Mensagem do que falta, ou `null` quando o valor é aceitável. */
export function motivoValidationError(value: string, t: Translate): string | null {
  if (value.trim() === '') {
    return t('reason.required');
  }
  if (value.length < MOTIVO_MIN_LENGTH) {
    return t('reason.tooShort', { min: MOTIVO_MIN_LENGTH });
  }
  if (value.length > MOTIVO_MAX_LENGTH) {
    return t('reason.tooLong', { max: MOTIVO_MAX_LENGTH });
  }
  return null;
}
