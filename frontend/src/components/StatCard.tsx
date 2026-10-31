import { Link } from 'react-router-dom';
import { ArrowUpRight, type LucideIcon } from 'lucide-react';
import { Card } from './Card';
import { Skeleton } from './Skeleton';
import { accentBar, accentChip, type Accent } from './accentStyles';

interface StatCardProps {
  label: string;
  value: number;
  icon: LucideIcon;
  accent: Accent;
  /** Quando informado, o card vira atalho para a página do domínio. Sem destino, não é clicável. */
  to?: string;
  loading?: boolean;
  /** Contexto do número principal, em texto. Sem detalhe, o rodapé do card não aparece. */
  detail?: string;
  /** Fração de 0 a 1 que a barra representa. A barra é decorativa; quem informa é o `detail`. */
  ratio?: number;
}

export function StatCard({
  label,
  value,
  icon: Icon,
  accent,
  to,
  loading = false,
  detail,
  ratio,
}: StatCardProps) {
  const percent = ratio === undefined ? null : Math.round(Math.min(1, Math.max(0, ratio)) * 100);

  const content = (
    <>
      <div className="flex items-start justify-between gap-3">
        <div className={`flex h-12 w-12 items-center justify-center rounded-xl ring-1 ring-inset shrink-0 ${accentChip[accent]}`}>
          <Icon size={24} strokeWidth={1.9} />
        </div>
        {to && (
          <ArrowUpRight
            size={18}
            className="shrink-0 text-gray-400 transition-colors group-hover:text-gray-700"
          />
        )}
      </div>

      <p className="mt-4 text-sm font-medium text-gray-600">{label}</p>
      {loading ? (
        <Skeleton className="mt-1 h-[30px] w-16" />
      ) : (
        <p className="mt-1 text-3xl font-bold leading-none text-gray-900 tabular-nums">{value}</p>
      )}

      {detail && (
        <div className="mt-4 border-t border-gray-200/70 pt-3">
          {/* O esqueleto repete a estrutura real (linha de texto + barra): reservando menos
              altura, o card cresce quando os dados chegam e empurra o que está abaixo. */}
          {loading ? (
            <>
              <Skeleton className="h-4 w-2/3" />
              {percent !== null && <Skeleton className="mt-2 h-1.5 w-full" />}
            </>
          ) : (
            <>
              <p className="text-xs text-gray-600">{detail}</p>
              {percent !== null && (
                <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-gray-200">
                  <div className={`h-full rounded-full ${accentBar[accent]}`} style={{ width: `${percent}%` }} />
                </div>
              )}
            </>
          )}
        </div>
      )}
    </>
  );

  if (!to) {
    return <Card tone="glass">{content}</Card>;
  }

  return (
    <Link
      to={to}
      className="group block rounded-xl focus:outline-none focus-visible:ring-2 focus-visible:ring-violet-500"
    >
      <Card tone="glass" className="h-full transition-all hover:shadow-md hover:border-white">
        {content}
      </Card>
    </Link>
  );
}
