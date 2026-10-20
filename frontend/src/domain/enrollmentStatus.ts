import type { BadgeVariant } from '../components/badgeStyles';

/**
 * Máquina de estados da matrícula, do lado do frontend.
 *
 * **A autoridade é o backend** (`EnrollmentService.Transitions`, em `Ciclo.Infrastructure`), que
 * recusa qualquer transição fora da tabela dele. Este arquivo é o espelho daquela tabela.
 *
 * Por que espelhar em vez de perguntar à API: as projeções que montam `EnrollmentDto` são
 * traduzidas para SQL, então não dá para calcular as transições permitidas dentro delas. Expor o
 * campo exigiria materializar a consulta antes de projetar, em 4 endpoints. O espelho fica em **um
 * só arquivo** para que a duplicação seja visível e barata de corrigir; o teste de contrato
 * `EnrollmentStateMachineTests` (backend) trava a tabela e aponta para cá.
 *
 * O defeito que isto corrige: a tela oferecia ação apenas para `Pendente` e filtrava 3 dos 6
 * estados, e `POST /enrollments/{id}/submit` não era chamado por nenhuma página — matrícula criada
 * em `Rascunho` não tinha como avançar.
 */

export const ENROLLMENT_STATUSES = [
  'Rascunho',
  'Pendente',
  'DocumentacaoPendente',
  'Aprovado',
  'Rejeitado',
  'Cancelado',
] as const;

export type EnrollmentStatus = (typeof ENROLLMENT_STATUSES)[number];

/** Operações que a API expõe, uma por endpoint. */
export type EnrollmentAction = 'submit' | 'approve' | 'reject' | 'cancel';

const STATUS_VARIANTS: Record<EnrollmentStatus, BadgeVariant> = {
  Rascunho: 'default',
  Pendente: 'warning',
  DocumentacaoPendente: 'info',
  Aprovado: 'success',
  Rejeitado: 'danger',
  Cancelado: 'default',
};

const ACTIONS: Record<EnrollmentStatus, EnrollmentAction[]> = {
  // `submit` decide sozinho se o destino é `Pendente` ou `DocumentacaoPendente`: quem verifica a
  // documentação obrigatória é o backend (Spec 50 FR-007). Não há escolha para o usuário fazer.
  Rascunho: ['submit'],
  Pendente: ['approve', 'reject', 'cancel'],
  DocumentacaoPendente: ['approve', 'reject', 'cancel'],
  // Terminais. Não há caminho de volta — a política de reversão é o item M12 do plano de correções.
  Aprovado: [],
  Rejeitado: [],
  Cancelado: [],
};

const ACTION_LABELS: Record<EnrollmentAction, string> = {
  submit: 'Enviar',
  approve: 'Aprovar',
  reject: 'Rejeitar',
  cancel: 'Cancelar',
};

/** Só `Rejeitar` exige motivo (10 a 500 caracteres) e por isso abre modal em vez de agir direto. */
export function actionRequiresReason(action: EnrollmentAction): boolean {
  return action === 'reject';
}

export function actionLabel(action: EnrollmentAction): string {
  return ACTION_LABELS[action];
}

export function statusVariant(status: string): BadgeVariant {
  return isKnownStatus(status) ? STATUS_VARIANTS[status] : 'default';
}

export function actionsFor(status: string): EnrollmentAction[] {
  return isKnownStatus(status) ? ACTIONS[status] : [];
}

function isKnownStatus(status: string): status is EnrollmentStatus {
  return (ENROLLMENT_STATUSES as readonly string[]).includes(status);
}
