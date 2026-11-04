import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { BellDot, Plus } from 'lucide-react';
import { Card } from '../../components/Card';
import { CardHeading } from '../../components/CardHeading';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Pagination } from '../../components/Pagination';
import * as api from '../../api/notifications';
import { apiErrorMessage } from '../../api/errors';
import { LoadError } from '../../components/LoadError';
import { Skeleton } from '../../components/Skeleton';
import { EmptyState } from '../../components/EmptyState';
import { formatDateTime } from '../../utils/format';
import { useLocale } from '../../i18n/LocaleContext';
import type { Notification } from '../../types';

const PAGE_SIZE = 10;

export default function NotificationList() {
  const { language, locale, t } = useLocale();
  const [items, setItems] = useState<Notification[] | null>(null);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  // `items === null` é o estado de carga: não há setState síncrono dentro do efeito.
  const loading = items === null;

  const changePage = (next: number) => {
    setItems(null);
    setPage(next);
  };

  const retry = () => {
    setItems(null);
    setReloadKey((key) => key + 1);
  };

  useEffect(() => {
    let active = true;
    api
      .getNotifications({ page, pageSize: PAGE_SIZE })
      .then((res) => {
        if (!active) return;
        setItems(res.items);
        setTotal(res.total);
        setError(null);
      })
      .catch((err) => {
        if (!active) return;
        setError(apiErrorMessage(err, t('notifications.loadError'), language));
      });
    return () => {
      active = false;
    };
  }, [page, reloadKey, t, language]);

  return (
    <div>
      <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h2 className="text-3xl font-bold tracking-tight text-gray-900">
            {t('nav.notifications')}
          </h2>
          <p className="mt-1 text-base text-gray-700">{t('notifications.subtitle')}</p>
        </div>
        <Link to="/admin/notifications/create">
          <Button className="gap-2 shrink-0">
            <Plus size={16} /> {t('notifications.new')}
          </Button>
        </Link>
      </div>

      <Card tone="glass">
        <CardHeading
          icon={BellDot}
          title={t('notifications.history')}
          action={
            total > 0 ? (
              <span className="shrink-0 rounded-full bg-white/80 px-3 py-1 text-xs font-medium text-gray-700">
                {t(total === 1 ? 'notifications.countOne' : 'notifications.countMany', {
                  count: total,
                })}
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
          <EmptyState message={t('notifications.empty')} />
        ) : (
          <ul className="-mx-2 space-y-0.5">
            {items.map((n) => (
              <li
                key={n.id}
                className="rounded-lg px-2 py-2.5 transition-colors hover:bg-white/60"
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-semibold text-gray-900">{n.titulo}</p>
                    <p className="mt-0.5 text-sm text-gray-600 line-clamp-2">{n.mensagem}</p>
                  </div>
                  <div className="flex shrink-0 items-center gap-3">
                    <Badge>{n.tipo}</Badge>
                    <span className="hidden text-xs text-gray-500 sm:inline">
                      {formatDateTime(n.createdAt, locale)}
                    </span>
                  </div>
                </div>
                <p className="mt-1 text-xs text-gray-500 sm:hidden">
                  {formatDateTime(n.createdAt, locale)}
                </p>
              </li>
            ))}
          </ul>
        )}

        <Pagination page={page} pageSize={PAGE_SIZE} total={total} onPageChange={changePage} />
      </Card>
    </div>
  );
}
