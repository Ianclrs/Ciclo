import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Plus, Search } from 'lucide-react';
import { Button } from '../../components/Button';
import { buttonClass } from '../../components/buttonStyles';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { Card } from '../../components/Card';
import { Pagination } from '../../components/Pagination';
import { StatusBadge } from '../../components/StatusBadge';
import { StudentAvatar } from '../../components/StudentAvatar';
import { Table } from '../../components/Table';
import { Modal } from '../../components/Modal';
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

const STATUS_OPTIONS = ['Ativo', 'Inativo', 'Transferido'];

function statusVariant(status: string) {
  if (status === 'Ativo') return 'success';
  if (status === 'Inativo') return 'warning';
  return 'info';
}

export default function StudentList() {
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
      toast.success('Aluno removido.');
      setRemoveId('');
      refresh();
    } catch {
      toast.error('Não foi possível remover o aluno.');
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
      header: 'Aluno',
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
      header: 'Status',
      className: 'text-center',
      accessor: (student: Student) => <StatusBadge status={student.status} variant={statusVariant(student.status)} />,
    },
    {
      header: '',
      className: 'text-right',
      accessor: (student: Student) => (
        <div className="flex justify-end gap-1">
          <Link to={`/admin/students/${student.id}/edit`} className={buttonClass('ghost', 'sm')}>
            Editar
          </Link>
          <Button
            variant="ghost"
            size="sm"
            className="text-red-600 hover:bg-red-50"
            onClick={() => setRemoveId(student.id)}
          >
            Remover
          </Button>
        </div>
      ),
    },
  ];

  return (
    <div>
      <div className="flex items-center justify-between gap-4 mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Alunos</h2>
        <Link to="/admin/students/new" className={buttonClass('primary', 'md', 'shrink-0')}>
          <Plus size={16} className="mr-1" /> Novo Aluno
        </Link>
      </div>

      <Card>
        <form
          className="flex flex-wrap items-end gap-3 mb-5"
          onSubmit={(event) => {
            event.preventDefault();
            applyQuery({ ...query, page: 1, search: form.search, turma: form.turma });
          }}
        >
          <div className="flex-1 min-w-[200px]">
            <Input
              aria-label="Buscar por nome"
              placeholder="Buscar por nome"
              value={form.search}
              onChange={(event) => setForm({ ...form, search: event.target.value })}
            />
          </div>

          <div className="w-32">
            <Input
              aria-label="Filtrar por turma"
              placeholder="Turma"
              value={form.turma}
              onChange={(event) => setForm({ ...form, turma: event.target.value })}
            />
          </div>

          <Select
            aria-label="Filtrar por status"
            className="min-w-[170px]"
            value={query.status}
            onChange={(event) => applyQuery({ ...query, page: 1, status: event.target.value })}
          >
            <option value="">Todos os status</option>
            {STATUS_OPTIONS.map((status) => (
              <option key={status} value={status}>
                {status}
              </option>
            ))}
          </Select>

          <Button type="submit" variant="secondary">
            <Search size={16} className="mr-1" /> Buscar
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
              Limpar
            </Button>
          )}
        </form>

        <Table
          columns={columns}
          data={students}
          keyExtractor={(student) => student.id}
          loading={loading}
          emptyMessage={
            failed
              ? 'Não foi possível carregar os alunos. Tente novamente.'
              : 'Nenhum aluno encontrado com estes filtros.'
          }
        />

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
        title="Remover aluno"
        onConfirm={handleRemove}
        confirmLabel="Remover"
        confirmVariant="danger"
      >
        <p>O aluno e os dados vinculados a ele serão removidos. Esta ação não pode ser desfeita.</p>
      </Modal>
    </div>
  );
}
