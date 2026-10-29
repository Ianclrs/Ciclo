import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Send } from 'lucide-react';
import { Button } from '../../components/Button';
import { Input } from '../../components/Input';
import { Card } from '../../components/Card';
import { CardHeading } from '../../components/CardHeading';
import { StudentPicker, UserPicker } from '../../components/RecipientPicker';
import * as api from '../../api/notifications';
import { apiErrorMessage } from '../../api/errors';
import type { ManagedUser, Student } from '../../types';

type TargetMode = 'users' | 'broadcast' | 'student';

const MODES: { value: TargetMode; label: string }[] = [
  { value: 'users', label: 'Usuários' },
  { value: 'broadcast', label: 'Broadcast' },
  { value: 'student', label: 'Por aluno' },
];

export default function NotificationCreate() {
  const navigate = useNavigate();
  const [titulo, setTitulo] = useState('');
  const [mensagem, setMensagem] = useState('');
  const [tipo, setTipo] = useState(0);
  const [mode, setMode] = useState<TargetMode>('users');
  const [selectedUsers, setSelectedUsers] = useState<ManagedUser[]>([]);
  const [selectedStudent, setSelectedStudent] = useState<Student | null>(null);

  // Sem destinatário a API recusaria o envio: bloqueia antes, com o motivo à vista.
  const missingRecipient =
    (mode === 'users' && selectedUsers.length === 0) ||
    (mode === 'student' && selectedStudent === null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      if (mode === 'broadcast') {
        await api.broadcastNotification({ titulo, mensagem, tipo });
        toast.success('Broadcast enviado!');
      } else if (mode === 'student' && selectedStudent) {
        await api.sendByStudent(selectedStudent.id, titulo, mensagem, tipo);
        toast.success('Notificação enviada!');
      } else if (mode === 'users') {
        await api.createNotification({
          titulo,
          mensagem,
          tipo,
          userIds: selectedUsers.map((user) => user.id),
        });
        toast.success('Notificação criada!');
      }
      navigate('/admin/notifications');
    } catch (err) {
      toast.error(apiErrorMessage(err, 'Não foi possível criar a notificação.'));
    }
  };

  return (
    <div className="mx-auto max-w-5xl">
      <div className="mb-6">
        <h2 className="text-3xl font-bold tracking-tight text-gray-900">Nova notificação</h2>
        <p className="mt-1 text-base text-gray-700">Escolha o destino, escreva e envie.</p>
      </div>

      <Card tone="glass">
        <CardHeading icon={Send} title="Compor mensagem" />

        <form onSubmit={handleSubmit} className="space-y-6">
          {/* Duas colunas no desktop: compor à esquerda, escolher destinatário à direita.
              Empilhado, o formulário passava da altura da tela e obrigava a rolar para enviar. */}
          <div className="grid gap-6 lg:grid-cols-3">
            <div className="space-y-4 lg:col-span-2">
              <Input
                label="Título"
                value={titulo}
                onChange={(e) => setTitulo(e.target.value)}
                required
                maxLength={200}
              />

              <div>
                <label htmlFor="mensagem" className="mb-1 block text-sm font-medium text-gray-700">
                  Mensagem
                </label>
                <textarea
                  id="mensagem"
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
                  rows={6}
                  value={mensagem}
                  onChange={(e) => setMensagem(e.target.value)}
                  required
                  maxLength={2000}
                />
              </div>

              <div>
                <label htmlFor="tipo" className="mb-1 block text-sm font-medium text-gray-700">
                  Tipo
                </label>
                <select
                  id="tipo"
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm"
                  value={tipo}
                  onChange={(e) => setTipo(Number(e.target.value))}
                >
                  <option value={0}>Geral</option>
                  <option value={1}>Documento Pendente</option>
                  <option value={2}>Reunião</option>
                  <option value={3}>Matrícula</option>
                  <option value={4}>Outro</option>
                </select>
              </div>
            </div>

            <fieldset className="space-y-2 lg:col-span-1">
              <legend className="mb-2 block text-sm font-medium text-gray-700">Destino</legend>
              <div className="flex flex-wrap gap-2">
                {MODES.map((option) => (
                  <Button
                    key={option.value}
                    variant={mode === option.value ? 'primary' : 'secondary'}
                    size="sm"
                    type="button"
                    aria-pressed={mode === option.value}
                    onClick={() => setMode(option.value)}
                  >
                    {option.label}
                  </Button>
                ))}
              </div>

              {mode === 'users' && <UserPicker selected={selectedUsers} onChange={setSelectedUsers} />}
              {mode === 'student' && (
                <StudentPicker selected={selectedStudent} onChange={setSelectedStudent} />
              )}
              {mode === 'broadcast' && (
                <p className="rounded-lg border border-amber-200/80 bg-amber-50/80 px-3 py-2 text-xs text-amber-900">
                  O aviso vai para todos os usuários do colégio.
                </p>
              )}
            </fieldset>
          </div>

          <div className="flex flex-wrap items-center gap-3 border-t border-gray-200/70 pt-4">
            <Button type="submit" className="gap-2" disabled={missingRecipient}>
              <Send size={16} />
              {mode === 'broadcast' ? 'Enviar para todos' : 'Enviar'}
            </Button>
            <Button
              variant="secondary"
              type="button"
              onClick={() => navigate('/admin/notifications')}
            >
              Cancelar
            </Button>
            {missingRecipient && (
              <p className="text-xs text-gray-600">Escolha ao menos um destinatário para enviar.</p>
            )}
          </div>
        </form>
      </Card>
    </div>
  );
}
