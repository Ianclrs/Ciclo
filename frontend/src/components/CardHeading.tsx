import type { ReactNode } from 'react';
import type { LucideIcon } from 'lucide-react';
import { accentChip, type Accent } from './accentStyles';

interface CardHeadingProps {
  icon: LucideIcon;
  title: string;
  accent?: Accent;
  /** Conteúdo alinhado à direita: link de ação, badge de estado, contador. */
  action?: ReactNode;
}

/**
 * Cabeçalho padrão dos painéis do dashboard: chip de ícone, título e uma ação à direita.
 * Vive em um só lugar para os painéis não divergirem de tamanho e espaçamento entre si.
 */
export function CardHeading({ icon: Icon, title, accent = 'violet', action }: CardHeadingProps) {
  return (
    <div className="mb-4 flex items-center gap-3">
      <div
        className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ring-1 ring-inset ${accentChip[accent]}`}
      >
        <Icon size={20} strokeWidth={1.9} />
      </div>
      <h3 className="min-w-0 flex-1 truncate text-base font-semibold text-gray-800">{title}</h3>
      {action}
    </div>
  );
}
