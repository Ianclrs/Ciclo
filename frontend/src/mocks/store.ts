import { createSeed, type DemoState } from './seed.ts';

export const DEMO_STORAGE_KEY = 'ciclo.demo.v1';

/** Cópia em memória para ambientes sem `localStorage` (testes, navegador com storage bloqueado). */
let memoryFallback: DemoState | null = null;

function storage(): Storage | null {
  try {
    return typeof window !== 'undefined' ? window.localStorage : null;
  } catch {
    // Storage bloqueado por política do navegador: a demo continua, só não persiste.
    return null;
  }
}

export function resetDemoState(): DemoState {
  const seed = createSeed();
  memoryFallback = seed;
  const store = storage();
  if (store) {
    store.setItem(DEMO_STORAGE_KEY, JSON.stringify(seed));
  }
  return seed;
}

export function loadDemoState(): DemoState {
  const store = storage();
  if (!store) {
    memoryFallback ??= createSeed();
    return memoryFallback;
  }

  const raw = store.getItem(DEMO_STORAGE_KEY);
  if (!raw) {
    const seed = createSeed();
    store.setItem(DEMO_STORAGE_KEY, JSON.stringify(seed));
    return seed;
  }

  try {
    return JSON.parse(raw) as DemoState;
  } catch {
    // Registro corrompido (versão antiga do formato, edição manual): recomeça da semente.
    return resetDemoState();
  }
}

export function saveDemoState(state: DemoState): void {
  memoryFallback = state;
  const store = storage();
  if (store) {
    store.setItem(DEMO_STORAGE_KEY, JSON.stringify(state));
  }
}
