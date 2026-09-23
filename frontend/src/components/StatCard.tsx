import { Link } from 'react-router-dom';
import { ArrowUpRight, type LucideIcon } from 'lucide-react';
import { Card } from './Card';

type Accent = 'violet' | 'blue' | 'green' | 'amber' | 'red';

const accents: Record<Accent, string> = {
  violet: 'bg-violet-100 text-violet-700',
  blue: 'bg-blue-100 text-blue-700',
  green: 'bg-green-100 text-green-700',
  amber: 'bg-amber-100 text-amber-700',
  red: 'bg-red-100 text-red-700',
};

interface StatCardProps {
  label: string;
  value: number;
  icon: LucideIcon;
  accent: Accent;
  /** Quando informado, o card vira atalho para a página do domínio. Sem destino, não é clicável. */
  to?: string;
  loading?: boolean;
}

export function StatCard({ label, value, icon: Icon, accent, to, loading = false }: StatCardProps) {
  const content = (
    <div className="flex items-center gap-4">
      <div className={`p-3 rounded-xl shrink-0 ${accents[accent]}`}>
        <Icon size={22} />
      </div>
      <div className="min-w-0 flex-1">
        <p className="text-sm text-gray-500 truncate">{label}</p>
        {loading ? (
          <span className="mt-1 block h-7 w-12 rounded bg-gray-200 animate-pulse" />
        ) : (
          <p className="text-2xl font-bold text-gray-800 tabular-nums">{value}</p>
        )}
      </div>
      {to && (
        <ArrowUpRight
          size={18}
          className="shrink-0 text-gray-300 transition-colors group-hover:text-gray-600"
        />
      )}
    </div>
  );

  if (!to) {
    return <Card>{content}</Card>;
  }

  return (
    <Link
      to={to}
      className="group block rounded-xl focus:outline-none focus-visible:ring-2 focus-visible:ring-violet-500"
    >
      <Card className="hover:shadow-md hover:border-gray-300 transition-all">{content}</Card>
    </Link>
  );
}
