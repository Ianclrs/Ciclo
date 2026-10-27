/**
 * Regras de apresentação/validação da gestão de usuários (M11).
 *
 * Ficam fora do componente para poderem ser testadas sem DOM — e porque a validação do convite
 * é a única parte dessa tela que pode ser verificada de forma determinística.
 */

export type ManagedRole = 'Admin' | 'Staff' | 'Parent';

/** Papéis que podem ser convidados: responsável se cadastra sozinho pelo slug do colégio. */
export const INVITE_ROLES = ['Admin', 'Staff'] as const;
export type InviteRole = (typeof INVITE_ROLES)[number];

const ROLE_LABELS: Record<ManagedRole, string> = {
  Admin: 'Administração',
  Staff: 'Secretaria',
  Parent: 'Responsável',
};

/** Rótulo legível; papel desconhecido é devolvido como veio (não inventa texto). */
export function roleLabel(role: string): string {
  return ROLE_LABELS[role as ManagedRole] ?? role;
}

export interface InviteForm {
  name: string;
  email: string;
  role: InviteRole;
}

/** Devolve a mensagem de erro, ou null quando o formulário está válido. */
export function validateInvite(form: InviteForm): string | null {
  if (!form.name.trim()) return 'Informe o nome.';

  const email = form.email.trim();
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(email)) return 'Informe um e-mail válido.';

  if (!INVITE_ROLES.includes(form.role)) return 'Escolha um papel válido.';

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

/** Rótulo da ação de status, conforme o estado atual. */
export function activationLabel(isActive: boolean): string {
  return isActive ? 'Desativar' : 'Ativar';
}
