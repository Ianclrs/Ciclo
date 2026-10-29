import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowRight, BellDot } from 'lucide-react';
import { Card } from './Card';
import { CardHeading } from './CardHeading';
import { Badge } from './Badge';
import { Button } from './Button';
import * as api from '../api/notifications';
import type { Notification } from '../types';

const LIMIT = 5;

/**
 * Notificações mais recentes do usuário logado, somente leitura.
 * Marcar como lida continua sendo responsabilidade de NotificationInbox —
 * assim este bloco não precisa compartilhar estado com os KPIs do dashboard.
 */
export function RecentNotifications({ viewAllTo }: { viewAllTo: string }) {
  const [items, setItems] = useState<Notification[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        const res = await api.getNotifications({ page: 1, pageSize: LIMIT });
        if (cancelled) return;
        setItems(res.items);
        setFailed(false);
      } catch {
        if (cancelled) return;
        setItems([]);
        setFailed(true);
      }
    })();

    // Descarta a resposta se o bloco sair da tela ou se um recarregamento mais novo tomar o lugar.
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  const retry = () => {
    setItems(null);
    setFailed(false);
    setReloadKey((key) => key + 1);
  };

  // Contagem dentro do recorte visível: o total global é responsabilidade do KPI "Não lidas".
  const unreadHere = items?.filter((item) => !item.isRead).length ?? 0;

  return (
    <Card tone="glass">
      <CardHeading
        icon={BellDot}
        title="Atividade recente"
        action={
          <div className="flex shrink-0 items-center gap-3">
            {unreadHere > 0 && (
              <span className="rounded-full bg-violet-100 px-2.5 py-0.5 text-xs font-medium text-violet-700">
                {unreadHere} {unreadHere === 1 ? 'não lida' : 'não lidas'}
              </span>
            )}
            <Link
              to={viewAllTo}
              className="inline-flex items-center gap-1 text-sm font-medium text-violet-700 hover:text-violet-800"
            >
              Ver todas <ArrowRight size={14} />
            </Link>
          </div>
        }
      />

      {failed ? (
        <div className="py-6 text-center">
          <p className="text-sm text-gray-600">Não foi possível carregar as notificações.</p>
          <Button variant="secondary" size="sm" className="mt-3" onClick={retry}>
            Tentar de novo
          </Button>
        </div>
      ) : items === null ? (
        <ul className="space-y-3">
          {[0, 1, 2].map((i) => (
            <li key={i} className="flex items-start gap-3">
              <span className="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-gray-200" />
              <div className="min-w-0 flex-1">
                <span className="block h-4 w-2/3 rounded bg-gray-200 animate-pulse" />
                <span className="mt-2 block h-3 w-full rounded bg-gray-100 animate-pulse" />
              </div>
            </li>
          ))}
        </ul>
      ) : items.length === 0 ? (
        <div className="flex flex-col items-center py-8 text-center">
          <BellDot size={28} className="text-gray-300" />
          <p className="mt-2 text-sm text-gray-600">Nenhuma notificação por aqui.</p>
        </div>
      ) : (
        <ul className="-mx-2 space-y-0.5">
          {items.map((n) => (
            <li
              key={n.userNotificationId}
              className="rounded-lg px-2 py-2.5 transition-colors hover:bg-white/60"
            >
              <div className="flex items-start gap-3">
                <span
                  aria-hidden="true"
                  className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ${
                    n.isRead ? 'bg-gray-300' : 'bg-violet-600'
                  }`}
                />
                <div className="min-w-0 flex-1">
                  <div className="flex items-start justify-between gap-3">
                    <p
                      className={`min-w-0 truncate text-sm ${
                        n.isRead ? 'text-gray-700' : 'font-semibold text-gray-900'
                      }`}
                    >
                      {n.titulo}
                    </p>
                    <span className="shrink-0 text-xs text-gray-500">
                      {new Date(n.createdAt).toLocaleString('pt-BR')}
                    </span>
                  </div>
                  <p className="mt-0.5 text-sm text-gray-600 line-clamp-2">{n.mensagem}</p>
                  <Badge className="mt-1.5">{n.tipo}</Badge>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
