import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Card } from '../../components/Card';
import { StatusBadge } from '../../components/StatusBadge';
import { StudentAvatar } from '../../components/StudentAvatar';
import { Button } from '../../components/Button';
import { Pagination } from '../../components/Pagination';
import { Modal } from '../../components/Modal';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { Table } from '../../components/Table';
import * as api from '../../api/enrollments';
import type { Enrollment, EnrollmentPeriod } from '../../types';

const PAGE_SIZE = 10;

const STATUS_OPTIONS = ['Pendente', 'Aprovado', 'Rejeitado'];

interface Query {
  page: number;
  periodId: string;
  status: string;
}

const INITIAL_QUERY: Query = { page: 1, periodId: '', status: '' };

function statusVariant(status: string) {
  if (status === 'Aprovado') return 'success';
  if (status === 'Pendente') return 'warning';
  if (status === 'Rejeitado') return 'danger';
  return 'info';
}

export default function EnrollmentList() {
  const [enrollments, setEnrollments] = useState<Enrollment[]>([]);
  const [periods, setPeriods] = useState<EnrollmentPeriod[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [query, setQuery] = useState<Query>(INITIAL_QUERY);
  const [reloadKey, setReloadKey] = useState(0);
  const [rejectId, setRejectId] = useState('');
  const [motivo, setMotivo] = useState('');

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        const res = await api.getEnrollments({
          periodId: query.periodId || undefined,
          status: query.status || undefined,
          page: query.page,
          pageSize: PAGE_SIZE,
        });
        if (cancelled) return;
        setEnrollments(res.items);
        setTotal(res.total);
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
  }, [query, reloadKey]);

  // Períodos mudam raramente: buscados uma vez, fora do ciclo de paginação.
  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        const list = await api.getEnrollmentPeriods();
        if (!cancelled) setPeriods(list);
      } catch {
        if (!cancelled) toast.error('Não foi possível carregar os períodos.');
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  const applyQuery = (next: Query) => {
    setLoading(true);
    setQuery(next);
  };

  const refresh = () => {
    setLoading(true);
    setReloadKey((key) => key + 1);
  };

  const handleApprove = async (id: string) => {
    try {
      await api.approveEnrollment(id);
      toast.success('Matrícula aprovada.');
      refresh();
    } catch {
      toast.error('Não foi possível aprovar a matrícula.');
    }
  };

  const handleReject = async () => {
    try {
      await api.rejectEnrollment(rejectId, motivo);
      toast.success('Matrícula rejeitada.');
      setRejectId('');
      setMotivo('');
      refresh();
    } catch {
      toast.error('Não foi possível rejeitar a matrícula.');
    }
  };

  const columns = [
    {
      header: '',
      className: 'w-16 pl-4 text-center',
      // Coluna própria só para a foto: a largura fixa garante que todos os avatares
      // fiquem na mesma linha vertical, independentemente do tamanho do nome.
      accessor: (enrollment: Enrollment) => (
        <StudentAvatar name={enrollment.studentName} photo={enrollment.studentFoto} />
      ),
    },
    {
      header: 'Aluno',
      className: 'text-center',
      accessor: (enrollment: Enrollment) => (
        <div className="min-w-0">
          <Link
            to={`/admin/enrollments/${enrollment.id}`}
            className="font-medium text-gray-900 hover:text-indigo-600"
          >
            {enrollment.studentName}
          </Link>
          <p className="text-xs text-gray-500 mt-0.5">{enrollment.periodName}</p>
        </div>
      ),
    },
    {
      header: 'Status',
      className: 'text-center',
      accessor: (enrollment: Enrollment) => (
        <StatusBadge status={enrollment.status} variant={statusVariant(enrollment.status)} />
      ),
    },
    {
      header: '',
      className: 'text-right',
      accessor: (enrollment: Enrollment) =>
        enrollment.status === 'Pendente' ? (
          <div className="flex justify-end gap-1">
            <Button variant="ghost" size="sm" onClick={() => handleApprove(enrollment.id)}>
              Aprovar
            </Button>
            <Button
              variant="ghost"
              size="sm"
              className="text-red-600 hover:bg-red-50"
              onClick={() => {
                setMotivo('');
                setRejectId(enrollment.id);
              }}
            >
              Rejeitar
            </Button>
          </div>
        ) : null,
    },
  ];

  const hasFilters = query.periodId !== '' || query.status !== '';

  return (
    <div>
      <div className="mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Matrículas</h2>
        <p className="text-sm text-gray-500 mt-1">Acompanhe e aprove as matrículas por período.</p>
      </div>

      <Card>
        <div className="flex flex-wrap items-end gap-3 mb-5">
          <Select
            label="Período"
            className="min-w-[210px]"
            value={query.periodId}
            onChange={(event) => applyQuery({ ...query, page: 1, periodId: event.target.value })}
          >
            <option value="">Todos os períodos</option>
            {periods.map((period) => (
              <option key={period.id} value={period.id}>
                {period.nome} ({period.anoLetivo})
              </option>
            ))}
          </Select>

          <Select
            label="Status"
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

          {hasFilters && (
            <Button type="button" variant="ghost" onClick={() => applyQuery(INITIAL_QUERY)}>
              Limpar
            </Button>
          )}
        </div>

        <Table
          columns={columns}
          data={enrollments}
          keyExtractor={(enrollment) => enrollment.id}
          loading={loading}
          emptyMessage={
            failed
              ? 'Não foi possível carregar as matrículas. Tente novamente.'
              : 'Nenhuma matrícula encontrada com estes filtros.'
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
        open={!!rejectId}
        onClose={() => {
          setRejectId('');
          setMotivo('');
        }}
        title="Rejeitar matrícula"
        onConfirm={handleReject}
        confirmLabel="Rejeitar"
        confirmVariant="danger"
        confirmDisabled={motivo.trim() === ''}
      >
        <Input
          label="Motivo da rejeição"
          value={motivo}
          onChange={(event) => setMotivo(event.target.value)}
        />
      </Modal>
    </div>
  );
}
