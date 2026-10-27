import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Card } from '../../components/Card';
import { useAuth } from '../../hooks/useAuth';
import { StatusBadge } from '../../components/StatusBadge';
import { StudentAvatar } from '../../components/StudentAvatar';
import { Button } from '../../components/Button';
import { Pagination } from '../../components/Pagination';
import { Modal } from '../../components/Modal';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { Table } from '../../components/Table';
import * as api from '../../api/enrollments';
import { motivoValidationError } from '../../domain/validation';
import {
  ENROLLMENT_STATUSES,
  actionLabel,
  actionRequiresReason,
  actionsFor,
  statusVariant,
  type EnrollmentAction,
} from '../../domain/enrollmentStatus';
import type { Enrollment, EnrollmentPeriod } from '../../types';

const PAGE_SIZE = 10;

interface Query {
  page: number;
  periodId: string;
  status: string;
}

const INITIAL_QUERY: Query = { page: 1, periodId: '', status: '' };

export default function EnrollmentList() {
  const { user } = useAuth();
  const [enrollments, setEnrollments] = useState<Enrollment[]>([]);
  const [periods, setPeriods] = useState<EnrollmentPeriod[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [query, setQuery] = useState<Query>(INITIAL_QUERY);
  const [reloadKey, setReloadKey] = useState(0);
  const [rejectId, setRejectId] = useState('');
  const [motivo, setMotivo] = useState('');
  const [reopenId, setReopenId] = useState('');
  const motivoError = motivo === '' ? null : motivoValidationError(motivo);

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

  const ACTION_DONE: Record<EnrollmentAction, string> = {
    submit: 'Matrícula enviada.',
    approve: 'Matrícula aprovada.',
    cancel: 'Matrícula cancelada.',
    reject: 'Matrícula rejeitada.',
    reopen: 'Matrícula reaberta.',
  };

  const ACTION_FAILED: Record<EnrollmentAction, string> = {
    submit: 'Não foi possível enviar a matrícula.',
    approve: 'Não foi possível aprovar a matrícula.',
    cancel: 'Não foi possível cancelar a matrícula.',
    reject: 'Não foi possível rejeitar a matrícula.',
    reopen: 'Não foi possível reabrir a matrícula.',
  };

  const runAction = async (id: string, action: EnrollmentAction) => {
    // Rejeitar exige motivo (10 a 500 caracteres): abre o modal em vez de agir direto.
    if (actionRequiresReason(action)) {
      setMotivo('');
      setRejectId(id);
      return;
    }

    // Reabrir desfaz uma decisão já tomada: confirma antes, em vez de agir no primeiro clique.
    if (action === 'reopen') {
      setReopenId(id);
      return;
    }

    try {
      if (action === 'submit') await api.submitEnrollment(id);
      else if (action === 'approve') await api.approveEnrollment(id);
      else await api.cancelEnrollment(id);

      toast.success(ACTION_DONE[action]);
      refresh();
    } catch {
      toast.error(ACTION_FAILED[action]);
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

  const handleReopen = async () => {
    try {
      await api.reopenEnrollment(reopenId);
      toast.success(ACTION_DONE.reopen);
      setReopenId('');
      refresh();
    } catch {
      toast.error(ACTION_FAILED.reopen);
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
      // As ações vêm do estado da matrícula, não de um `if` na tela: quem sabe quais transições
      // são válidas é a máquina de estados (espelhada em domain/enrollmentStatus.ts).
      accessor: (enrollment: Enrollment) => {
        const actions = actionsFor(enrollment.status, user?.role);
        if (actions.length === 0) return null;

        return (
          <div className="flex justify-end gap-1">
            {actions.map((action) => (
              <Button
                key={action}
                variant="ghost"
                size="sm"
                className={
                  action === 'reject' || action === 'cancel' ? 'text-red-600 hover:bg-red-50' : ''
                }
                onClick={() => runAction(enrollment.id, action)}
              >
                {actionLabel(action)}
              </Button>
            ))}
          </div>
        );
      },
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
            {ENROLLMENT_STATUSES.map((status) => (
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
        confirmDisabled={motivo.trim() === '' || motivoError !== null}
      >
        <Input
          label="Motivo da rejeição"
          value={motivo}
          onChange={(event) => setMotivo(event.target.value)}
        />
        {motivoError && <p className="text-xs text-red-600 mt-1">{motivoError}</p>}
      </Modal>

      <Modal
        open={reopenId !== ''}
        onClose={() => setReopenId('')}
        title="Reabrir matrícula"
        onConfirm={handleReopen}
        confirmLabel="Reabrir"
        confirmVariant="primary"
      >
        <p className="text-sm text-gray-700">
          A matrícula volta para a fila de análise. A decisão anterior é desfeita e o registro
          guarda quem reabriu e quando.
        </p>
      </Modal>
    </div>
  );
}
