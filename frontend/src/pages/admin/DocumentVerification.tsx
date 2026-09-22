import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import axios from 'axios';
import { Card } from '../../components/Card';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Pagination } from '../../components/Pagination';
import { Modal } from '../../components/Modal';
import { Input } from '../../components/Input';
import { Table } from '../../components/Table';
import { Download } from 'lucide-react';
import * as api from '../../api/documents';
import type { Document } from '../../types';

export default function DocumentVerification() {
  const [tab, setTab] = useState<'pending' | 'all'>('pending');
  const [docs, setDocs] = useState<Document[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [verifyId, setVerifyId] = useState('');
  const [approved, setApproved] = useState(true);
  const [motivo, setMotivo] = useState('');

  const load = async (p: number) => {
    try {
      const res = tab === 'pending'
        ? await api.getPendingDocuments({ page: p, pageSize: 10 })
        : { items: [] as Document[], total: 0 };
      setDocs(res.items); setTotal(res.total); setPage(p);
    } catch { toast.error('Erro.'); }
  };
  useEffect(() => { load(1); }, [tab]);

  const handleVerify = async () => {
    try {
      await api.verifyDocument(verifyId, approved, approved ? undefined : motivo);
      toast.success(approved ? 'Aprovado!' : 'Rejeitado.');
      setVerifyId(''); load(page);
    } catch { toast.error('Erro.'); }
  };

  const handleDownload = async (doc: Document) => {
    try {
      const blob = await api.downloadDocument(doc.id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = doc.nomeArquivo;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch (err) {
      const title =
        axios.isAxiosError(err) && typeof err.response?.data?.title === 'string'
          ? err.response.data.title
          : '';
      toast.error(title === 'document_file_not_found'
        ? 'Arquivo não encontrado no servidor.'
        : 'Erro ao baixar o documento.');
    }
  };

  const columns = [
    { header: 'Arquivo', accessor: (d: Document) => (
      <div className="flex items-center gap-2">
        <span>{d.nomeArquivo}</span>
        <button type="button" onClick={() => handleDownload(d)} className="text-indigo-600 hover:text-indigo-800" title="Download"><Download size={14} /></button>
      </div>
    )},
    { header: 'Aluno', accessor: (d: Document) => d.studentName },
    { header: 'Tipo', accessor: (d: Document) => d.documentTypeName },
    { header: 'Status', accessor: (d: Document) => <Badge variant={d.status === 'Aprovado' ? 'success' : d.status === 'Pendente' ? 'warning' : 'danger'}>{d.status}</Badge> },
    { header: '', accessor: (d: Document) => d.status === 'Pendente' ? (
      <div className="flex justify-end gap-1">
        <Button variant="ghost" size="sm" onClick={() => { setVerifyId(d.id); setApproved(true); }}>Aprovar</Button>
        <Button variant="ghost" size="sm" onClick={() => { setVerifyId(d.id); setApproved(false); }}>Rejeitar</Button>
      </div>
    ) : null, className: 'text-right' },
  ];

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-6">Documentos</h2>
      <div className="flex gap-2 mb-4">
        <Button variant={tab === 'pending' ? 'primary' : 'secondary'} size="sm" onClick={() => { setTab('pending'); setPage(1); }}>Pendentes</Button>
        <Button variant={tab === 'all' ? 'primary' : 'secondary'} size="sm" onClick={() => { setTab('all'); setPage(1); }}>Todos</Button>
      </div>
      <Card>
        <Table columns={columns} data={docs} keyExtractor={(d) => d.id} emptyMessage="Nenhum documento." />
        <Pagination page={page} pageSize={10} total={total} onPageChange={load} />
      </Card>
      <Modal open={!!verifyId} onClose={() => setVerifyId('')} title={approved ? 'Aprovar Documento' : 'Rejeitar Documento'} onConfirm={handleVerify} confirmLabel={approved ? 'Aprovar' : 'Rejeitar'} confirmVariant={approved ? 'primary' : 'danger'}>
        {!approved && <Input label="Motivo da rejeição" value={motivo} onChange={(e) => setMotivo(e.target.value)} />}
        {approved && <p>Confirmar aprovação deste documento?</p>}
      </Modal>
    </div>
  );
}
