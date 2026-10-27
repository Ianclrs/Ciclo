import api from './client';
import type { ManagedUser, ManagedRole } from '../types';

export interface InviteRequest {
  email: string;
  name: string;
  role: ManagedRole;
}

export interface InviteResult {
  user: ManagedUser;
  /** Só vem em Development: o token de definição de senha que ativa a conta. */
  inviteToken?: string;
}

export async function getUsers(): Promise<ManagedUser[]> {
  const res = await api.get('/api/users');
  return res.data;
}

export async function inviteUser(data: InviteRequest): Promise<InviteResult> {
  const res = await api.post('/api/users/invite', data);

  // Em produção a resposta é o usuário; em Development vem `{ user, inviteToken }`.
  return res.data.user
    ? { user: res.data.user, inviteToken: res.data.inviteToken }
    : { user: res.data };
}

export async function changeUserRole(id: string, role: ManagedRole): Promise<ManagedUser> {
  const res = await api.patch(`/api/users/${id}/role`, { role });
  return res.data;
}

export async function setUserActive(id: string, isActive: boolean): Promise<ManagedUser> {
  const res = await api.patch(`/api/users/${id}/active`, { isActive });
  return res.data;
}
