import axios from 'axios';

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

api.interceptors.response.use(
  (res) => res,
  async (error) => {
    const original = error.config;
    // Não interceptar chamadas ao próprio refresh para evitar loop infinito
    if (original.url === '/auth/refresh') {
      return Promise.reject(error);
    }
    // Não tentar refresh em endpoints anônimos de autenticação:
    // o 401 do login é só "credenciais inválidas" e deve aparecer na tela.
    const anonymousAuthEndpoints = ['/auth/login', '/auth/forgot-password', '/auth/reset-password'];
    if (anonymousAuthEndpoints.includes(original.url)) {
      return Promise.reject(error);
    }
    if (error.response?.status === 401 && !original._retry) {
      original._retry = true;
      try {
        const res = await axios.post(`${api.defaults.baseURL}/auth/refresh`, {}, { withCredentials: true });
        const newToken = res.data.accessToken as string;
        setAccessToken(newToken);
        original.headers.Authorization = `Bearer ${newToken}`;
        return api(original);
      } catch {
        setAccessToken(null);
        window.location.href = '/login';
        return Promise.reject(error);
      }
    }
    return Promise.reject(error);
  },
);

export default api;
