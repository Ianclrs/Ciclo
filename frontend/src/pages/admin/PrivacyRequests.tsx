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
import type { MessageKey } from '../../i18n/messages';
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
const TYPE_LABEL_KEYS: Record<PrivacyRequestType, MessageKey> = {
  Export: 'priv.type.export',
  Anonymization: 'priv.type.anonymization',
  ConsentRevocation: 'priv.type.revocation',
};

const STATUS_LABEL_KEYS: Record<PrivacyRequestStatus, MessageKey> = {
  Submitted: 'priv.status.submitted',
  Approved: 'priv.status.approved',
  Rejected: 'priv.status.rejected',
  Completed: 'priv.status.completed',
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
  const { language, locale, t } = useLocale();
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
        setError(apiErrorMessage(err, t('priv.loadError'), language));
      });

    return () => {
      active = false;
    };
    // `t`/`language` entram porque a mensagem de erro é traduzida.
  }, [reloadKey, t, language]);

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
      toast.success(t(approving ? 'priv.approved' : 'priv.refused'));
      closeDecision();
      setReloadKey((key) => key + 1);
    } catch (err: unknown) {
      // A lista anterior fica na tela: o erro é da ação, não do que já foi carregado.
      toast.error(apiErrorMessage(err, t('priv.decisionError'), language));
      setDeciding(false);
    }
  };

  const handleComplete = async (request: PrivacyRequest) => {
    setBusyId(request.id);

    try {
      await completePrivacyRequest(request.id);
      toast.success(t('priv.completed'));
      setReloadKey((key) => key + 1);
    } catch (err: unknown) {
      // Bloqueio legal em anonimização chega aqui como `legal_hold_active` (409).
      toast.error(apiErrorMessage(err, t('priv.completeError'), language));
    } finally {
      setBusyId(null);
    }
  };

  const typeLabel = (type: PrivacyRequestType): string => {
    const key = TYPE_LABEL_KEYS[type];
    return key ? t(key) : type;
  };

  const columns = [
    {
      header: t('priv.column.type'),
      accessor: (request: PrivacyRequest) => typeLabel(request.type),
    },
    {
      header: t('priv.column.status'),
      accessor: (request: PrivacyRequest) => {
        const key = STATUS_LABEL_KEYS[request.status];
        return (
          <Badge variant={STATUS_VARIANTS[request.status] ?? 'default'}>
            {key ? t(key) : request.status}
          </Badge>
        );
      },
    },
    {
      header: t('priv.column.student'),
      accessor: (request: PrivacyRequest) => (
        <span title={request.subjectStudentId}>{subjectLabel(request)}</span>
      ),
    },
    {
      header: t('priv.column.legalHold'),
      accessor: (request: PrivacyRequest) => (
        <Badge variant={request.legalHold ? 'danger' : 'default'}>
          {request.legalHold ? t('priv.yes') : t('priv.no')}
        </Badge>
      ),
    },
    {
      header: t('priv.column.retainUntil'),
      accessor: (request: PrivacyRequest) =>
        request.retainUntil === null ? '—' : formatDate(request.retainUntil, locale),
    },
    {
      header: t('priv.column.submittedAt'),
      accessor: (request: PrivacyRequest) => formatDateTime(request.submittedAt, locale),
    },
    {
      header: t('priv.column.actions'),
      accessor: (request: PrivacyRequest) => {
        if (request.status === 'Submitted') {
          return (
            <div className="flex flex-wrap gap-2">
              <Button size="sm" onClick={() => openDecision(request, 'approve')}>
                {t('priv.approve')}
              </Button>
              <Button
                size="sm"
                variant="danger"
                onClick={() => openDecision(request, 'reject')}
              >
                {t('priv.reject')}
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
              {busyId === request.id ? t('priv.completing') : t('priv.complete')}
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
        title={t('nav.privacy')}
        subtitle={t('priv.requestsSubtitle')}
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
          <EmptyState message={t('priv.emptyRequests')} />
        </Card>
      ) : (
        <Card>
          <Table columns={columns} data={requests} keyExtractor={(request) => request.id} />
        </Card>
      )}

      <Modal
        open={selected !== null}
        onClose={closeDecision}
        title={t(decision === 'approve' ? 'priv.approveTitle' : 'priv.rejectTitle')}
        onConfirm={confirmDecision}
        confirmLabel={t(decision === 'approve' ? 'priv.approve' : 'priv.reject')}
        confirmVariant={decision === 'approve' ? 'primary' : 'danger'}
        confirmDisabled={deciding}
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-700">
            {selected === null
              ? ''
              : `${typeLabel(selected.type)} — ${subjectLabel(selected)}`}
          </p>

          <Input
            label={t(decision === 'approve' ? 'priv.resolutionOptional' : 'priv.reasonOptional')}
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
                {t('priv.legalHoldHint')}
              </label>

              <Input
                label={t('priv.holdUntil')}
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
