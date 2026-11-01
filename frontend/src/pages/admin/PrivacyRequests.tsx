import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { Table } from '../../components/Table';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Input } from '../../components/Input';
import { Modal } from '../../components/Modal';
import { EmptyState } from '../../components/EmptyState';
import { LoadError } from '../../components/LoadError';
import { apiErrorMessage } from '../../api/errors';
import { useLocale } from '../../i18n/LocaleContext';
import { formatDate, formatDateTime } from '../../utils/format';
import { getStudents } from '../../api/students';
import {
  completePrivacyRequest,
  decidePrivacyRequest,
  getPrivacyRequests,
  type PrivacyRequest,
  type PrivacyRequestStatus,
  type PrivacyRequestType,
} from '../../api/privacy';

/** Rótulos de exibição; o identificador da API continua sendo o nome do membro do enum. */
const TYPE_LABELS: Record<PrivacyRequestType, string> = {
  Export: 'Exportação',
  Anonymization: 'Anonimização',
  ConsentRevocation: 'Revogação de consentimento',
};

const STATUS_LABELS: Record<PrivacyRequestStatus, string> = {
  Submitted: 'Aguardando decisão',
  Approved: 'Aprovada',
  Rejected: 'Recusada',
  Completed: 'Concluída',
};

const STATUS_VARIANTS: Record<PrivacyRequestStatus, 'warning' | 'info' | 'danger' | 'success'> = {
  Submitted: 'warning',
  Approved: 'info',
  Rejected: 'danger',
  Completed: 'success',
};

/**
 * Tela administrativa de solicitações do titular (Spec 210, FR-006).
 *
 * A autorização real é da API: a lista exige `PrivacyRead` e decidir/concluir exige
 * `PrivacyManage`. A tela não repete a regra — apenas apresenta o que a API recusou, sem
 * descartar a lista já carregada. Recusar um pedido de anonimização com bloqueio legal
 * responde 409 e a linha continua na situação anterior.
 */
export default function PrivacyRequests() {
  const { locale } = useLocale();
  const [requests, setRequests] = useState<PrivacyRequest[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [studentNames, setStudentNames] = useState<Record<string, string>>({});
  const [busyId, setBusyId] = useState<string | null>(null);

  // Decisão em aberto no modal: o tipo escolhido define quais campos aparecem.
  const [selected, setSelected] = useState<PrivacyRequest | null>(null);
  const [decision, setDecision] = useState<'approve' | 'reject'>('approve');
  const [resolution, setResolution] = useState('');
  const [legalHold, setLegalHold] = useState(false);
  const [retainUntil, setRetainUntil] = useState('');
  const [deciding, setDeciding] = useState(false);

  useEffect(() => {
    let active = true;

    getPrivacyRequests()
      .then((rows) => {
        if (!active) return;
        setRequests(rows);
        setError(null);
      })
      .catch((err: unknown) => {
        if (!active) return;
        setError(apiErrorMessage(err, 'Não foi possível carregar as solicitações.'));
      });

    return () => {
      active = false;
    };
  }, [reloadKey]);

  // Nome do aluno é conforto de leitura: a API devolve só o id. Se esta consulta falhar, a
  // lista continua correta e a coluna mostra o id curto — nenhuma linha desaparece por isso.
  useEffect(() => {
    let active = true;

    getStudents({ pageSize: 100 })
      .then((page) => {
        if (!active) return;
        setStudentNames(Object.fromEntries(page.items.map((student) => [student.id, student.nome])));
      })
      .catch(() => {
        if (active) setStudentNames({});
      });

    return () => {
      active = false;
    };
  }, []);

  const retry = () => {
    setRequests(null);
    setReloadKey((key) => key + 1);
  };

  const subjectLabel = (request: PrivacyRequest) => {
    const name = studentNames[request.subjectStudentId];
    // Id truncado com o valor completo em `title`: um GUID inteiro quebra a coluna e não é
    // lido por ninguém, mas o valor completo precisa continuar acessível.
    return name ?? request.subjectStudentId.slice(0, 8);
  };

  const openDecision = (request: PrivacyRequest, next: 'approve' | 'reject') => {
    setSelected(request);
    setDecision(next);
    setResolution('');
    setLegalHold(false);
    setRetainUntil('');
  };

  const closeDecision = () => {
    setSelected(null);
    setDeciding(false);
  };

  const confirmDecision = async () => {
    if (selected === null) return;

    setDeciding(true);
    const approving = decision === 'approve';

    try {
      await decidePrivacyRequest(selected.id, {
        decision,
        resolution: resolution.trim() === '' ? null : resolution.trim(),
        legalHold: approving ? legalHold : false,
        retainUntil: approving && retainUntil !== '' ? retainUntil : null,
      });
      toast.success(approving ? 'Solicitação aprovada.' : 'Solicitação recusada.');
      closeDecision();
      setReloadKey((key) => key + 1);
    } catch (err: unknown) {
      // A lista anterior fica na tela: o erro é da ação, não do que já foi carregado.
      toast.error(apiErrorMessage(err, 'Não foi possível registrar a decisão.'));
      setDeciding(false);
    }
  };

  const handleComplete = async (request: PrivacyRequest) => {
    setBusyId(request.id);

    try {
      await completePrivacyRequest(request.id);
      toast.success('Solicitação concluída.');
      setReloadKey((key) => key + 1);
    } catch (err: unknown) {
      // Bloqueio legal em anonimização chega aqui como `legal_hold_active` (409).
      toast.error(apiErrorMessage(err, 'Não foi possível concluir a solicitação.'));
    } finally {
      setBusyId(null);
    }
  };

  const columns = [
    {
      header: 'Tipo',
      accessor: (request: PrivacyRequest) => TYPE_LABELS[request.type] ?? request.type,
    },
    {
      header: 'Situação',
      accessor: (request: PrivacyRequest) => (
        <Badge variant={STATUS_VARIANTS[request.status] ?? 'default'}>
          {STATUS_LABELS[request.status] ?? request.status}
        </Badge>
      ),
    },
    {
      header: 'Aluno',
      accessor: (request: PrivacyRequest) => (
        <span title={request.subjectStudentId}>{subjectLabel(request)}</span>
      ),
    },
    {
      header: 'Bloqueio legal',
      accessor: (request: PrivacyRequest) => (
        <Badge variant={request.legalHold ? 'danger' : 'default'}>
          {request.legalHold ? 'Sim' : 'Não'}
        </Badge>
      ),
    },
    {
      header: 'Reter até',
      accessor: (request: PrivacyRequest) =>
        request.retainUntil === null ? '—' : formatDate(request.retainUntil, locale),
    },
    {
      header: 'Enviada em',
      accessor: (request: PrivacyRequest) => formatDateTime(request.submittedAt, locale),
    },
    {
      header: 'Ações',
      accessor: (request: PrivacyRequest) => {
        if (request.status === 'Submitted') {
          return (
            <div className="flex flex-wrap gap-2">
              <Button size="sm" onClick={() => openDecision(request, 'approve')}>
                Aprovar
              </Button>
              <Button
                size="sm"
                variant="danger"
                onClick={() => openDecision(request, 'reject')}
              >
                Recusar
              </Button>
            </div>
          );
        }

        if (request.status === 'Approved') {
          return (
            <Button
              size="sm"
              variant="secondary"
              onClick={() => handleComplete(request)}
              disabled={busyId === request.id}
            >
              {busyId === request.id ? 'Concluindo…' : 'Concluir'}
            </Button>
          );
        }

        return '—';
      },
    },
  ];

  return (
    <div>
      <PageHeader
        title="Privacidade"
        subtitle="Solicitações de titulares, decisões e execução do colégio."
      />

      {error !== null ? (
        <LoadError message={error} onRetry={retry} />
      ) : requests === null ? (
        <Card>
          <Table
            columns={columns}
            data={[]}
            keyExtractor={(request) => request.id}
            loading
            loadingRows={5}
          />
        </Card>
      ) : requests.length === 0 ? (
        <Card>
          <EmptyState message="Nenhuma solicitação registrada." />
        </Card>
      ) : (
        <Card>
          <Table columns={columns} data={requests} keyExtractor={(request) => request.id} />
        </Card>
      )}

      <Modal
        open={selected !== null}
        onClose={closeDecision}
        title={decision === 'approve' ? 'Aprovar solicitação' : 'Recusar solicitação'}
        onConfirm={confirmDecision}
        confirmLabel={decision === 'approve' ? 'Aprovar' : 'Recusar'}
        confirmVariant={decision === 'approve' ? 'primary' : 'danger'}
        confirmDisabled={deciding}
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-700">
            {selected === null
              ? ''
              : `${TYPE_LABELS[selected.type] ?? selected.type} — ${subjectLabel(selected)}`}
          </p>

          <Input
            label={decision === 'approve' ? 'Parecer (opcional)' : 'Motivo da recusa (opcional)'}
            value={resolution}
            onChange={(event) => setResolution(event.target.value)}
            maxLength={500}
          />

          {decision === 'approve' && (
            <>
              <label className="flex items-center gap-2 text-sm text-gray-700">
                <input
                  type="checkbox"
                  checked={legalHold}
                  onChange={(event) => setLegalHold(event.target.checked)}
                />
                Bloqueio legal: impede a anonimização até a liberação
              </label>

              <Input
                label="Reter até (opcional)"
                type="date"
                value={retainUntil}
                onChange={(event) => setRetainUntil(event.target.value)}
              />
            </>
          )}
        </div>
      </Modal>
    </div>
  );
}
