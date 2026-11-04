import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { Download, FileText, Search } from 'lucide-react';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { Badge } from '../../components/Badge';
import { uniformBadgeClass } from '../../components/badgeStyles';
import { StudentAvatar } from '../../components/StudentAvatar';
import { Button } from '../../components/Button';
import { Pagination } from '../../components/Pagination';
import { Modal } from '../../components/Modal';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { Table } from '../../components/Table';
import { EmptyState } from '../../components/EmptyState';
import { LoadError } from '../../components/LoadError';
import { formatDate } from '../../utils/format';
import * as api from '../../api/documents';
import { apiErrorMessage } from '../../api/errors';
import { statusLabelKey } from '../../domain/statusLabels';
import { motivoValidationError } from '../../domain/validation';
import { useLocale } from '../../i18n/LocaleContext';
import type { Document, DocumentType } from '../../types';

const PAGE_SIZE = 10;

/**
 * Status que a API devolve e aceita no filtro. O rótulo vem do dono único do vocabulário
 * (`domain/statusLabels.ts`), o mesmo usado pela etiqueta de status das outras telas.
 */
const DOCUMENT_STATUSES = ['Pendente', 'Aprovado', 'Rejeitado'] as const;

/** Validade do documento; destaca em vermelho quando a data já passou. */
function Validity({ date }: { date: string | null }) {
  const { locale, t } = useLocale();
  if (!date) return <span className="text-gray-400">—</span>;

  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const expired = new Date(date) < today;

  return (
    <span className={expired ? 'text-red-600' : 'text-gray-700'}>
      <span className={expired ? 'font-medium' : ''}>{formatDate(date, locale)}</span>
      {expired && <span className="block text-xs text-red-500">{t('renewal.expired')}</span>}
    </span>
  );
}

export default function DocumentVerification() {
  const { language, locale, t } = useLocale();
  const [docs, setDocs] = useState<Document[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const [verify, setVerify] = useState<{ id: string; approved: boolean } | null>(null);
  const [motivo, setMotivo] = useState('');
  const motivoError = motivo === '' ? null : motivoValidationError(motivo, t);
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
      .catch((err) => toast.error(apiErrorMessage(err, t('docs.typesError'), language)));
  }, [t, language]);

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
      toast.success(t(verify.approved ? 'docs.approved' : 'docs.rejected'));
      closeVerify();
      refresh();
    } catch (err) {
      toast.error(apiErrorMessage(err, t('docs.verifyError'), language));
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
      toast.error(apiErrorMessage(err, t('docs.downloadError'), language));
    }
  };

  // Na visão filtrável o status é informação; em "A vencer" todos são aprovados e a coluna
  // só repetiria a mesma palavra.
  const statusColumn = {
    header: t('common.status'),
    className: 'text-center',
    accessor: (doc: Document) => {
      const key = statusLabelKey(doc.status);
      return (
        <Badge
          className={uniformBadgeClass}
          variant={doc.status === 'Aprovado' ? 'success' : doc.status === 'Rejeitado' ? 'danger' : 'warning'}
        >
          {key ? t(key) : doc.status}
        </Badge>
      );
    },
  };

  const columns = [
    {
      header: t('docs.col.file'),
      accessor: (doc: Document) => (
        <div className="flex items-center gap-3 min-w-0">
          <div className="p-2 rounded-lg bg-gray-100 text-gray-500 shrink-0">
            <FileText size={16} />
          </div>
          <div className="min-w-0">
            <p className="font-medium text-gray-900 truncate max-w-[260px]">{doc.nomeArquivo}</p>
            <p className="text-xs text-gray-500 mt-0.5">
              {t('docs.sentAt', { date: formatDate(doc.createdAt, locale) })}
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
      header: t('students.col.student'),
      className: 'text-center',
      accessor: (doc: Document) => (
        <span className="text-gray-700">{doc.studentName}</span>
      ),
    },
    {
      header: t('docs.field.type'),
      className: 'text-center',
      accessor: (doc: Document) => <Badge className={uniformBadgeClass}>{doc.documentTypeName}</Badge>,
    },
    ...(view === 'all' ? [statusColumn] : []),
    {
      header: t('docs.col.validity'),
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
            aria-label={t('docs.downloadAria', { name: doc.nomeArquivo })}
            title={t('docs.downloadTitle')}
            className="p-2 rounded-lg text-gray-500 hover:bg-gray-100 hover:text-indigo-600 transition-colors"
          >
            <Download size={16} />
          </button>
          {/* Aprovar/rejeitar só para documento pendente. */}
          {doc.status === 'Pendente' && (
            <>
              <Button variant="ghost" size="sm" onClick={() => openVerify(doc, true)}>
                {t('enrollment.action.approve')}
              </Button>
              <Button
                variant="ghost"
                size="sm"
                className="text-red-600 hover:bg-red-50"
                onClick={() => openVerify(doc, false)}
              >
                {t('enrollment.action.reject')}
              </Button>
            </>
          )}
        </div>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('nav.documents')}
        subtitle={t(view === 'all' ? 'docs.subtitle.all' : 'docs.subtitle.expiring')}
        actions={
          <>
            <Button
              variant={view === 'all' ? 'primary' : 'secondary'}
              size="sm"
              aria-pressed={view === 'all'}
              onClick={() => switchView('all')}
            >
              {t('docs.view.all')}
            </Button>
            <Button
              variant={view === 'expiring' ? 'primary' : 'secondary'}
              size="sm"
              aria-pressed={view === 'expiring'}
              onClick={() => switchView('expiring')}
            >
              {t('docs.view.expiring')}
            </Button>
          </>
        }
      />

      <Card tone="glass">
        {view === 'all' && (
          <div className="mb-5 flex flex-wrap items-end gap-3">
            <div className="min-w-[260px] flex-1 sm:max-w-md">
              <label htmlFor="filtro-aluno" className="mb-1 block text-sm font-medium text-gray-700">
                {t('students.col.student')}
              </label>
              <div className="relative">
                <Search
                  size={16}
                  aria-hidden="true"
                  className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-gray-400"
                />
                <input
                  id="filtro-aluno"
                  type="search"
                  placeholder={t('docs.filter.studentPlaceholder')}
                  value={search}
                  onChange={(event) => {
                    setPage(1);
                    setLoading(true);
                    setSearch(event.target.value);
                  }}
                  className="w-full rounded-lg border border-gray-300 bg-white py-2 pl-9 pr-3 text-sm focus:border-transparent focus:outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>
            </div>
            <Select
              label={t('docs.field.type')}
              value={documentTypeId}
              onChange={(event) => {
                setPage(1);
                setLoading(true);
                setDocumentTypeId(event.target.value);
              }}
            >
              <option value="">{t('docs.filter.allTypes')}</option>
              {types.map((type) => (
                <option key={type.id} value={type.id}>{type.nome}</option>
              ))}
            </Select>
            <Select
              label={t('common.status')}
              value={status}
              onChange={(event) => {
                setPage(1);
                setLoading(true);
                setStatus(event.target.value);
              }}
            >
              <option value="">{t('docs.filter.allStatus')}</option>
              {DOCUMENT_STATUSES.map((option) => {
                const key = statusLabelKey(option);
                return (
                  <option key={option} value={option}>{key ? t(key) : option}</option>
                );
              })}
            </Select>
          </div>
        )}

        {failed ? (
          <LoadError message={t('docs.loadListError')} onRetry={refresh} />
        ) : loading || docs.length > 0 ? (
          <Table
            columns={columns}
            data={docs}
            keyExtractor={(doc) => doc.id}
            loading={loading}
            loadingRows={PAGE_SIZE}
          />
        ) : view === 'all' ? (
          <EmptyState />
        ) : (
          <EmptyState message={t('docs.noneExpiring')} />
        )}

        <Pagination page={page} pageSize={PAGE_SIZE} total={total} onPageChange={changePage} />
      </Card>

      <Modal
        open={!!verify}
        onClose={closeVerify}
        title={t(verify?.approved ? 'docs.approveTitle' : 'docs.rejectTitle')}
        onConfirm={handleVerify}
        confirmLabel={t(verify?.approved ? 'enrollment.action.approve' : 'enrollment.action.reject')}
        confirmVariant={verify?.approved ? 'primary' : 'danger'}
        confirmDisabled={!verify?.approved && (motivo.trim() === '' || motivoError !== null)}
      >
        {verify?.approved ? (
          <p>{t('docs.confirmApprove')}</p>
        ) : (
          <Input
            label={t('enrollments.rejectReason')}
            value={motivo}
            onChange={(event) => setMotivo(event.target.value)}
          />
        )}
        {!verify?.approved && motivoError && <p className="text-xs text-red-600 mt-1">{motivoError}</p>}
      </Modal>
    </div>
  );
}
