import api from './client';
import type { User } from '../types';

/**
 * `rememberMe` decide a NATUREZA da sessão no servidor: com `true` o cookie de refresh é
 * persistente (30 dias); com `false` é cookie de sessão, descartado ao fechar o navegador.
 * Vem sempre explícito para não depender do padrão do backend.
 */
export async function login(
  email: string,
  password: string,
  rememberMe = false,
): Promise<{ accessToken: string; user: User }> {
  const res = await api.post('/api/auth/login', { email, password, rememberMe });
  return res.data;
}

export async function registerParent(input: {
  schoolSlug: string;
  name: string;
  email: string;
  password: string;
}): Promise<{ accessToken: string; user: User }> {
  const res = await api.post('/api/auth/register/parent', input);
  return res.data;
}

export async function refreshToken(): Promise<{ accessToken: string }> {
  const res = await api.post('/api/auth/refresh');
  return res.data;
}

export async function logout(): Promise<void> {
  await api.post('/api/auth/revoke');
}

export async function getMe(): Promise<User> {
  const res = await api.get('/api/auth/me');
  return res.data;
}

export async function forgotPassword(email: string): Promise<{ resetToken?: string }> {
  const res = await api.post('/api/auth/forgot-password', { email });
  return res.data;
}

export async function resetPassword(email: string, token: string, newPassword: string): Promise<void> {
  await api.post('/api/auth/reset-password', { email, token, newPassword });
}
