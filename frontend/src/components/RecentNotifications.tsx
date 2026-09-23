import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowRight } from 'lucide-react';
import { Card } from './Card';
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

  return (
    <Card>
      <div className="flex items-center justify-between mb-4">
        <h3 className="font-semibold text-gray-800">Atividade recente</h3>
        <Link
          to={viewAllTo}
          className="inline-flex items-center gap-1 text-sm text-violet-600 hover:text-violet-700"
        >
          Ver todas <ArrowRight size={14} />
        </Link>
      </div>

      {failed ? (
        <div className="text-center py-4">
          <p className="text-sm text-gray-500">Não foi possível carregar as notificações.</p>
          <Button variant="secondary" size="sm" className="mt-3" onClick={retry}>
            Tentar de novo
          </Button>
        </div>
      ) : items === null ? (
        <ul className="space-y-4">
          {[0, 1, 2].map((i) => (
            <li key={i}>
              <span className="block h-4 w-2/3 rounded bg-gray-200 animate-pulse" />
              <span className="block h-3 w-full rounded bg-gray-100 animate-pulse mt-2" />
            </li>
          ))}
        </ul>
      ) : items.length === 0 ? (
        <p className="text-sm text-gray-500 text-center py-4">Nenhuma notificação.</p>
      ) : (
        <ul className="divide-y divide-gray-100">
          {items.map((n) => (
            <li key={n.userNotificationId} className="py-3 first:pt-0 last:pb-0">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p
                    className={`text-sm truncate ${
                      n.isRead ? 'text-gray-500' : 'font-medium text-gray-800'
                    }`}
                  >
                    {n.titulo}
                  </p>
                  <p className="text-sm text-gray-500 mt-0.5 line-clamp-2">{n.mensagem}</p>
                  <p className="text-xs text-gray-400 mt-1">
                    {new Date(n.createdAt).toLocaleString('pt-BR')}
                  </p>
                </div>
                <div className="flex items-center gap-2 shrink-0">
                  {!n.isRead && <span className="w-2 h-2 rounded-full bg-violet-600" />}
                  <Badge>{n.tipo}</Badge>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
