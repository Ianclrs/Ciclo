import { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Card } from '../../components/Card';
import { StatusBadge } from '../../components/StatusBadge';
import { StudentAvatar } from '../../components/StudentAvatar';
import { Button } from '../../components/Button';
import { Input } from '../../components/Input';
import { ArrowLeft } from 'lucide-react';
import * as api from '../../api/students';
import type { Student } from '../../types';

export default function StudentDetail() {
  const { id } = useParams();
  const [student, setStudent] = useState<Student | null>(null);
  const [parentEmail, setParentEmail] = useState('');

  useEffect(() => {
    if (id) api.getStudent(id).then(setStudent).catch(() => toast.error('Erro ao carregar.'));
  }, [id]);

  const handleLinkParent = async () => {
    if (!parentEmail || !id) return;
    try { await api.linkParent(id, parentEmail); toast.success('Responsável vinculado!'); setParentEmail('');
      const s = await api.getStudent(id); setStudent(s); }
    catch { toast.error('Erro ao vincular.'); }
  };

  const handleUnlink = async (parentId: string) => {
    if (!id) return;
    try { await api.unlinkParent(id, parentId); toast.success('Desvinculado.'); setStudent(await api.getStudent(id)); }
    catch { toast.error('Erro.'); }
  };

  if (!student) return <p>Carregando...</p>;

  return (
    <div>
      <Link to="/admin/students" className="flex items-center gap-1 text-sm text-gray-500 hover:text-gray-700 mb-4"><ArrowLeft size={16} /> Voltar</Link>
      <div className="flex items-center gap-4 mb-6">
        <StudentAvatar name={student.nome} photo={student.foto} size="lg" />
        <h2 className="text-2xl font-bold text-gray-900">{student.nome}</h2>
      </div>
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Card title="Dados do Aluno">
          <dl className="space-y-2 text-sm">
            <div className="flex justify-between"><dt className="text-gray-500">Turma:</dt><dd className="font-medium">{student.turma}</dd></div>
            <div className="flex justify-between"><dt className="text-gray-500">Ano Letivo:</dt><dd className="font-medium">{student.anoLetivo}</dd></div>
            <div className="flex justify-between"><dt className="text-gray-500">CPF:</dt><dd className="font-medium">{student.cpf || '—'}</dd></div>
            <div className="flex justify-between"><dt className="text-gray-500">Status:</dt><dd><StatusBadge status={student.status} variant={student.status === 'Ativo' ? 'success' : 'warning'} /></dd></div>
            <div className="flex justify-between"><dt className="text-gray-500">Observações:</dt><dd className="font-medium">{student.observacoes || '—'}</dd></div>
          </dl>
        </Card>
        <Card title="Responsáveis">
          {student.parents.length === 0 && <p className="text-sm text-gray-500 mb-4">Nenhum responsável vinculado.</p>}
          <ul className="space-y-2 mb-4">
            {student.parents.map((p) => (
              <li key={p.parentId} className="flex items-center justify-between text-sm py-1">
                <span>{p.parentName} <span className="text-gray-400">({p.parentEmail})</span></span>
                <Button variant="ghost" size="sm" onClick={() => handleUnlink(p.parentId)}>Remover</Button>
              </li>
            ))}
          </ul>
          <div className="flex gap-2">
            <Input placeholder="Email do responsável" value={parentEmail} onChange={(e) => setParentEmail(e.target.value)} />
            <Button size="sm" onClick={handleLinkParent}>Vincular</Button>
          </div>
        </Card>
      </div>
    </div>
  );
}
