interface DemoEnv {
  DEV?: boolean;
  VITE_API_URL?: string;
  VITE_DEMO_MODE?: string;
}

const env = (import.meta as unknown as { env?: DemoEnv }).env ?? {};

/**
 * Liga o modo demonstração quando não existe API configurada.
 *
 * Em desenvolvimento a API é alcançada pelo proxy do Vite (`/api` → localhost), então a demo
 * fica desligada — senão o dev local passaria a mostrar dados falsos sem ninguém perceber.
 * `VITE_DEMO_MODE=1` força a demo também no dev, para testar o fluxo antes do deploy.
 */
export const isDemoMode = env.VITE_DEMO_MODE === '1' || (!env.DEV && !env.VITE_API_URL);
