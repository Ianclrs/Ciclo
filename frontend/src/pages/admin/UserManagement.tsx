import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { UserPlus } from 'lucide-react';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { Modal } from '../../components/Modal';
import { LoadError } from '../../components/LoadError';
import * as api from '../../api/users';
import { apiErrorMessage } from '../../api/errors';
import { useAuth } from '../../hooks/useAuth';
import {
  INVITE_ROLES,
  activationLabel,
  canChangeRole,
  roleLabel,
  validateInvite,
  type InviteRole,
} from '../../domain/userAdmin';
import type { ManagedRole, ManagedUser } from '../../types';

/**
 * M11 — gestão de usuários do colégio.
 *
 * A rota vive em `/admin/*`, que também aceita Staff; a autorização real é da API (Admin-only).
 * Aqui a tela apenas não oferece o que o papel não pode fazer — e o item de menu só aparece
 * para Admin (App.tsx).
 */
export default function UserManagement() {
  const { user: current } = useAuth();
  const [users, setUsers] = useState<ManagedUser[]>([]);
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

  useEffect(() => {
    let active = true;
    api
      .getUsers()
      .then((list) => {
        if (!active) return;
        setUsers(list);
        setError(null);
      })
      .catch((err) => {
        if (!active) return;
        setError(apiErrorMessage(err, 'Não foi possível carregar os usuários.'));
      });
    return () => {
      active = false;
    };
  }, [reloadKey]);

  if (current?.role !== 'Admin') {
    return (
      <Card tone="glass">
        <p className="text-gray-600 text-center py-4">
          Acesso restrito ao administrador do colégio.
        </p>
      </Card>
    );
  }

  const reload = () => setReloadKey((key) => key + 1);

  const handleInvite = async () => {
    const problem = validateInvite(form);
    if (problem) {
      toast.error(problem);
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
      toast.error(apiErrorMessage(err, 'Não foi possível convidar o usuário.'));
    } finally {
      setBusy(false);
    }
  };

  const handleRole = async (target: ManagedUser, role: ManagedRole) => {
    if (role === target.role) return;

    try {
      await api.changeUserRole(target.id, role);
      toast.success(`Papel de ${target.name} alterado para ${roleLabel(role)}.`);
      reload();
    } catch (err) {
      toast.error(apiErrorMessage(err, 'Não foi possível alterar o papel.'));
    }
  };

  const handleActive = async (target: ManagedUser) => {
    try {
      await api.setUserActive(target.id, !target.isActive);
      toast.success(`${target.name} ${target.isActive ? 'desativado' : 'ativado'}.`);
      reload();
    } catch (err) {
      toast.error(apiErrorMessage(err, 'Não foi possível alterar a situação do usuário.'));
    }
  };

  return (
    <div>
      <PageHeader
        title="Usuários"
        subtitle="Quem tem acesso ao colégio e com qual papel."
        actions={
          <Button className="shrink-0" onClick={() => setInviteOpen(true)}>
            <UserPlus size={16} className="mr-1" /> Convidar
          </Button>
        }
      />

      <Card tone="glass">
        {error ? (
          <LoadError message={error} onRetry={reload} />
        ) : users.length === 0 ? (
          <p className="text-gray-500 text-center py-4">Nenhum usuário cadastrado.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b text-left">
                  <th className="py-2 whitespace-nowrap">Nome</th>
                  <th className="py-2 whitespace-nowrap">E-mail</th>
                  <th className="py-2 whitespace-nowrap">Papel</th>
                  <th className="py-2 whitespace-nowrap">Situação</th>
                  <th className="py-2 whitespace-nowrap" />
                </tr>
              </thead>
              <tbody>
                {users.map((item) => {
                  const isSelf = current?.id === item.id;
                  return (
                    <tr key={item.id} className="border-b">
                      <td className="py-2 font-medium whitespace-nowrap">
                        {item.name}
                        {isSelf && <span className="ml-2 text-xs text-gray-400">(você)</span>}
                      </td>
                      <td className="py-2 text-gray-500">{item.email}</td>
                      <td className="py-2">
                        <Select
                          value={item.role}
                          disabled={!canChangeRole(current?.id ?? '', item.id)}
                          onChange={(event) => handleRole(item, event.target.value as ManagedRole)}
                        >
                          <option value="Admin">Administração</option>
                          <option value="Staff">Secretaria</option>
                          <option value="Parent">Responsável</option>
                        </Select>
                      </td>
                      <td className="py-2 space-x-2 whitespace-nowrap">
                        <Badge variant={item.isActive ? 'success' : 'danger'}>
                          {item.isActive ? 'Ativo' : 'Inativo'}
                        </Badge>
                        {!item.emailConfirmed && <Badge variant="warning">E-mail não confirmado</Badge>}
                      </td>
                      <td className="py-2 text-right">
                        {!isSelf && (
                          <Button variant="secondary" onClick={() => handleActive(item)}>
                            {activationLabel(item.isActive)}
                          </Button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Modal
        open={inviteOpen}
        onClose={() => setInviteOpen(false)}
        title="Convidar usuário"
        confirmLabel="Convidar"
        confirmDisabled={busy || validateInvite(form) !== null}
        onConfirm={handleInvite}
      >
        <div className="space-y-4">
          <Input
            label="Nome"
            value={form.name}
            onChange={(event) => setForm({ ...form, name: event.target.value })}
          />
          <Input
            label="E-mail"
            type="email"
            value={form.email}
            onChange={(event) => setForm({ ...form, email: event.target.value })}
          />
          <Select
            label="Papel"
            value={form.role}
            onChange={(event) => setForm({ ...form, role: event.target.value as InviteRole })}
          >
            {INVITE_ROLES.map((role) => (
              <option key={role} value={role}>{roleLabel(role)}</option>
            ))}
          </Select>
          <p className="text-xs text-gray-500">
            A conta nasce sem senha: o usuário só passa a entrar depois de aceitar o convite.
          </p>
        </div>
      </Modal>

      <Modal
        open={inviteResult !== null}
        onClose={() => setInviteResult(null)}
        title="Convite criado"
      >
        <p className="mb-3">
          Envie as instruções para <strong>{inviteResult?.user.email}</strong>. O usuário define a
          senha pelo fluxo de recuperação, usando o token abaixo.
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
                  toast.success('Token copiado.');
                } catch {
                  toast.error('Não foi possível copiar — selecione o texto manualmente.');
                }
              }}
            >
              Copiar token
            </Button>
          </div>
        ) : (
          <p className="text-xs text-gray-500">
            O token não é exibido neste ambiente: ele deve ser entregue por e-mail ao convidado.
          </p>
        )}
      </Modal>
    </div>
  );
}
