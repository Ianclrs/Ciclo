import { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Card } from '../../components/Card';
import { StatusBadge } from '../../components/StatusBadge';
import { StudentAvatar } from '../../components/StudentAvatar';
import { Button } from '../../components/Button';
import { ArrowLeft, FileText } from 'lucide-react';
import * as api from '../../api/parent';
import type { ChildDetail } from '../../types';

export default function ChildDetail() {
  const { id } = useParams();
  const [child, setChild] = useState<ChildDetail | null>(null);
  const [tab, setTab] = useState<'info' | 'docs' | 'grades'>('info');

  useEffect(() => { if (id) api.getChildDetail(id).then(setChild).catch(() => {}); }, [id]);
  if (!child) return <p>Carregando...</p>;

  return (
    <div>
      <Link to="/parent" className="flex items-center gap-1 text-sm text-gray-500 mb-4"><ArrowLeft size={16} /> Voltar</Link>
      <div className="flex items-center gap-4 mb-6">
        <StudentAvatar name={child.student.nome} photo={child.student.foto} size="lg" />
        <h2 className="text-2xl font-bold text-gray-900">{child.student.nome}</h2>
      </div>

      <div className="flex flex-wrap gap-2 mb-6">
        {(['info', 'docs', 'grades'] as const).map((t) => (
          <Button key={t} variant={tab === t ? 'primary' : 'secondary'} size="sm" onClick={() => setTab(t)}>
            {t === 'info' ? 'Informações' : t === 'docs' ? 'Documentos' : 'Notas'}
          </Button>
        ))}
      </div>

      {tab === 'info' && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <Card title="Dados">
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between"><dt className="text-gray-500">Turma:</dt><dd>{child.student.turma}</dd></div>
              <div className="flex justify-between"><dt className="text-gray-500">Ano:</dt><dd>{child.student.anoLetivo}</dd></div>
              <div className="flex justify-between"><dt className="text-gray-500">Status:</dt><dd><StatusBadge status={child.student.status} variant={child.student.status === 'Ativo' ? 'success' : 'warning'} /></dd></div>
            </dl>
          </Card>
          <Card title="Matrícula Atual">
            {child.currentEnrollment ? (
              <dl className="space-y-2 text-sm">
                <div className="flex justify-between"><dt className="text-gray-500">Período:</dt><dd>{child.currentEnrollment.periodName}</dd></div>
                <div className="flex justify-between"><dt className="text-gray-500">Status:</dt><dd><StatusBadge status={child.currentEnrollment.status} variant={child.currentEnrollment.status === 'Aprovado' ? 'success' : 'warning'} /></dd></div>
              </dl>
            ) : <p className="text-gray-500">Nenhuma matrícula ativa.</p>}
          </Card>
        </div>
      )}

      {tab === 'docs' && (
        <Card title={`Documentos (${child.documents.length})`}>
          {child.documents.length === 0 ? <p className="text-gray-500">Nenhum documento.</p> : (
            <ul className="divide-y text-sm">
              {child.documents.map((d) => (
                <li key={d.id} className="py-3 flex items-center justify-between">
                  <div><p className="font-medium">{d.nomeArquivo}</p><p className="text-xs text-gray-500">{d.documentTypeName} • {new Date(d.createdAt).toLocaleDateString()}</p></div>
                  <StatusBadge status={d.status} variant={d.status === 'Aprovado' ? 'success' : d.status === 'Pendente' ? 'warning' : 'danger'} />
                </li>
              ))}
            </ul>
          )}
          <Link to={`/parent/children/${id}/documents`} className="mt-4 inline-flex items-center gap-1 text-sm text-indigo-600 hover:underline"><FileText size={14} /> Fazer upload de documento</Link>
        </Card>
      )}

      {tab === 'grades' && (
        <Card title="Notas">
          {child.grades.length === 0 ? <p className="text-gray-500">Nenhuma nota disponível.</p> : (
            <ul className="divide-y text-sm">{child.grades.map((g, i) => <li key={i} className="py-2 flex justify-between"><span>{g.disciplina}</span><span className="font-medium">{g.nota ?? '—'}</span></li>)}</ul>
          )}
        </Card>
      )}
    </div>
  );
}
