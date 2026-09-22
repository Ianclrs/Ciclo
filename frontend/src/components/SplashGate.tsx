import { useState, useEffect, type ReactNode } from 'react';

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
      className="min-h-screen flex items-center justify-center bg-gradient-to-br from-stone-100 via-stone-50 to-stone-100 animate-gradient px-4 relative overflow-hidden cursor-pointer select-none"
    >
      {/* Decorative animated blobs */}
      <div className="absolute -top-40 -right-40 w-96 h-96 bg-violet-300/40 rounded-full blur-3xl animate-blob" style={{ animationDelay: '0s', animationDuration: '24s' }} />
      <div className="absolute top-1/2 -left-20 w-72 h-72 bg-blue-300/35 rounded-full blur-3xl animate-blob" style={{ animationDelay: '2s', animationDuration: '31s', animationDirection: 'reverse' }} />
      <div className="absolute -bottom-32 right-1/4 w-80 h-80 bg-emerald-300/35 rounded-full blur-3xl animate-blob" style={{ animationDelay: '4s', animationDuration: '27s' }} />
      <div className="absolute top-1/4 left-1/3 w-60 h-60 bg-amber-300/35 rounded-full blur-3xl animate-blob" style={{ animationDelay: '6s', animationDuration: '35s', animationDirection: 'reverse' }} />
      <div className="absolute -top-24 -left-24 w-72 h-72 bg-red-400/30 rounded-full blur-3xl animate-blob" style={{ animationDelay: '8s', animationDuration: '25s' }} />
      <div className="absolute bottom-12 right-4 w-56 h-56 bg-fuchsia-300/30 rounded-full blur-3xl animate-blob" style={{ animationDelay: '10s', animationDuration: '28s', animationDirection: 'reverse' }} />
      <div className="absolute top-1/2 right-0 w-52 h-52 bg-orange-300/35 rounded-full blur-3xl animate-blob" style={{ animationDelay: '12s', animationDuration: '34s' }} />

      <div className="flex flex-col items-center relative z-10">
        <div className="relative">
          {/* Halo pulsante atrás da logo */}
          <div className="absolute inset-0 rounded-full bg-violet-400/30 blur-2xl animate-logo-pulse motion-reduce:animate-none" />
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
