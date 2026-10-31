import axios from 'axios';
import { createSingleFlight } from './singleFlight';

let accessToken: string | null = null;

export function getAccessToken(): string | null {
  return accessToken;
}

export function setAccessToken(token: string | null): void {
  accessToken = token;
}

const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL || '',
  withCredentials: true,
});

api.interceptors.request.use((config) => {
  const token = getAccessToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Uma única renovação em voo: o refresh rotaciona o token no servidor, então
// renovações paralelas fariam os perdedores usarem um token já revogado (A9).
const refreshAccessToken = createSingleFlight(async () => {
  const res = await axios.post(`${api.defaults.baseURL}/api/auth/refresh`, {}, { withCredentials: true });
  const token = res.data.accessToken as string;
  setAccessToken(token);
  return token;
});

// Falhas concorrentes compartilham a mesma sessão encerrada: um único redirect.
let sessionExpiredHandled = false;

function handleSessionExpired(): void {
  setAccessToken(null);
  if (sessionExpiredHandled) {
    return;
  }
  sessionExpiredHandled = true;
  window.location.href = '/login';
}

api.interceptors.response.use(
  (res) => res,
  async (error) => {
    const original = error.config;
    // Não interceptar chamadas ao próprio refresh para evitar loop infinito
    if (original.url === '/api/auth/refresh') {
      return Promise.reject(error);
    }
    // Não tentar refresh em endpoints anônimos de autenticação:
    // o 401 do login é só "credenciais inválidas" e deve aparecer na tela.
    const anonymousAuthEndpoints = [
      '/api/auth/login',
      '/api/auth/forgot-password',
      '/api/auth/reset-password',
      '/api/auth/accept-invite',
    ];
    if (anonymousAuthEndpoints.includes(original.url)) {
      return Promise.reject(error);
    }
    if (error.response?.status === 401 && !original._retry) {
      original._retry = true;
      try {
        const newToken = await refreshAccessToken();
        original.headers.Authorization = `Bearer ${newToken}`;
        return api(original);
      } catch {
        handleSessionExpired();
        return Promise.reject(error);
      }
    }
    return Promise.reject(error);
  },
);

export default api;
