import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { Download, FileText } from 'lucide-react';
import { Card } from '../../components/Card';
import { Badge } from '../../components/Badge';
import { uniformBadgeClass } from '../../components/badgeStyles';
import { StudentAvatar } from '../../components/StudentAvatar';
import { Button } from '../../components/Button';
import { Pagination } from '../../components/Pagination';
import { Modal } from '../../components/Modal';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { Table } from '../../components/Table';
import * as api from '../../api/documents';
import { apiErrorMessage } from '../../api/errors';
import { motivoValidationError } from '../../domain/validation';
import type { Document, DocumentType } from '../../types';

const PAGE_SIZE = 10;

/** Validade do documento; destaca em vermelho quando a data já passou. */
function Validity({ date }: { date: string | null }) {
  if (!date) return <span className="text-gray-400">—</span>;

  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const expired = new Date(date) < today;

  return (
    <span className={expired ? 'text-red-600' : 'text-gray-700'}>
      <span className={expired ? 'font-medium' : ''}>{new Date(date).toLocaleDateString('pt-BR')}</span>
      {expired && <span className="block text-xs text-red-500">vencido</span>}
    </span>
  );
}

export default function DocumentVerification() {
  const [docs, setDocs] = useState<Document[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const [verify, setVerify] = useState<{ id: string; approved: boolean } | null>(null);
  const [motivo, setMotivo] = useState('');
  const motivoError = motivo === '' ? null : motivoValidationError(motivo);
  // A6b: a visão "Todos" substitui a antiga "Pendentes" — status virou filtro. "A vencer"
  // continua separada porque tem semântica de data, não de status.
  const [view, setView] = useState<'all' | 'expiring'>('all');
  const [status, setStatus] = useState('');
  const [documentTypeId, setDocumentTypeId] = useState('');
  const [search, setSearch] = useState('');
  const [types, setTypes] = useState<DocumentType[]>([]);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        if (view === 'expiring') {
          // Lista simples: o contrato deste endpoint não é paginado.
          const expiring = await api.getExpiringDocuments();
          if (cancelled) return;
          setDocs(expiring);
          setTotal(expiring.length);
        } else {
          const res = await api.getDocuments({
            status: status || undefined,
            documentTypeId: documentTypeId || undefined,
            search: search.trim() || undefined,
            page,
            pageSize: PAGE_SIZE,
          });
          if (cancelled) return;
          setDocs(res.items);
          setTotal(res.total);
        }
        setFailed(false);
      } catch {
        if (cancelled) return;
        setFailed(true);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [page, reloadKey, view, status, documentTypeId, search]);

  // Tipos alimentam o filtro; falha aqui não impede a lista, por isso vira aviso.
  useEffect(() => {
    api
      .getDocumentTypes()
      .then(setTypes)
      .catch((err) => toast.error(apiErrorMessage(err, 'Não foi possível carregar os tipos.')));
  }, []);

  const changePage = (nextPage: number) => {
    setLoading(true);
    setPage(nextPage);
  };

  const switchView = (next: 'all' | 'expiring') => {
    setPage(1);
    setLoading(true);
    setView(next);
  };

  const refresh = () => {
    setLoading(true);
    setReloadKey((key) => key + 1);
  };

  const openVerify = (doc: Document, approved: boolean) => {
    setMotivo('');
    setVerify({ id: doc.id, approved });
  };

  const closeVerify = () => {
    setVerify(null);
    setMotivo('');
  };

  const handleVerify = async () => {
    if (!verify) return;
    try {
      await api.verifyDocument(verify.id, verify.approved, verify.approved ? undefined : motivo);
      toast.success(verify.approved ? 'Documento aprovado.' : 'Documento rejeitado.');
      closeVerify();
      refresh();
    } catch (err) {
      toast.error(apiErrorMessage(err, 'Não foi possível concluir a verificação.'));
    }
  };

  const handleDownload = async (doc: Document) => {
    try {
      const blob = await api.downloadDocument(doc.id);
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = doc.nomeArquivo;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      // B5: revogar no mesmo tick pode cancelar o download em andamento (Firefox).
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (err) {
      toast.error(apiErrorMessage(err, 'Erro ao baixar o documento.'));
    }
  };

  // Na visão filtrável o status é informação; em "A vencer" todos são aprovados e a coluna
  // só repetiria a mesma palavra.
  const statusColumn = {
    header: 'Status',
    className: 'text-center',
    accessor: (doc: Document) => (
      <Badge
        className={uniformBadgeClass}
        variant={doc.status === 'Aprovado' ? 'success' : doc.status === 'Rejeitado' ? 'danger' : 'warning'}
      >
        {doc.status}
      </Badge>
    ),
  };

  const columns = [
    {
      header: 'Arquivo',
      accessor: (doc: Document) => (
        <div className="flex items-center gap-3 min-w-0">
          <div className="p-2 rounded-lg bg-gray-100 text-gray-500 shrink-0">
            <FileText size={16} />
          </div>
          <div className="min-w-0">
            <p className="font-medium text-gray-900 truncate max-w-[260px]">{doc.nomeArquivo}</p>
            <p className="text-xs text-gray-500 mt-0.5">
              Enviado em {new Date(doc.createdAt).toLocaleDateString('pt-BR')}
            </p>
          </div>
        </div>
      ),
    },
    {
      header: '',
      className: 'w-16 pl-4 text-center',
      // Coluna própria só para a foto: a largura fixa garante que todos os avatares
      // fiquem na mesma linha vertical, independentemente do tamanho do nome.
      accessor: (doc: Document) => <StudentAvatar name={doc.studentName} photo={doc.studentFoto} />,
    },
    {
      header: 'Aluno',
      className: 'text-center',
      accessor: (doc: Document) => (
        <span className="text-gray-700">{doc.studentName}</span>
      ),
    },
    {
      header: 'Tipo',
      className: 'text-center',
      accessor: (doc: Document) => <Badge className={uniformBadgeClass}>{doc.documentTypeName}</Badge>,
    },
    ...(view === 'all' ? [statusColumn] : []),
    {
      header: 'Validade',
      className: 'text-center',
      accessor: (doc: Document) => <Validity date={doc.dataValidade} />,
    },
    {
      header: '',
      className: 'text-right',
      accessor: (doc: Document) => (
        <div className="flex items-center justify-end gap-1">
          <button
            type="button"
            onClick={() => handleDownload(doc)}
            aria-label={`Baixar ${doc.nomeArquivo}`}
            title="Baixar arquivo"
            className="p-2 rounded-lg text-gray-500 hover:bg-gray-100 hover:text-indigo-600 transition-colors"
          >
            <Download size={16} />
          </button>
          {/* Aprovar/rejeitar só para documento pendente. */}
          {doc.status === 'Pendente' && (
            <>
              <Button variant="ghost" size="sm" onClick={() => openVerify(doc, true)}>
                Aprovar
              </Button>
              <Button
                variant="ghost"
                size="sm"
                className="text-red-600 hover:bg-red-50"
                onClick={() => openVerify(doc, false)}
              >
                Rejeitar
              </Button>
            </>
          )}
        </div>
      ),
    },
  ];

  return (
    <div>
      <div className="mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Documentos</h2>
        <p className="text-sm text-gray-500 mt-1">
          {view === 'all'
            ? 'Documentos de todos os alunos, com filtros.'
            : 'Documentos aprovados que vencem nos próximos 30 dias.'}
        </p>
        <div className="flex gap-2 mt-3">
          <Button
            variant={view === 'all' ? 'primary' : 'ghost'}
            size="sm"
            onClick={() => switchView('all')}
          >
            Todos
          </Button>
          <Button
            variant={view === 'expiring' ? 'primary' : 'ghost'}
            size="sm"
            onClick={() => switchView('expiring')}
          >
            A vencer
          </Button>
        </div>

        {view === 'all' && (
          <div className="mt-3 flex flex-wrap items-end gap-3">
            <Input
              label="Aluno"
              placeholder="Buscar por nome"
              value={search}
              onChange={(event) => {
                setPage(1);
                setLoading(true);
                setSearch(event.target.value);
              }}
              className="max-w-xs"
            />
            <Select
              label="Tipo"
              value={documentTypeId}
              onChange={(event) => {
                setPage(1);
                setLoading(true);
                setDocumentTypeId(event.target.value);
              }}
            >
              <option value="">Todos os tipos</option>
              {types.map((type) => (
                <option key={type.id} value={type.id}>{type.nome}</option>
              ))}
            </Select>
            <Select
              label="Status"
              value={status}
              onChange={(event) => {
                setPage(1);
                setLoading(true);
                setStatus(event.target.value);
              }}
            >
              <option value="">Todos os status</option>
              <option value="Pendente">Pendente</option>
              <option value="Aprovado">Aprovado</option>
              <option value="Rejeitado">Rejeitado</option>
            </Select>
          </div>
        )}
      </div>

      <Card>
        <Table
          columns={columns}
          data={docs}
          keyExtractor={(doc) => doc.id}
          loading={loading}
          emptyMessage={
            failed
              ? 'Não foi possível carregar os documentos. Tente novamente.'
              : view === 'all'
                ? 'Nenhum documento encontrado com esses filtros.'
                : 'Nenhum documento vence nos próximos 30 dias.'
          }
        />

        <Pagination page={page} pageSize={PAGE_SIZE} total={total} onPageChange={changePage} />
      </Card>

      <Modal
        open={!!verify}
        onClose={closeVerify}
        title={verify?.approved ? 'Aprovar documento' : 'Rejeitar documento'}
        onConfirm={handleVerify}
        confirmLabel={verify?.approved ? 'Aprovar' : 'Rejeitar'}
        confirmVariant={verify?.approved ? 'primary' : 'danger'}
        confirmDisabled={!verify?.approved && (motivo.trim() === '' || motivoError !== null)}
      >
        {verify?.approved ? (
          <p>Confirmar a aprovação deste documento?</p>
        ) : (
          <Input
            label="Motivo da rejeição"
            value={motivo}
            onChange={(event) => setMotivo(event.target.value)}
          />
        )}
        {!verify?.approved && motivoError && <p className="text-xs text-red-600 mt-1">{motivoError}</p>}
      </Modal>
    </div>
  );
}
