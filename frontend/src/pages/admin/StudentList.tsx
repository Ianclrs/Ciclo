import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Plus, Search } from 'lucide-react';
import { Button } from '../../components/Button';
import { buttonClass } from '../../components/buttonStyles';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { Pagination } from '../../components/Pagination';
import { StatusBadge } from '../../components/StatusBadge';
import { StudentAvatar } from '../../components/StudentAvatar';
import { Table } from '../../components/Table';
import { EmptyState } from '../../components/EmptyState';
import { LoadError } from '../../components/LoadError';
import { Modal } from '../../components/Modal';
import { useLocale } from '../../i18n/LocaleContext';
import type { MessageKey } from '../../i18n/messages';
import * as api from '../../api/students';
import type { Student } from '../../types';

const PAGE_SIZE = 10;

interface Query {
  page: number;
  search: string;
  turma: string;
  status: string;
}

const NO_FILTERS: Query = { page: 1, search: '', turma: '', status: '' };

/**
 * Status do aluno: o `value` é o que a API persiste; o rótulo vem do catálogo, então o filtro
 * acompanha o idioma sem tocar no contrato.
 */
const STATUS_OPTIONS: ReadonlyArray<{ value: string; labelKey: MessageKey }> = [
  { value: 'Ativo', labelKey: 'student.status.active' },
  { value: 'Inativo', labelKey: 'student.status.inactive' },
  { value: 'Transferido', labelKey: 'student.status.transferred' },
];

function statusVariant(status: string) {
  if (status === 'Ativo') return 'success';
  if (status === 'Inativo') return 'warning';
  return 'info';
}

export default function StudentList() {
  const { t } = useLocale();
  const [students, setStudents] = useState<Student[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [removeId, setRemoveId] = useState('');
  // `query` é o que foi consultado; `form` é o texto ainda não enviado. Isso impede
  // que a consulta use um valor obsoleto do campo.
  const [query, setQuery] = useState<Query>(NO_FILTERS);
  const [form, setForm] = useState({ search: '', turma: '' });
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        const res = await api.getStudents({
          search: query.search || undefined,
          turma: query.turma || undefined,
          status: query.status || undefined,
          page: query.page,
          pageSize: PAGE_SIZE,
        });
        if (cancelled) return;
        setStudents(res.items);
        setTotal(res.total);
        setFailed(false);
      } catch {
        if (cancelled) return;
        setFailed(true);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    // Descarta respostas de consultas antigas (evita exibir página fora de ordem).
    return () => {
      cancelled = true;
    };
  }, [query, reloadKey]);

  /** Toda troca de filtro/página passa por aqui: o reset do carregamento é um evento, não um efeito. */
  const applyQuery = (next: Query) => {
    setLoading(true);
    setQuery(next);
  };

  const refresh = () => {
    setLoading(true);
    setReloadKey((key) => key + 1);
  };

  const handleRemove = async () => {
    try {
      await api.deleteStudent(removeId);
      toast.success(t('students.removed'));
      setRemoveId('');
      refresh();
    } catch {
      toast.error(t('students.removeError'));
    }
  };

  const hasFilters = query.search !== '' || query.turma !== '' || query.status !== '';

  const columns = [
    {
      header: '',
      className: 'w-16 pl-4 text-center',
      // Coluna própria só para a foto: a largura fixa garante que todos os avatares
      // fiquem na mesma linha vertical, independentemente do tamanho do nome.
      accessor: (student: Student) => <StudentAvatar name={student.nome} photo={student.foto} />,
    },
    {
      header: t('students.col.student'),
      className: 'text-center',
      accessor: (student: Student) => (
        <div className="min-w-0">
          <Link
            to={`/admin/students/${student.id}`}
            className="font-medium text-gray-900 hover:text-indigo-600"
          >
            {student.nome}
          </Link>
          <p className="text-xs text-gray-500 mt-0.5">
            {student.turma} · {student.anoLetivo}
          </p>
        </div>
      ),
    },
    {
      header: t('common.status'),
      className: 'text-center',
      accessor: (student: Student) => <StatusBadge status={student.status} variant={statusVariant(student.status)} />,
    },
    {
      header: '',
      className: 'text-right',
      accessor: (student: Student) => (
        <div className="flex justify-end gap-1">
          <Link to={`/admin/students/${student.id}/edit`} className={buttonClass('ghost', 'sm')}>
            {t('students.edit')}
          </Link>
          <Button
            variant="ghost"
            size="sm"
            className="text-red-600 hover:bg-red-50"
            onClick={() => setRemoveId(student.id)}
          >
            {t('common.remove')}
          </Button>
        </div>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('nav.students')}
        subtitle={t('students.subtitle')}
        actions={
          <Link to="/admin/students/new" className={buttonClass('primary', 'md', 'shrink-0')}>
            <Plus size={16} className="mr-1" /> {t('students.new')}
          </Link>
        }
      />

      <Card tone="glass">
        <form
          className="flex flex-wrap items-end gap-3 mb-5"
          onSubmit={(event) => {
            event.preventDefault();
            applyQuery({ ...query, page: 1, search: form.search, turma: form.turma });
          }}
        >
          <div className="flex-1 min-w-[200px]">
            <Input
              aria-label={t('students.searchPlaceholder')}
              placeholder={t('students.searchPlaceholder')}
              value={form.search}
              onChange={(event) => setForm({ ...form, search: event.target.value })}
            />
          </div>

          <div className="w-32">
            <Input
              aria-label={t('students.filterClass')}
              placeholder={t('students.classPlaceholder')}
              value={form.turma}
              onChange={(event) => setForm({ ...form, turma: event.target.value })}
            />
          </div>

          <Select
            aria-label={t('students.filterStatus')}
            className="min-w-[170px]"
            value={query.status}
            onChange={(event) => applyQuery({ ...query, page: 1, status: event.target.value })}
          >
            <option value="">{t('students.allStatus')}</option>
            {STATUS_OPTIONS.map((status) => (
              <option key={status.value} value={status.value}>
                {t(status.labelKey)}
              </option>
            ))}
          </Select>

          <Button type="submit" variant="secondary">
            <Search size={16} className="mr-1" /> {t('students.search')}
          </Button>

          {hasFilters && (
            <Button
              type="button"
              variant="ghost"
              onClick={() => {
                setForm({ search: '', turma: '' });
                applyQuery(NO_FILTERS);
              }}
            >
              {t('students.clear')}
            </Button>
          )}
        </form>

        {failed ? (
          <LoadError message={t('students.loadListError')} onRetry={refresh} />
        ) : loading || students.length > 0 ? (
          <Table
            columns={columns}
            data={students}
            keyExtractor={(student) => student.id}
            loading={loading}
            loadingRows={PAGE_SIZE}
          />
        ) : (
          <EmptyState message={t('students.empty')} />
        )}

        <Pagination
          page={query.page}
          pageSize={PAGE_SIZE}
          total={total}
          onPageChange={(nextPage) => applyQuery({ ...query, page: nextPage })}
        />
      </Card>

      <Modal
        open={!!removeId}
        onClose={() => setRemoveId('')}
        title={t('students.removeTitle')}
        onConfirm={handleRemove}
        confirmLabel={t('common.remove')}
        confirmVariant="danger"
      >
        <p>{t('students.removeBody')}</p>
      </Modal>
    </div>
  );
}
