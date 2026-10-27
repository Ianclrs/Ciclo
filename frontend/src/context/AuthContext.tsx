import { createContext, useState, useEffect, useRef, type ReactNode } from 'react';
import { getAccessToken, setAccessToken } from '../api/client';
import * as authApi from '../api/auth';
import type { User } from '../types';

interface AuthState {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  refreshToken: () => Promise<string | null>;
  getCurrentUser: () => Promise<User | null>;
}

export const AuthContext = createContext<AuthState>({
  user: null, isAuthenticated: false, isLoading: true,
  login: async () => {}, logout: async () => {},
  refreshToken: async () => null, getCurrentUser: async () => null,
});

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const initialized = useRef(false);

  useEffect(() => {
    if (initialized.current) return;
    initialized.current = true;
    const init = async () => {
      const token = getAccessToken();
      if (token) {
        try {
          const me = await authApi.getMe();
          setUser(me);
        } catch { setAccessToken(null); }
      } else {
        // Tenta restaurar a sessão via refresh token (cookie httpOnly)
        try {
          const newToken = await authApi.refreshToken();
          setAccessToken(newToken.accessToken);
          const me = await authApi.getMe();
          setUser(me);
        } catch { /* sem sessão válida */ }
      }
      setIsLoading(false);
    };
    init();
  }, []);

  const login = async (email: string, password: string) => {
    const res = await authApi.login(email, password);
    setAccessToken(res.accessToken);
    setUser(res.user);
  };

  const logout = async () => {
    // Best-effort: falha ao revogar no servidor não pode impedir a saída local
    // (o token expira sozinho). Não é carregamento de dados — nada fica "vazio".
    try { await authApi.logout(); } catch { /* revogação remota best-effort */ }
    setAccessToken(null);
    setUser(null);
  };

  const refreshToken = async (): Promise<string | null> => {
    try {
      const res = await authApi.refreshToken();
      setAccessToken(res.accessToken);
      return res.accessToken;
    } catch { return null; }
  };

  const getCurrentUser = async (): Promise<User | null> => {
    try {
      const me = await authApi.getMe();
      setUser(me);
      return me;
    } catch { return null; }
  };

  return (
    <AuthContext.Provider value={{ user, isAuthenticated: !!user, isLoading, login, logout, refreshToken, getCurrentUser }}>
      {children}
    </AuthContext.Provider>
  );
}
