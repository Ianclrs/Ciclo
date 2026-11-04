import { useEffect, useMemo, useState } from 'react';
import toast from 'react-hot-toast';
import { UserPlus } from 'lucide-react';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { Modal } from '../../components/Modal';
import { Table } from '../../components/Table';
import { Pagination } from '../../components/Pagination';
import { StudentAvatar } from '../../components/StudentAvatar';
import { LoadError } from '../../components/LoadError';
import { SkeletonRows } from '../../components/Skeleton';
import { EmptyState } from '../../components/EmptyState';
import * as api from '../../api/users';
import { apiErrorMessage } from '../../api/errors';
import { useAuth } from '../../hooks/useAuth';
import {
  INVITE_ROLES,
  activationLabelKey,
  canChangeRole,
  roleLabelKey,
  validateInvite,
  type InviteRole,
} from '../../domain/userAdmin';
import { useLocale } from '../../i18n/LocaleContext';
import type { MessageKey } from '../../i18n/messages';
import { matches } from '../../utils/search';
import type { ManagedRole, ManagedUser } from '../../types';

/** Papéis que a API aceita na troca de papel, na ordem de exibição. */
const ROLE_OPTIONS: ManagedRole[] = ['Admin', 'Staff', 'Parent'];

const PAGE_SIZE = 8;

type StatusFilter = '' | 'active' | 'inactive';

/**
 * M11 — gestão de usuários do colégio.
 *
 * A rota vive em `/admin/*`, que também aceita Staff; a autorização real é da API (Admin-only).
 * Aqui a tela apenas não oferece o que o papel não pode fazer — e o item de menu só aparece
 * para Admin (App.tsx).
 *
 * Estrutura da tela: resumo numérico → filtros → tabela → paginação. Antes eram só cinco
 * colunas de tabela com um `select` de papel em cada linha: qualquer lista longa virava uma
 * parede de campos de formulário, sem busca e sem indicação de quantos usuários existem. A
 * troca de papel agora é uma ação explícita (modal), não um controle permanentemente aberto.
 */
export default function UserManagement() {
  const { user: current } = useAuth();
  const { language, t } = useLocale();

  /** Papel desconhecido aparece como veio: só os conhecidos têm texto no catálogo. */
  const roleText = (role: string) => {
    const key = roleLabelKey(role);
    return key ? t(key) : role;
  };

  const [users, setUsers] = useState<ManagedUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [busy, setBusy] = useState(false);

  const [inviteOpen, setInviteOpen] = useState(false);
  const [form, setForm] = useState<{ name: string; email: string; role: InviteRole }>({
    name: '',
    email: '',
    role: 'Staff',
  });
  const [inviteResult, setInviteResult] = useState<api.InviteResult | null>(null);

  const [search, setSearch] = useState('');
  const [roleFilter, setRoleFilter] = useState<'' | ManagedRole>('');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('');
  const [page, setPage] = useState(1);

  const [roleTarget, setRoleTarget] = useState<ManagedUser | null>(null);
  const [roleDraft, setRoleDraft] = useState<ManagedRole>('Staff');
  const [deactivateTarget, setDeactivateTarget] = useState<ManagedUser | null>(null);

  useEffect(() => {
    let active = true;
    api
      .getUsers()
      .then((list) => {
        if (!active) return;
        setUsers(list);
        setError(null);
        setLoading(false);
      })
      .catch((err) => {
        if (!active) return;
        setError(apiErrorMessage(err, t('users.loadError'), language));
        setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [reloadKey, t, language]);

  /** Resumo da lista inteira — os filtros não mudam estes números. */
  const stats = useMemo<{ key: MessageKey; value: number }[]>(
    () => [
      { key: 'users.stats.total', value: users.length },
      { key: 'users.stats.admins', value: users.filter((item) => item.role === 'Admin').length },
      {
        key: 'users.stats.unconfirmed',
        value: users.filter((item) => !item.emailConfirmed).length,
      },
      { key: 'users.stats.inactive', value: users.filter((item) => !item.isActive).length },
    ],
    [users],
  );

  const filtered = useMemo(
    () =>
      users.filter((item) => {
        if (roleFilter !== '' && item.role !== roleFilter) return false;
        if (statusFilter === 'active' && !item.isActive) return false;
        if (statusFilter === 'inactive' && item.isActive) return false;
        return matches(`${item.name} ${item.email}`, search);
      }),
    [users, roleFilter, statusFilter, search],
  );

  // A página pode ficar fora de faixa depois de filtrar: o corte usa o valor corrigido.
  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const currentPage = Math.min(page, totalPages);
  const visible = filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);
  const hasFilters = search.trim() !== '' || roleFilter !== '' || statusFilter !== '';

  function clearFilters(): void {
    setSearch('');
    setRoleFilter('');
    setStatusFilter('');
    setPage(1);
  }

  if (current?.role !== 'Admin') {
    return (
      <Card tone="glass">
        <p className="text-gray-600 text-center py-4">{t('users.restricted')}</p>
      </Card>
    );
  }

  const reload = () => {
    setLoading(true);
    setReloadKey((key) => key + 1);
  };

  const handleInvite = async () => {
    const problem = validateInvite(form);
    if (problem) {
      toast.error(t(problem));
      return;
    }

    setBusy(true);
    try {
      const result = await api.inviteUser({ ...form, role: form.role as ManagedRole });
      setInviteOpen(false);
      setForm({ name: '', email: '', role: 'Staff' });
      setInviteResult(result);
      reload();
    } catch (err) {
      toast.error(apiErrorMessage(err, t('users.inviteError'), language));
    } finally {
      setBusy(false);
    }
  };

  const handleRole = async (target: ManagedUser, role: ManagedRole) => {
    if (role === target.role) return;

    try {
      await api.changeUserRole(target.id, role);
      toast.success(t('user.roleChanged', { name: target.name, role: roleText(role) }));
      reload();
    } catch (err) {
      toast.error(apiErrorMessage(err, t('users.roleError'), language));
    }
  };

  const handleActive = async (target: ManagedUser) => {
    try {
      await api.setUserActive(target.id, !target.isActive);
      toast.success(
        t(target.isActive ? 'users.deactivated' : 'users.activated', { name: target.name }),
      );
      reload();
    } catch (err) {
      toast.error(apiErrorMessage(err, t('users.activeError'), language));
    }
  };

  function openRole(item: ManagedUser): void {
    setRoleTarget(item);
    setRoleDraft(item.role);
  }

  const columns = [
    {
      header: t('users.col.user'),
      accessor: (item: ManagedUser) => (
        <div className="flex min-w-0 items-center gap-3">
          {/* Mesmo avatar de iniciais das outras listas: nome sem foto não vira texto solto. */}
          <StudentAvatar name={item.name} size="sm" />
          <div className="min-w-0">
            <p className="flex items-center gap-2 font-medium text-gray-900">
              <span className="truncate">{item.name}</span>
              {item.id === current?.id && <Badge variant="default">{t('users.you')}</Badge>}
            </p>
            <p className="truncate text-xs text-gray-500">{item.email}</p>
          </div>
        </div>
      ),
    },
    {
      header: t('field.role'),
      className: 'text-center',
      accessor: (item: ManagedUser) => (
        <Badge variant={item.role === 'Admin' ? 'info' : 'default'}>{roleText(item.role)}</Badge>
      ),
    },
    {
      header: t('users.col.status'),
      className: 'text-center',
      accessor: (item: ManagedUser) => (
        <div className="flex flex-wrap items-center justify-center gap-1">
          <Badge variant={item.isActive ? 'success' : 'danger'}>
            {item.isActive ? t('users.active') : t('users.inactive')}
          </Badge>
          {!item.emailConfirmed && (
            <Badge variant="warning">{t('users.emailNotConfirmed')}</Badge>
          )}
        </div>
      ),
    },
    {
      header: t('users.col.actions'),
      className: 'text-right',
      accessor: (item: ManagedUser) => {
        // Regra de domínio: ninguém altera o próprio papel nem se desativa por esta tela.
        if (!canChangeRole(current?.id ?? '', item.id)) {
          return <span className="text-gray-400">—</span>;
        }

        return (
          <div className="flex justify-end gap-1">
            <Button variant="ghost" size="sm" onClick={() => openRole(item)}>
              {t('users.changeRoleTitle')}
            </Button>
            <Button
              variant="ghost"
              size="sm"
              className={item.isActive ? 'text-red-600 hover:bg-red-50' : ''}
              // Ativar é reversível e imediato; desativar bloqueia acesso e pede confirmação.
              onClick={() => (item.isActive ? setDeactivateTarget(item) : handleActive(item))}
            >
              {t(activationLabelKey(item.isActive))}
            </Button>
          </div>
        );
      },
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('nav.users')}
        subtitle={t('users.subtitle')}
        actions={
          <Button className="shrink-0" onClick={() => setInviteOpen(true)}>
            <UserPlus size={16} className="mr-1" /> {t('users.invite')}
          </Button>
        }
      />

      <Card tone="glass">
        {users.length > 0 && !loading && !error && (
          <>
            <div className="grid grid-cols-2 gap-4 border-b border-gray-200/70 pb-5 sm:grid-cols-4">
              {stats.map((stat) => (
                <div key={stat.key}>
                  <p className="text-xs font-medium uppercase tracking-wide text-gray-500">
                    {t(stat.key)}
                  </p>
                  <p className="mt-1 text-2xl font-semibold text-gray-900">{stat.value}</p>
                </div>
              ))}
            </div>

            <div className="flex flex-wrap items-end gap-3 py-5">
              <div className="min-w-[220px] flex-1">
                <Input
                  aria-label={t('users.searchPlaceholder')}
                  placeholder={t('users.searchPlaceholder')}
                  value={search}
                  onChange={(event) => {
                    setSearch(event.target.value);
                    setPage(1);
                  }}
                />
              </div>

              <Select
                aria-label={t('field.role')}
                className="min-w-[170px]"
                value={roleFilter}
                onChange={(event) => {
                  setRoleFilter(event.target.value as '' | ManagedRole);
                  setPage(1);
                }}
              >
                <option value="">{t('users.allRoles')}</option>
                {ROLE_OPTIONS.map((role) => (
                  <option key={role} value={role}>{roleText(role)}</option>
                ))}
              </Select>

              <Select
                aria-label={t('users.col.status')}
                className="min-w-[170px]"
                value={statusFilter}
                onChange={(event) => {
                  setStatusFilter(event.target.value as StatusFilter);
                  setPage(1);
                }}
              >
                <option value="">{t('users.allStatuses')}</option>
                <option value="active">{t('users.active')}</option>
                <option value="inactive">{t('users.inactive')}</option>
              </Select>

              {hasFilters && (
                <Button type="button" variant="ghost" onClick={clearFilters}>
                  {t('common.clear')}
                </Button>
              )}
            </div>
          </>
        )}

        {error ? (
          <LoadError message={error} onRetry={reload} />
        ) : loading ? (
          /* Sem estas linhas a tela mostrava "Nenhum usuário cadastrado" e trocava pela lista
             quando a API respondia — a piscada de quem abre uma seção ainda sem dados. */
          <SkeletonRows rows={PAGE_SIZE} />
        ) : users.length === 0 ? (
          <EmptyState message={t('users.empty')} />
        ) : (
          <>
            <Table
              columns={columns}
              data={visible}
              keyExtractor={(item) => item.id}
              emptyMessage={t('users.emptyFiltered')}
            />
            <Pagination
              page={currentPage}
              pageSize={PAGE_SIZE}
              total={filtered.length}
              onPageChange={setPage}
            />
          </>
        )}
      </Card>

      <Modal
        open={inviteOpen}
        onClose={() => setInviteOpen(false)}
        title={t('users.inviteTitle')}
        confirmLabel={t('users.inviteConfirm')}
        confirmDisabled={busy || validateInvite(form) !== null}
        onConfirm={handleInvite}
      >
        <div className="space-y-4">
          <Input
            label={t('field.name')}
            value={form.name}
            onChange={(event) => setForm({ ...form, name: event.target.value })}
          />
          <Input
            label={t('field.email')}
            type="email"
            value={form.email}
            onChange={(event) => setForm({ ...form, email: event.target.value })}
          />
          <Select
            label={t('field.role')}
            value={form.role}
            onChange={(event) => setForm({ ...form, role: event.target.value as InviteRole })}
          >
            {INVITE_ROLES.map((role) => (
              <option key={role} value={role}>{roleText(role)}</option>
            ))}
          </Select>
          <p className="text-xs text-gray-500">{t('users.inviteHint')}</p>
        </div>
      </Modal>

      <Modal
        open={roleTarget !== null}
        onClose={() => setRoleTarget(null)}
        title={t('users.changeRoleTitle')}
        confirmLabel={t('common.save')}
        confirmDisabled={busy || roleTarget === null || roleDraft === roleTarget.role}
        onConfirm={async () => {
          if (roleTarget !== null) await handleRole(roleTarget, roleDraft);
          setRoleTarget(null);
        }}
      >
        <div className="space-y-4">
          <p className="text-sm font-medium text-gray-800">{roleTarget?.name}</p>
          <Select
            label={t('field.role')}
            value={roleDraft}
            onChange={(event) => setRoleDraft(event.target.value as ManagedRole)}
          >
            {ROLE_OPTIONS.map((role) => (
              <option key={role} value={role}>{roleText(role)}</option>
            ))}
          </Select>
          <p className="text-xs text-gray-500">{t('users.changeRoleHint')}</p>
        </div>
      </Modal>

      <Modal
        open={deactivateTarget !== null}
        onClose={() => setDeactivateTarget(null)}
        title={t('users.deactivateTitle')}
        confirmLabel={t('users.deactivateConfirm')}
        confirmVariant="danger"
        onConfirm={async () => {
          if (deactivateTarget !== null) await handleActive(deactivateTarget);
          setDeactivateTarget(null);
        }}
      >
        <p className="text-sm text-gray-700">
          {t('users.deactivateBody', { name: deactivateTarget?.name ?? '' })}
        </p>
      </Modal>

      <Modal
        open={inviteResult !== null}
        onClose={() => setInviteResult(null)}
        title={t('users.inviteCreated')}
      >
        <p className="mb-3">
          {t('users.inviteCreatedBody', { email: inviteResult?.user.email ?? '' })}
        </p>
        {inviteResult?.inviteToken ? (
          <div className="space-y-2">
            <code className="block break-all rounded bg-gray-100 p-2 text-xs">
              {inviteResult.inviteToken}
            </code>
            <Button
              variant="secondary"
              onClick={async () => {
                try {
                  await navigator.clipboard.writeText(inviteResult.inviteToken!);
                  toast.success(t('users.tokenCopied'));
                } catch {
                  toast.error(t('users.copyError'));
                }
              }}
            >
              {t('users.copyToken')}
            </Button>
          </div>
        ) : (
          <p className="text-xs text-gray-500">{t('users.tokenHidden')}</p>
        )}
      </Modal>
    </div>
  );
}
