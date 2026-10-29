import { useState, useEffect, type ReactNode } from 'react';
import { ShaderBackground } from './ShaderBackground';

// Splash exibida uma única vez por sessão do navegador (sobrevive a reloads/navegação,
// mas reaparece em uma nova aba/sessão).
const SEEN_KEY = 'ciclo.splash.seen';

// Ações que dispensam a splash: clique/toque, qualquer tecla ou rolagem.
const DISMISS_EVENTS = ['pointerdown', 'touchstart', 'keydown', 'wheel'] as const;

function wasSeenThisSession(): boolean {
  try {
    return sessionStorage.getItem(SEEN_KEY) === '1';
  } catch {
    // sessionStorage indisponível (ex: cookies bloqueados): exibe a splash.
    return false;
  }
}

function markSeen(): void {
  try {
    sessionStorage.setItem(SEEN_KEY, '1');
  } catch {
    // Sem persistência: a splash volta a aparecer na próxima carga. Não é um erro fatal.
  }
}

export function SplashGate({ children }: { children: ReactNode }) {
  const [dismissed, setDismissed] = useState(wasSeenThisSession);

  useEffect(() => {
    if (dismissed) return;

    const dismiss = () => {
      markSeen();
      setDismissed(true);
    };

    DISMISS_EVENTS.forEach((event) =>
      window.addEventListener(event, dismiss, { once: true }),
    );

    return () => {
      DISMISS_EVENTS.forEach((event) => window.removeEventListener(event, dismiss));
    };
  }, [dismissed]);

  if (dismissed) {
    return <>{children}</>;
  }

  return (
    <div
      role="button"
      tabIndex={0}
      aria-label="Continuar para o login"
      className="min-h-screen flex items-center justify-center bg-[#eceff2] px-4 relative overflow-hidden cursor-pointer select-none"
    >
      <ShaderBackground />

      <div className="flex flex-col items-center relative z-10">
        <div className="relative">
          <img
            src="/Ciclo_favicon.png"
            alt="Ciclo"
            className="relative w-32 h-32 sm:w-40 sm:h-40 animate-logo-pulse motion-reduce:animate-none"
          />
        </div>
        <h1 className="mt-6 text-4xl font-bold text-stone-800 tracking-tight">Ciclo</h1>
        <p className="text-stone-500 mt-1 text-sm">Seu Sistema de Gestão Escolar</p>
        <p className="mt-10 text-xs text-stone-400 animate-pulse motion-reduce:animate-none">
          Toque ou pressione qualquer tecla para continuar
        </p>
      </div>
    </div>
  );
}
