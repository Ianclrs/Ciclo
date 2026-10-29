import type { AxiosInstance, AxiosResponse, InternalAxiosRequestConfig } from 'axios';
import { handleRequest } from './handlers';
import { loadDemoState, saveDemoState } from './store';

export { isDemoMode } from './mode';
export { DEMO_STORAGE_KEY, resetDemoState } from './store';
export { DEMO_ACCOUNTS } from './accounts';

/**
 * Substitui o transporte HTTP do axios por uma API em memória.
 *
 * Feito no adaptador — e não com Service Worker ou interceptor de resposta — porque é o único
 * ponto por onde toda requisição passa, sem depender de HTTPS, registro de SW ou suporte do
 * navegador. Em produção com API real (VITE_API_URL definida) nada disto é instalado.
 */
export function installDemoApi(api: AxiosInstance): void {
  api.defaults.adapter = async (config) => {
    const state = loadDemoState();
    const method = (config.method ?? 'get').toUpperCase();
    const url = config.url ?? '';
    const params = (config.params ?? {}) as Record<string, unknown>;

    const result = handleRequest(state, method, url, params, parseBody(config.data));
    // Persiste depois de cada requisição: qualquer escrita vale para o próximo carregamento.
    saveDemoState(state);

    const response: AxiosResponse = {
      data: result.data,
      status: result.status,
      statusText: '',
      headers: {},
      config: config as InternalAxiosRequestConfig,
    };

    if (result.status >= 400) {
      // Mesma forma de erro do axios, para `apiErrorMessage` e as telas reagirem sem saber da demo.
      throw Object.assign(new Error(`Demonstração: ${result.status}`), {
        isAxiosError: true,
        config,
        response,
        toJSON: () => ({}),
      });
    }

    return response;
  };
}

function parseBody(data: unknown): Record<string, unknown> {
  if (!data) return {};

  if (typeof data === 'string') {
    try {
      return JSON.parse(data) as Record<string, unknown>;
    } catch {
      // Corpo não-JSON (ex.: texto enviado por outro cliente): a rota trata como vazio.
      return {};
    }
  }

  if (typeof FormData !== 'undefined' && data instanceof FormData) {
    const entries: Record<string, unknown> = {};
    data.forEach((value, key) => {
      if (typeof value === 'string') entries[key] = value;
    });
    return entries;
  }

  if (typeof data === 'object') return data as Record<string, unknown>;
  return {};
}
