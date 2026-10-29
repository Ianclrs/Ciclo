import { useEffect, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Bell } from 'lucide-react';
import { getCurrentTenant } from '../api/tenants';
import { getUnreadCount } from '../api/notifications';
import type { TenantSummary } from '../types';
import { isDemoMode } from '../mocks/mode';

interface TopbarActionsProps {
  /** Rota de notificações do perfil logado (admin e responsável têm caminhos distintos). */
  notificationsTo: string;
}

/**
 * Ações da topbar: identificação do colégio e acesso às notificações.
 *
 * O nome do colégio vem do próprio recurso de tenant, não do usuário: é dado da escola e
 * apareceria repetido em toda listagem de usuários se morasse no UserDto.
 */
export function TopbarActions({ notificationsTo }: TopbarActionsProps) {
  const [tenant, setTenant] = useState<TenantSummary | null>(null);
  const [unread, setUnread] = useState(0);
  const { pathname } = useLocation();

  useEffect(() => {
    let cancelled = false;

    getCurrentTenant()
      .then((result) => {
        if (!cancelled) setTenant(result);
      })
      .catch(() => {
        // Sem o nome, a topbar continua utilizável — o chip simplesmente não aparece.
        if (!cancelled) setTenant(null);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  // Reconsulta a cada troca de rota: ler ou marcar notificações muda o contador.
  useEffect(() => {
    let cancelled = false;

    getUnreadCount()
      .then((result) => {
        if (!cancelled) setUnread(result.count);
      })
      .catch(() => {
        // Contador é informação acessória: falhar não pode esconder o sino.
        if (!cancelled) setUnread(0);
      });

    return () => {
      cancelled = true;
    };
  }, [pathname]);

  return (
    <div className="ml-auto flex items-center gap-2">
      {isDemoMode && (
        <span
          title="Os dados desta instalação são fictícios e ficam no seu navegador."
          className="hidden items-center rounded-full bg-amber-100/90 px-2.5 py-1 text-xs font-medium text-amber-900 ring-1 ring-inset ring-amber-200 sm:inline-flex"
        >
          demonstração
        </span>
      )}
      {tenant && (
        <span className="hidden sm:inline-flex max-w-[16rem] items-center truncate rounded-full border border-stone-300 bg-white/70 px-3 py-1 text-xs font-medium text-stone-700">
          {tenant.name}
        </span>
      )}

      <Link
        to={notificationsTo}
        aria-label={unread > 0 ? `Notificações (${unread} não lidas)` : 'Notificações'}
        title="Notificações"
        className="relative p-2 rounded-xl text-stone-700 hover:bg-stone-300/80 active:scale-95 transition-all"
      >
        <Bell size={20} />
        {unread > 0 && (
          <span className="absolute -top-0.5 -right-0.5 flex h-[18px] min-w-[18px] items-center justify-center rounded-full bg-red-600 px-1 text-[11px] font-semibold text-white">
            {unread > 99 ? '99+' : unread}
          </span>
        )}
      </Link>
    </div>
  );
}
