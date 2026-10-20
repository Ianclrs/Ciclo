import { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Card } from '../../components/Card';
import { StatusBadge } from '../../components/StatusBadge';
import { Button } from '../../components/Button';
import { Modal } from '../../components/Modal';
import { Input } from '../../components/Input';
import { ArrowLeft } from 'lucide-react';
import * as api from '../../api/enrollments';
import type { Enrollment } from '../../types';

export default function EnrollmentDetail() {
  const { id } = useParams();
  const [enrollment, setEnrollment] = useState<Enrollment | null>(null);
  const [showReject, setShowReject] = useState(false);
  const [motivo, setMotivo] = useState('');
  const carried = enrollment?.carriedDocuments ?? [];
  const missing = enrollment?.missingDocumentTypes ?? [];

  useEffect(() => {
    if (id) api.getEnrollment(id).then(setEnrollment).catch(() => toast.error('Erro.'));
  }, [id]);

  const handleApprove = async () => {
    if (!id) return;
    try { await api.approveEnrollment(id); toast.success('Aprovada!'); setEnrollment(await api.getEnrollment(id)); }
    catch { toast.error('Erro.'); }
  };
  const handleReject = async () => {
    if (!id) return;
    try { await api.rejectEnrollment(id, motivo); toast.success('Rejeitada.'); setShowReject(false); setEnrollment(await api.getEnrollment(id)); }
    catch { toast.error('Erro.'); }
  };

  if (!enrollment) return <p>Carregando...</p>;

  return (
    <div>
      <Link to="/admin/enrollments" className="flex items-center gap-1 text-sm text-gray-500 mb-4"><ArrowLeft size={16} /> Voltar</Link>
      <h2 className="text-2xl font-bold text-gray-900 mb-6">Detalhes da Matrícula</h2>
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Card title="Informações">
          <dl className="space-y-2 text-sm">
            <div className="flex justify-between"><dt className="text-gray-500">Aluno:</dt><dd>{enrollment.studentName}</dd></div>
            <div className="flex justify-between"><dt className="text-gray-500">Período:</dt><dd>{enrollment.periodName}</dd></div>
            <div className="flex justify-between"><dt className="text-gray-500">Status:</dt><dd><StatusBadge status={enrollment.status} variant={enrollment.status === 'Aprovado' ? 'success' : 'warning'} /></dd></div>
            {enrollment.motivoRejeicao && <div className="flex justify-between"><dt className="text-gray-500">Motivo:</dt><dd className="text-red-600">{enrollment.motivoRejeicao}</dd></div>}
            <div className="flex justify-between"><dt className="text-gray-500">Data:</dt><dd>{new Date(enrollment.createdAt).toLocaleDateString()}</dd></div>
            {enrollment.approvedAt && <div className="flex justify-between"><dt className="text-gray-500">Aprovada em:</dt><dd>{new Date(enrollment.approvedAt).toLocaleDateString()}</dd></div>}
          </dl>
        </Card>
        {/* A4 passou a devolver a situação documental no detalhe; sem esta exibição o dado não
            chegava ao usuário — e é ele que explica POR QUE a matrícula está em documentação
            pendente ou por que a aprovação será recusada. */}
        <Card title="Situação documental">
          {missing.length > 0 ? (
            <div className="mb-4">
              <p className="text-sm font-medium text-amber-700">Documentos obrigatórios pendentes</p>
              <ul className="mt-1 space-y-1">
                {missing.map((nome) => (
                  <li key={nome} className="text-sm text-amber-700">• {nome}</li>
                ))}
              </ul>
            </div>
          ) : (
            <p className="text-sm text-green-700 mb-4">Documentação obrigatória em dia.</p>
          )}

          {carried.length > 0 && (
            <>
              <p className="text-sm font-medium text-gray-700">Documentos do aluno</p>
              <ul className="mt-1 space-y-1">
                {carried.map((doc) => (
                  <li key={doc.documentId} className="flex justify-between gap-3 text-sm">
                    <span className="text-gray-700">{doc.documentType}</span>
                    <span className={doc.isExpired ? 'text-red-600' : 'text-gray-500'}>
                      {doc.isExpired
                        ? 'vencido — precisa reenviar'
                        : doc.dataValidade
                          ? `válido até ${new Date(doc.dataValidade).toLocaleDateString()}`
                          : 'sem validade'}
                    </span>
                  </li>
                ))}
              </ul>
            </>
          )}

          {carried.length === 0 && missing.length === 0 && (
            <p className="text-sm text-gray-500">Nenhum documento registrado para este aluno.</p>
          )}
        </Card>
        {enrollment.status === 'Pendente' && (
          <Card title="Ações">
            <div className="flex gap-3">
              <Button onClick={handleApprove}>Aprovar</Button>
              <Button variant="danger" onClick={() => setShowReject(true)}>Rejeitar</Button>
            </div>
          </Card>
        )}
      </div>
      <Modal open={showReject} onClose={() => setShowReject(false)} title="Rejeitar Matrícula" onConfirm={handleReject} confirmLabel="Rejeitar" confirmVariant="danger">
        <Input label="Motivo da rejeição" value={motivo} onChange={(e) => setMotivo(e.target.value)} />
      </Modal>
    </div>
  );
}
