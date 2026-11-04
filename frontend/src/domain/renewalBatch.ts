import type { MessageKey } from '../i18n/messages';
import type { RenewalOutcome, RenewalPreviewItem } from '../types';

/**
 * Regras puras da tela de rematrícula em lote (Spec 160 FR-006).
 *
 * Ficam fora do componente para que a decisão "quem pode ser renovado" e o vocabulário do
 * desfecho sejam provados sem navegador. A elegibilidade em si é do backend
 * (`EnrollmentRenewalService`); aqui só se respeita o que ele respondeu.
 */

/** Alunos que podem entrar no lote: apenas os marcados como elegíveis pelo backend. */
export function selectableStudentIds(items: RenewalPreviewItem[]): string[] {
  return items.filter((item) => item.eligible === true).map((item) => item.studentId);
}

const OUTCOME_LABEL_KEYS: Record<RenewalOutcome, MessageKey> = {
  Renewed: 'renewal.outcome.renewed',
  SkippedAlreadyExists: 'renewal.outcome.skipped',
  BlockedPendingDocuments: 'renewal.outcome.blocked',
  Failed: 'renewal.outcome.failed',
};

/** Chave do rótulo do desfecho por aluno — o texto mora no catálogo, não aqui. */
export function outcomeLabelKey(outcome: RenewalOutcome): MessageKey {
  return OUTCOME_LABEL_KEYS[outcome];
}
