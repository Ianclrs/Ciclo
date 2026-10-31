import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { Button } from '../../components/Button';
import { Select } from '../../components/Select';
import { Badge } from '../../components/Badge';
import { LoadError } from '../../components/LoadError';
import { SkeletonRows } from '../../components/Skeleton';
import * as api from '../../api/enrollments';
import { apiErrorMessage, codeMessage } from '../../api/errors';
import { describeOutcome, selectableStudentIds } from '../../domain/renewalBatch';
import type { BadgeVariant } from '../../components/badgeStyles';
import type {
  EnrollmentPeriod,
  RenewalBatchResult,
  RenewalOutcome,
  RenewalPreview,
} from '../../types';

/**
 * Spec 160 — rematrícula em lote.
 *
 * A tela não decide elegibilidade: o backend responde `eligible` e `blockReason` por aluno, e
 * aqui só se respeita essa resposta — o checkbox nasce desabilitado para quem está bloqueado.
 * A seleção também passa por `selectableStudentIds` antes de virar requisição, para que um
 * estado inconsistente da tela não tente renovar quem o servidor já recusou.
 *
 * Desfecho é por aluno, nunca sucesso/falha global: a resposta traz um item por aluno e os
 * contadores somam exatamente a quantidade de itens.
 */

const OUTCOME_VARIANTS: Record<RenewalOutcome, BadgeVariant> = {
  Renewed: 'success',
  SkippedAlreadyExists: 'default',
  BlockedPendingDocuments: 'warning',
  Failed: 'danger',
};

export default function EnrollmentRenewal() {
  const [periods, setPeriods] = useState<EnrollmentPeriod[]>([]);
  const [loadingPeriods, setLoadingPeriods] = useState(true);
  const [periodsError, setPeriodsError] = useState<string | null>(null);

  const [periodId, setPeriodId] = useState('');
  // `undefined` = prévia ainda não pedida; o carregamento é derivado daí (ver ChildDetail).
  const [preview, setPreview] = useState<RenewalPreview | undefined>(undefined);
  const [previewError, setPreviewError] = useState<string | null>(null);

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [result, setResult] = useState<RenewalBatchResult | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let active = true;
    api
      .getEnrollmentPeriods()
      .then((list) => {
        if (!active) return;
        setPeriods(list);
        setPeriodsError(null);
        setLoadingPeriods(false);
      })
      .catch((err) => {
        if (!active) return;
        setPeriodsError(apiErrorMessage(err, 'Não foi possível carregar os períodos.'));
        setLoadingPeriods(false);
      });
    return () => {
      active = false;
    };
  }, [reloadKey]);

  useEffect(() => {
    if (!periodId) return;
    let active = true;
    api
      .getRenewalPreview(periodId)
      .then((data) => {
        if (!active) return;
        setPreview(data);
        setPreviewError(null);
      })
      .catch((err) => {
        if (!active) return;
        setPreviewError(apiErrorMessage(err, 'Não foi possível carregar a prévia.'));
      });
    return () => {
      active = false;
    };
  }, [periodId, reloadKey]);

  const loadingPreview = periodId !== '' && preview === undefined && previewError === null;

  const changePeriod = (next: string) => {
    setPeriodId(next);
    setPreview(undefined);
    setPreviewError(null);
    setResult(null);
    setSelected(new Set());
  };

  const reload = () => {
    setPeriodsError(null);
    setPreviewError(null);
    setPreview(undefined);
    setReloadKey((key) => key + 1);
  };

  const toggle = (studentId: string, eligible: boolean) => {
    if (!eligible) return;
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(studentId)) next.delete(studentId);
      else next.add(studentId);
      return next;
    });
  };

  const confirm = async () => {
    if (!preview) return;

    const chosen = preview.items.filter((item) => selected.has(item.studentId));
    const studentIds = selectableStudentIds(chosen);
    if (studentIds.length === 0) {
      // Sem seleção não há requisição: o lote não deve ser disparado "no vazio".
      toast.error('Selecione ao menos um aluno elegível.');
      return;
    }

    setSubmitting(true);
    try {
      const batch = await api.renewEnrollmentBatch({
        enrollmentPeriodId: preview.periodId,
        studentIds,
      });
      setResult(batch);
      setSelected(new Set());
      toast.success('Lote processado. Confira o desfecho de cada aluno.');
    } catch (err) {
      toast.error(apiErrorMessage(err, 'Não foi possível executar o lote.'));
    } finally {
      setSubmitting(false);
    }
  };

  const eligibleCount = preview ? preview.items.filter((item) => item.eligible).length : 0;

  return (
    <div>
      <PageHeader
        title="Rematrícula em lote"
        subtitle="Prévia por aluno, com os documentos reaproveitados antes de confirmar."
      />

      {periodsError ? (
        <LoadError message={periodsError} onRetry={reload} />
      ) : loadingPeriods ? (
        <Card tone="glass">
          <SkeletonRows rows={4} />
        </Card>
      ) : periods.length === 0 ? (
        <Card tone="glass">
          <p className="py-2 text-sm text-gray-500">
            Nenhum período de matrícula cadastrado. Crie o período do próximo ano antes de renovar.
          </p>
        </Card>
      ) : (
        <div className="space-y-6">
          <Card tone="glass">
            <div className="mb-4 flex flex-wrap items-end gap-4">
              <Select
                label="Período de destino"
                value={periodId}
                onChange={(event) => changePeriod(event.target.value)}
              >
                <option value="">Selecione um período</option>
                {periods.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.nome} · {item.anoLetivo}
                  </option>
                ))}
              </Select>
              {preview && (
                <Button onClick={confirm} disabled={submitting || selected.size === 0}>
                  {submitting ? 'Processando…' : `Renovar ${selected.size} selecionado(s)`}
                </Button>
              )}
            </div>

            {!periodId ? (
              <p className="py-2 text-sm text-gray-500">Escolha um período para ver a prévia.</p>
            ) : previewError ? (
              <LoadError message={previewError} onRetry={reload} />
            ) : loadingPreview ? (
              <SkeletonRows rows={6} />
            ) : !preview || preview.items.length === 0 ? (
              <p className="py-2 text-sm text-gray-500">
                Nenhum aluno candidato à renovação neste período.
              </p>
            ) : (
              <>
                <p className="mb-3 text-xs text-gray-500">
                  {preview.items.length} aluno(s) candidato(s) · {eligibleCount} elegível(is). A
                  prévia não grava nada: só a confirmação renova.
                </p>
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b text-left">
                        <th className="py-2 whitespace-nowrap" />
                        <th className="py-2 whitespace-nowrap">Aluno</th>
                        <th className="py-2 whitespace-nowrap">Documentos reaproveitados</th>
                        <th className="py-2 whitespace-nowrap">Pendências</th>
                      </tr>
                    </thead>
                    <tbody>
                      {preview.items.map((item) => (
                        <tr key={item.studentId} className="border-b align-top">
                          <td className="py-2">
                            <input
                              type="checkbox"
                              aria-label={`Selecionar ${item.studentName}`}
                              disabled={!item.eligible}
                              checked={selected.has(item.studentId)}
                              onChange={() => toggle(item.studentId, item.eligible)}
                            />
                          </td>
                          <td className="py-2 font-medium whitespace-nowrap">
                            {item.studentName}
                            {!item.eligible && (
                              <p className="text-xs font-normal text-red-700">
                                {codeMessage(item.blockReason, 'Não elegível para renovação.')}
                              </p>
                            )}
                          </td>
                          <td className="py-2 text-xs text-gray-600">
                            {item.carriedDocuments.length === 0 ? (
                              <span className="text-gray-400">Nenhum documento</span>
                            ) : (
                              <ul className="space-y-1">
                                {item.carriedDocuments.map((doc) => (
                                  <li key={doc.documentId}>
                                    {doc.documentType} ·{' '}
                                    <span
                                      className={doc.isExpired ? 'text-red-700' : 'text-green-700'}
                                    >
                                      {doc.isExpired ? 'vencido' : 'reaproveitado'}
                                    </span>
                                  </li>
                                ))}
                              </ul>
                            )}
                          </td>
                          <td className="py-2 text-xs text-gray-600">
                            {item.missingDocumentTypes.length === 0 ? (
                              <span className="text-gray-400">Nenhuma</span>
                            ) : (
                              item.missingDocumentTypes.join(', ')
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </>
            )}
          </Card>

          {result && (
            <Card tone="glass" title={`Desfecho do lote · ano letivo ${result.anoLetivo}`}>
              <p className="mb-3 text-xs text-gray-500">
                {result.renewed} renovado(s) · {result.skipped} já existia(m) · {result.blocked}{' '}
                bloqueado(s) · {result.failed} falha(s)
              </p>
              <ul className="divide-y text-sm">
                {result.items.map((item) => (
                  <li key={item.studentId} className="flex items-start justify-between gap-3 py-2">
                    <div>
                      <p className="font-medium">{item.studentName}</p>
                      {item.message && <p className="text-xs text-gray-500">{item.message}</p>}
                    </div>
                    <Badge variant={OUTCOME_VARIANTS[item.outcome]}>
                      {describeOutcome(item.outcome)}
                    </Badge>
                  </li>
                ))}
              </ul>
            </Card>
          )}
        </div>
      )}
    </div>
  );
}
