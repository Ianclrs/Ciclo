/**
 * Regras de apresentação/validação da gestão de usuários (M11).
 *
 * Ficam fora do componente para poderem ser testadas sem DOM — e porque a validação do convite
 * é a única parte dessa tela que pode ser verificada de forma determinística.
 */

import type { MessageKey } from '../i18n/messages';

export type ManagedRole = 'Admin' | 'Staff' | 'Parent';

/** Papéis que podem ser convidados: responsável se cadastra sozinho pelo slug do colégio. */
export const INVITE_ROLES = ['Admin', 'Staff'] as const;
export type InviteRole = (typeof INVITE_ROLES)[number];

const ROLE_LABEL_KEYS: Record<ManagedRole, MessageKey> = {
  Admin: 'role.admin',
  Staff: 'role.staff',
  Parent: 'role.parent',
};

/**
 * Chave do rótulo legível; `null` para papel desconhecido. O texto está no catálogo — antes o
 * rótulo era a string em português, o que impedia a tela de acompanhar o idioma.
 */
export function roleLabelKey(role: string): MessageKey | null {
  return ROLE_LABEL_KEYS[role as ManagedRole] ?? null;
}

export interface InviteForm {
  name: string;
  email: string;
  role: InviteRole;
}

/** Devolve a chave da mensagem de erro, ou null quando o formulário está válido. */
export function validateInvite(form: InviteForm): MessageKey | null {
  if (!form.name.trim()) return 'users.validate.name';

  const email = form.email.trim();
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(email)) return 'users.validate.email';

  if (!INVITE_ROLES.includes(form.role)) return 'users.validate.role';

  return null;
}

/**
 * O próprio usuário não pode trocar o próprio papel nem se desativar pela tela.
 * A API também recusa (`cannot_change_own_role` / `cannot_deactivate_self`) — isto evita
 * oferecer um botão que só produz erro.
 */
export function canChangeRole(actorId: string, targetId: string): boolean {
  return actorId !== targetId;
}

/** Chave do rótulo da ação de status, conforme o estado atual. */
export function activationLabelKey(isActive: boolean): MessageKey {
  return isActive ? 'user.deactivate' : 'user.activate';
}
