import api from './client';
import type { User } from '../types';

export async function login(email: string, password: string): Promise<{ accessToken: string; user: User }> {
  const res = await api.post('/auth/login', { email, password });
  return res.data;
}

export async function refreshToken(): Promise<{ accessToken: string }> {
  const res = await api.post('/auth/refresh');
  return res.data;
}

export async function logout(): Promise<void> {
  await api.post('/auth/revoke');
}

export async function getMe(): Promise<User> {
  const res = await api.get('/auth/me');
  return res.data;
}

export async function forgotPassword(email: string): Promise<{ resetToken?: string }> {
  const res = await api.post('/auth/forgot-password', { email });
  return res.data;
}

export async function resetPassword(email: string, token: string, newPassword: string): Promise<void> {
  await api.post('/auth/reset-password', { email, token, newPassword });
}
