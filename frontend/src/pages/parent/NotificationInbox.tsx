import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { CheckCheck, Inbox, MailOpen } from 'lucide-react';
import { Card } from '../../components/Card';
import { CardHeading } from '../../components/CardHeading';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Pagination } from '../../components/Pagination';
import * as api from '../../api/notifications';
import { apiErrorMessage } from '../../api/errors';
import { LoadError } from '../../components/LoadError';
import { Skeleton } from '../../components/Skeleton';
import { formatDateTime } from '../../utils/format';
import type { Notification } from '../../types';

const PAGE_SIZE = 10;

export default function NotificationInbox() {
  const [items, setItems] = useState<Notification[] | null>(null);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  // `items === null` é o estado de carga: o efeito só escreve estado de forma assíncrona.
  const loading = items === null;

  const changePage = (next: number) => {
    setItems(null);
    setPage(next);
  };

  const toggleUnreadOnly = () => {
    setItems(null);
    setPage(1);
    setUnreadOnly((value) => !value);
  };

  const retry = () => {
    setItems(null);
    setReloadKey((key) => key + 1);
  };

  useEffect(() => {
    let active = true;
    api
      .getNotifications({ unreadOnly: unreadOnly || undefined, page, pageSize: PAGE_SIZE })
      .then((res) => {
        if (!active) return;
        setItems(res.items);
        setTotal(res.total);
        setError(null);
      })
      .catch((err) => {
        if (!active) return;
        setError(apiErrorMessage(err, 'Não foi possível carregar as notificações.'));
      });
    return () => {
      active = false;
    };
  }, [page, unreadOnly, reloadKey]);

  const handleMarkRead = async (notificationId: string) => {
    try {
      await api.markRead(notificationId);
      setItems(
        (items ?? []).map((n) =>
          n.userNotificationId === notificationId ? { ...n, isRead: true } : n,
        ),
      );
    } catch (err) {
      toast.error(apiErrorMessage(err, 'Não foi possível marcar como lida.'));
    }
  };

  const handleMarkAll = async () => {
    try {
      const res = await api.markAllRead();
      toast.success(`${res.updatedCount} marcadas como lidas.`);
      setReloadKey((k) => k + 1);
    } catch (err) {
      toast.error(apiErrorMessage(err, 'Não foi possível marcar todas como lidas.'));
    }
  };

  return (
    <div>
      <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h2 className="text-3xl font-bold tracking-tight text-gray-900">Notificações</h2>
          <p className="mt-1 text-base text-gray-700">Avisos da escola para você.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            variant={unreadOnly ? 'primary' : 'secondary'}
            size="sm"
            aria-pressed={unreadOnly}
            onClick={toggleUnreadOnly}
          >
            Não lidas
          </Button>
          <Button variant="ghost" size="sm" className="gap-1.5" onClick={handleMarkAll}>
            <CheckCheck size={14} /> Marcar todas
          </Button>
        </div>
      </div>

      <Card tone="glass">
        <CardHeading
          icon={Inbox}
          title={unreadOnly ? 'Não lidas' : 'Caixa de entrada'}
          action={
            total > 0 ? (
              <span className="shrink-0 rounded-full bg-white/80 px-3 py-1 text-xs font-medium text-gray-700">
                {total} {total === 1 ? 'aviso' : 'avisos'}
              </span>
            ) : undefined
          }
        />

        {error ? (
          <LoadError message={error} onRetry={retry} />
        ) : loading || items === null ? (
          <ul className="space-y-4">
            {[0, 1, 2, 3].map((i) => (
              <li key={i}>
                <Skeleton className="h-4 w-1/3" />
                <Skeleton className="mt-2 h-3 w-2/3" />
              </li>
            ))}
          </ul>
        ) : items.length === 0 ? (
          <div className="flex flex-col items-center py-8 text-center">
            <MailOpen size={28} className="text-gray-300" />
            <p className="mt-2 text-sm text-gray-600">
              {unreadOnly ? 'Nenhuma notificação não lida.' : 'Nenhuma notificação por aqui.'}
            </p>
          </div>
        ) : (
          <ul className="-mx-2 space-y-0.5">
            {items.map((n) => (
              <li key={n.userNotificationId}>
                <button
                  type="button"
                  // Ler é a ação do clique; o item já lido continua focável para leitura.
                  onClick={() => !n.isRead && handleMarkRead(n.userNotificationId)}
                  className={`w-full rounded-lg px-2 py-2.5 text-left transition-colors hover:bg-white/60 ${
                    n.isRead ? '' : 'bg-violet-50/60'
                  }`}
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
                          {formatDateTime(n.createdAt)}
                        </span>
                      </div>
                      <p className="mt-0.5 text-sm text-gray-600">{n.mensagem}</p>
                      <div className="mt-1.5 flex items-center gap-2">
                        <Badge>{n.tipo}</Badge>
                        {!n.isRead && (
                          <span className="text-xs font-medium text-violet-700">
                            Marcar como lida
                          </span>
                        )}
                      </div>
                    </div>
                  </div>
                </button>
              </li>
            ))}
          </ul>
        )}

        <Pagination page={page} pageSize={PAGE_SIZE} total={total} onPageChange={changePage} />
      </Card>
    </div>
  );
}
