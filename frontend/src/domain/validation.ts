/**
 * Espelho da validação que o backend aplica ao motivo de rejeição.
 *
 * `DocumentService` e `EnrollmentService` recusam com
 * `motivo_required_10_to_500_chars` quando o texto é vazio ou tem menos de 10
 * ou mais de 500 caracteres. As telas só exigiam "não vazio", então o usuário
 * digitava 3 caracteres, o botão liberava e a API devolvia 400 genérico (A10).
 */
export const MOTIVO_MIN_LENGTH = 10;
export const MOTIVO_MAX_LENGTH = 500;

/** Mensagem do que falta, ou `null` quando o valor é aceitável. */
export function motivoValidationError(value: string): string | null {
  if (value.trim() === '') {
    return 'Informe o motivo da rejeição.';
  }
  if (value.length < MOTIVO_MIN_LENGTH) {
    return `O motivo deve ter ao menos ${MOTIVO_MIN_LENGTH} caracteres.`;
  }
  if (value.length > MOTIVO_MAX_LENGTH) {
    return `O motivo deve ter no máximo ${MOTIVO_MAX_LENGTH} caracteres.`;
  }
  return null;
}
