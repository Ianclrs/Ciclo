import type { ReactNode } from 'react';

/**
 * `solid` para telas sem fundo decorativo; `glass` deixa o shader do layout atravessar.
 * O desfoque existe para o texto continuar legível sobre a textura — sem ele, a
 * transparência viraria ruído atrás do conteúdo.
 */
export type CardTone = 'solid' | 'glass';

const tones: Record<CardTone, string> = {
  solid: 'bg-white border-gray-200 shadow-sm',
  glass: 'bg-white/70 border-white/60 shadow-sm backdrop-blur-md',
};

interface CardProps {
  title?: string;
  children: ReactNode;
  className?: string;
  tone?: CardTone;
}

export function Card({ title, children, className = '', tone = 'solid' }: CardProps) {
  return (
    <div className={`rounded-xl border ${tones[tone]} ${className}`}>
      {title && <div className="px-6 py-4 border-b border-gray-200 font-semibold text-gray-800">{title}</div>}
      <div className="p-6">{children}</div>
    </div>
  );
}
