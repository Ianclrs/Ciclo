import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Card } from '../../components/Card';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Pagination } from '../../components/Pagination';
import { Plus } from 'lucide-react';
import * as api from '../../api/notifications';
import { apiErrorMessage } from '../../api/errors';
import { LoadError } from '../../components/LoadError';
import { formatDate } from '../../utils/format';
import type { Notification } from '../../types';

export default function NotificationList() {
  const [items, setItems] = useState<Notification[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let active = true;
    api
      .getNotifications({ page, pageSize: 10 })
      .then((res) => {
        if (!active) return;
        setItems(res.items); setTotal(res.total); setError(null);
      })
      .catch((err) => {
        if (!active) return;
        setError(apiErrorMessage(err, 'Não foi possível carregar as notificações.'));
      });
    return () => { active = false; };
  }, [page, reloadKey]);

  return (
    <div>
      <div className="flex items-center justify-between mb-6 gap-3">
        <h2 className="text-2xl font-bold text-gray-900">Notificações</h2>
        <Link to="/admin/notifications/create"><Button className="shrink-0"><Plus size={16} className="mr-1" /> Nova</Button></Link>
      </div>
      <Card>
        {error ? (
          <LoadError message={error} onRetry={() => setReloadKey((k) => k + 1)} />
        ) : items.length === 0 ? <p className="text-gray-500 text-center py-4">Nenhuma notificação.</p> : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead><tr className="border-b text-left"><th className="py-2 whitespace-nowrap">Título</th><th className="py-2 whitespace-nowrap">Mensagem</th><th className="py-2 whitespace-nowrap">Tipo</th><th className="py-2 whitespace-nowrap">Data</th></tr></thead>
              <tbody>
                {items.map((n) => (
                  <tr key={n.id} className="border-b">
                    <td className="py-2 font-medium whitespace-nowrap">{n.titulo}</td>
                    <td className="py-2 text-gray-500 truncate max-w-xs">{n.mensagem}</td>
                    <td className="py-2 whitespace-nowrap"><Badge>{n.tipo}</Badge></td>
                    <td className="py-2 text-gray-400 whitespace-nowrap">{formatDate(n.createdAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <Pagination page={page} pageSize={10} total={total} onPageChange={setPage} />
      </Card>
    </div>
  );
}
