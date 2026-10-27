import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Button } from '../../components/Button';
import { Input } from '../../components/Input';
import { Card } from '../../components/Card';
import * as api from '../../api/notifications';
import { apiErrorMessage } from '../../api/errors';

type TargetMode = 'users' | 'broadcast' | 'student';

export default function NotificationCreate() {
  const navigate = useNavigate();
  const [titulo, setTitulo] = useState('');
  const [mensagem, setMensagem] = useState('');
  const [tipo, setTipo] = useState(0);
  const [userIds, setUserIds] = useState('');
  const [studentId, setStudentId] = useState('');
  const [mode, setMode] = useState<TargetMode>('users');

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      if (mode === 'broadcast') {
        await api.broadcastNotification({ titulo, mensagem, tipo });
        toast.success('Broadcast enviado!');
      } else if (mode === 'student') {
        await api.sendByStudent(studentId, titulo, mensagem, tipo);
        toast.success('Notificação enviada!');
      } else {
        const ids = userIds.split(',').map((s) => s.trim()).filter(Boolean);
        await api.createNotification({ titulo, mensagem, tipo, userIds: ids });
        toast.success('Notificação criada!');
      }
      navigate('/admin/notifications');
    } catch (err) { toast.error(apiErrorMessage(err, 'Não foi possível criar a notificação.')); }
  };

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-6">Nova Notificação</h2>
      <Card>
        <form onSubmit={handleSubmit} className="space-y-4 max-w-lg">
          <Input label="Título" value={titulo} onChange={(e) => setTitulo(e.target.value)} required maxLength={200} />
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Mensagem</label>
            <textarea className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500" rows={4} value={mensagem} onChange={(e) => setMensagem(e.target.value)} required maxLength={2000} />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Tipo</label>
            <select className="border rounded-lg px-3 py-2 text-sm w-full" value={tipo} onChange={(e) => setTipo(Number(e.target.value))}>
              <option value={0}>Geral</option><option value={1}>Documento Pendente</option><option value={2}>Reunião</option><option value={3}>Matrícula</option><option value={4}>Outro</option>
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Destino</label>
            <div className="flex flex-wrap gap-2">
              {(['users', 'broadcast', 'student'] as TargetMode[]).map((m) => (
                <Button key={m} variant={mode === m ? 'primary' : 'secondary'} size="sm" type="button" onClick={() => setMode(m)}>
                  {m === 'users' ? 'Usuários' : m === 'broadcast' ? 'Broadcast' : 'Por Aluno'}
                </Button>
              ))}
            </div>
          </div>
          {mode === 'users' && <Input label="IDs dos usuários (separados por vírgula)" value={userIds} onChange={(e) => setUserIds(e.target.value)} />}
          {mode === 'student' && <Input label="ID do Aluno" value={studentId} onChange={(e) => setStudentId(e.target.value)} />}
          <div className="flex gap-3">
            <Button type="submit">Enviar</Button>
            <Button variant="secondary" type="button" onClick={() => navigate('/admin/notifications')}>Cancelar</Button>
          </div>
        </form>
      </Card>
    </div>
  );
}
