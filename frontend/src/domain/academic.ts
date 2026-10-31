import type { BadgeVariant } from '../components/badgeStyles';
import type { ReportCardStatus } from '../types';

/**
 * Regras de apresentação da estrutura acadêmica e do boletim (Spec 150).
 *
 * **A autoridade é o backend**: `AcademicService` recusa nota fora de 0 a 10
 * (`grade_out_of_range`) e `GradeCalculation` decide a situação do componente. Este arquivo
 * existe para que a tela recuse o valor antes de enviar e para que o vocabulário de situação
 * (`aprovado`, `recuperacao`, `reprovado`, `sem_nota`) tenha um dono único no frontend — sem ele,
 * cada tela repetiria o mesmo `switch` e uma delas ficaria desatualizada.
 */

export const GRADE_MIN = 0;
export const GRADE_MAX = 10;

const SITUACAO_LABELS: Record<ReportCardStatus, string> = {
  aprovado: 'Aprovado',
  recuperacao: 'Em recuperação',
  reprovado: 'Reprovado',
  sem_nota: 'Sem nota',
};

const SITUACAO_VARIANTS: Record<ReportCardStatus, BadgeVariant> = {
  aprovado: 'success',
  recuperacao: 'warning',
  reprovado: 'danger',
  sem_nota: 'default',
};

function isKnownStatus(status: string): status is ReportCardStatus {
  return status in SITUACAO_LABELS;
}

/** Rótulo em português da situação do componente. */
export function situacaoLabel(status: string): string {
  return isKnownStatus(status) ? SITUACAO_LABELS[status] : 'Sem situação';
}

export function situacaoVariant(status: string): BadgeVariant {
  return isKnownStatus(status) ? SITUACAO_VARIANTS[status] : 'default';
}

/** Resultado da leitura de uma nota digitada: valor válido ou a mensagem a mostrar. */
export type GradeInput =
  | { ok: true; value: number }
  | { ok: false; error: string };

/**
 * Lê a nota digitada e recusa o que a API recusaria.
 *
 * Aceita vírgula porque o teclado numérico em pt-BR produz `7,5` — enviar isso como `7.5` é
 * responsabilidade desta função, não de cada campo da tela.
 */
export function parseGradeInput(raw: string): GradeInput {
  const trimmed = raw.trim();
  if (trimmed === '') {
    return { ok: false, error: 'Informe a nota.' };
  }

  const normalized = trimmed.replace(',', '.');
  // `Number('')` é 0 e `Number('7,5x')` é NaN: a checagem de formato vem antes da faixa.
  const value = Number(normalized);
  if (!Number.isFinite(value)) {
    return { ok: false, error: 'Nota inválida — use números, como 7,5.' };
  }

  if (value < GRADE_MIN || value > GRADE_MAX) {
    return { ok: false, error: `A nota deve estar entre ${GRADE_MIN} e ${GRADE_MAX}.` };
  }

  return { ok: true, value };
}

/** Nota para exibição: vazio vira travessão, e a fração usa a vírgula do pt-BR. */
export function formatGrade(value: number | null): string {
  if (value === null) return '—';
  return value.toLocaleString('pt-BR', { maximumFractionDigits: 2 });
}

/**
 * `DateOnly` chega como `YYYY-MM-DD`. Passar por `new Date()` interpretaria como meia-noite UTC e,
 * em fuso negativo (Brasil, UTC-3), a data exibida cairia para o dia anterior.
 */
export function formatDateOnly(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  if (!match) return value;
  const [, year, month, day] = match;
  return `${day}/${month}/${year}`;
}
