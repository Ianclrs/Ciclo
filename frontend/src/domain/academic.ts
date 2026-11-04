import type { BadgeVariant } from '../components/badgeStyles';
import { DEFAULT_LOCALE } from '../i18n/locale.ts';
import type { MessageKey } from '../i18n/messages';
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

const SITUACAO_LABEL_KEYS: Record<ReportCardStatus, MessageKey> = {
  aprovado: 'academic.status.approved',
  recuperacao: 'academic.status.recovery',
  reprovado: 'academic.status.failed',
  sem_nota: 'academic.status.noGrade',
};

const SITUACAO_VARIANTS: Record<ReportCardStatus, BadgeVariant> = {
  aprovado: 'success',
  recuperacao: 'warning',
  reprovado: 'danger',
  sem_nota: 'default',
};

function isKnownStatus(status: string): status is ReportCardStatus {
  return status in SITUACAO_LABEL_KEYS;
}

/** Chave do rótulo da situação; o texto mora no catálogo, então a tela acompanha o idioma. */
export function situacaoLabelKey(status: string): MessageKey {
  return isKnownStatus(status) ? SITUACAO_LABEL_KEYS[status] : 'academic.status.none';
}

export function situacaoVariant(status: string): BadgeVariant {
  return isKnownStatus(status) ? SITUACAO_VARIANTS[status] : 'default';
}

/** Resultado da leitura de uma nota digitada: valor válido ou a mensagem a mostrar. */
export type GradeInput =
  | { ok: true; value: number }
  | { ok: false; error: string };

/** Tradutor injetado: o domínio não importa o catálogo, só recebe o texto pronto. */
export type Translate = (key: MessageKey, params?: Record<string, string | number>) => string;

/**
 * Lê a nota digitada e recusa o que a API recusaria.
 *
 * Aceita vírgula porque o teclado numérico em pt-BR produz `7,5` — enviar isso como `7.5` é
 * responsabilidade desta função, não de cada campo da tela. O texto do erro vem do catálogo,
 * recebido por parâmetro, para que a mensagem acompanhe o idioma ativo.
 */
export function parseGradeInput(raw: string, t: Translate): GradeInput {
  const trimmed = raw.trim();
  if (trimmed === '') {
    return { ok: false, error: t('grades.error.required') };
  }

  const normalized = trimmed.replace(',', '.');
  // `Number('')` é 0 e `Number('7,5x')` é NaN: a checagem de formato vem antes da faixa.
  const value = Number(normalized);
  if (!Number.isFinite(value)) {
    return { ok: false, error: t('grades.error.invalid') };
  }

  if (value < GRADE_MIN || value > GRADE_MAX) {
    return {
      ok: false,
      error: t('grades.error.range', { min: GRADE_MIN, max: GRADE_MAX }),
    };
  }

  return { ok: true, value };
}

/**
 * Nota para exibição: vazio vira travessão e a fração usa o separador do locale ativo —
 * `7.5` em inglês, `7,5` em português e espanhol.
 */
export function formatGrade(value: number | null, locale: string = DEFAULT_LOCALE): string {
  if (value === null) return '—';
  return value.toLocaleString(locale, { maximumFractionDigits: 2 });
}

/**
 * `DateOnly` chega como `YYYY-MM-DD`. Passar por `new Date()` interpretaria como meia-noite UTC e,
 * em fuso negativo (Brasil, UTC-3), a data exibida cairia para o dia anterior.
 */
export function formatDateOnly(value: string, locale: string = DEFAULT_LOCALE): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  if (!match) return value;
  const [, year, month, day] = match;
  // Data montada em UTC e formatada em UTC: sem isso, `2026-03-10` viraria 09/03 em fuso negativo.
  const date = new Date(Date.UTC(Number(year), Number(month) - 1, Number(day)));
  return new Intl.DateTimeFormat(locale, { dateStyle: 'short', timeZone: 'UTC' }).format(date);
}
