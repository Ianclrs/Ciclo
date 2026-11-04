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
import { outcomeLabelKey, selectableStudentIds } from '../../domain/renewalBatch';
import { useLocale } from '../../i18n/LocaleContext';
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
  const { language, t } = useLocale();
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
        setPeriodsError(apiErrorMessage(err, t('renewal.periodsError'), language));
        setLoadingPeriods(false);
      });
    return () => {
      active = false;
    };
  }, [reloadKey, t, language]);

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
        setPreviewError(apiErrorMessage(err, t('renewal.previewError'), language));
      });
    return () => {
      active = false;
    };
  }, [periodId, reloadKey, t, language]);

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
      toast.error(t('renewal.noSelection'));
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
      toast.success(t('renewal.batchDone'));
    } catch (err) {
      toast.error(apiErrorMessage(err, t('renewal.batchError'), language));
    } finally {
      setSubmitting(false);
    }
  };

  const eligibleCount = preview ? preview.items.filter((item) => item.eligible).length : 0;

  return (
    <div>
      <PageHeader title={t('renewal.title')} subtitle={t('renewal.subtitle')} />

      {periodsError ? (
        <LoadError message={periodsError} onRetry={reload} />
      ) : loadingPeriods ? (
        <Card tone="glass">
          <SkeletonRows rows={4} />
        </Card>
      ) : periods.length === 0 ? (
        <Card tone="glass">
          <p className="py-2 text-sm text-gray-500">{t('renewal.noPeriods')}</p>
        </Card>
      ) : (
        <div className="space-y-6">
          <Card tone="glass">
            <div className="mb-4 flex flex-wrap items-end gap-4">
              <Select
                label={t('renewal.periodLabel')}
                value={periodId}
                onChange={(event) => changePeriod(event.target.value)}
              >
                <option value="">{t('renewal.selectPeriod')}</option>
                {periods.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.nome} · {item.anoLetivo}
                  </option>
                ))}
              </Select>
              {preview && (
                <Button onClick={confirm} disabled={submitting || selected.size === 0}>
                  {submitting
                    ? t('renewal.processing')
                    : t('renewal.renewCount', { count: selected.size })}
                </Button>
              )}
            </div>

            {!periodId ? (
              <p className="py-2 text-sm text-gray-500">{t('renewal.choosePeriod')}</p>
            ) : previewError ? (
              <LoadError message={previewError} onRetry={reload} />
            ) : loadingPreview ? (
              <SkeletonRows rows={6} />
            ) : !preview || preview.items.length === 0 ? (
              <p className="py-2 text-sm text-gray-500">{t('renewal.noCandidates')}</p>
            ) : (
              <>
                <p className="mb-3 text-xs text-gray-500">
                  {t('renewal.summary', {
                    total: preview.items.length,
                    eligible: eligibleCount,
                  })}
                </p>
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b text-left">
                        <th className="py-2 whitespace-nowrap" />
                        <th className="py-2 whitespace-nowrap">{t('students.col.student')}</th>
                        <th className="py-2 whitespace-nowrap">{t('renewal.col.carried')}</th>
                        <th className="py-2 whitespace-nowrap">{t('renewal.col.pending')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {preview.items.map((item) => (
                        <tr key={item.studentId} className="border-b align-top">
                          <td className="py-2">
                            <input
                              type="checkbox"
                              aria-label={t('renewal.selectStudent', { name: item.studentName })}
                              disabled={!item.eligible}
                              checked={selected.has(item.studentId)}
                              onChange={() => toggle(item.studentId, item.eligible)}
                            />
                          </td>
                          <td className="py-2 font-medium whitespace-nowrap">
                            {item.studentName}
                            {!item.eligible && (
                              <p className="text-xs font-normal text-red-700">
                                {codeMessage(item.blockReason, t('renewal.notEligible'), language)}
                              </p>
                            )}
                          </td>
                          <td className="py-2 text-xs text-gray-600">
                            {item.carriedDocuments.length === 0 ? (
                              <span className="text-gray-400">{t('renewal.noDoc')}</span>
                            ) : (
                              <ul className="space-y-1">
                                {item.carriedDocuments.map((doc) => (
                                  <li key={doc.documentId}>
                                    {doc.documentType} ·{' '}
                                    <span
                                      className={doc.isExpired ? 'text-red-700' : 'text-green-700'}
                                    >
                                      {doc.isExpired ? t('renewal.expired') : t('renewal.reused')}
                                    </span>
                                  </li>
                                ))}
                              </ul>
                            )}
                          </td>
                          <td className="py-2 text-xs text-gray-600">
                            {item.missingDocumentTypes.length === 0 ? (
                              <span className="text-gray-400">{t('renewal.none')}</span>
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
            <Card tone="glass" title={t('renewal.batchTitle', { year: result.anoLetivo })}>
              <p className="mb-3 text-xs text-gray-500">
                {t('renewal.batchSummary', {
                  renewed: result.renewed,
                  skipped: result.skipped,
                  blocked: result.blocked,
                  failed: result.failed,
                })}
              </p>
              <ul className="divide-y text-sm">
                {result.items.map((item) => (
                  <li key={item.studentId} className="flex items-start justify-between gap-3 py-2">
                    <div>
                      <p className="font-medium">{item.studentName}</p>
                      {item.message && <p className="text-xs text-gray-500">{item.message}</p>}
                    </div>
                    <Badge variant={OUTCOME_VARIANTS[item.outcome]}>
                      {t(outcomeLabelKey(item.outcome))}
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
