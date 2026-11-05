import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { Check, CheckCheck, CircleCheck, CircleX, FileText, UsersRound } from 'lucide-react';
import { Card } from '../../components/Card';
import { CardHeading } from '../../components/CardHeading';
import { PageHeader } from '../../components/PageHeader';
import { StatCard } from '../../components/StatCard';
import { Button } from '../../components/Button';
import { Select } from '../../components/Select';
import { Badge } from '../../components/Badge';
import { Table } from '../../components/Table';
import { EmptyState } from '../../components/EmptyState';
import { LoadError } from '../../components/LoadError';
import { SkeletonRows } from '../../components/Skeleton';
import * as api from '../../api/enrollments';
import { apiErrorMessage, codeMessage } from '../../api/errors';
import { outcomeLabelKey, selectableStudentIds } from '../../domain/renewalBatch';
import { share } from '../../utils/format';
import { useLocale } from '../../i18n/LocaleContext';
import type { BadgeVariant } from '../../components/badgeStyles';
import type {
  EnrollmentPeriod,
  RenewalBatchResult,
  RenewalOutcome,
  RenewalPreview,
  RenewalPreviewItem,
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
 *
 * Apresentação: três decisões separadas em três blocos — **qual período**, **quem renovar** e
 * **o que aconteceu**. Antes tudo vivia num cartão só, com o botão de disparar o lote ao lado do
 * seletor de período; a leitura não dizia em que etapa o usuário estava.
 */

const OUTCOME_VARIANTS: Record<RenewalOutcome, BadgeVariant> = {
  Renewed: 'success',
  SkippedAlreadyExists: 'default',
  BlockedPendingDocuments: 'warning',
  Failed: 'danger',
};

const CHECKBOX_CLASS = 'h-4 w-4 rounded border-gray-300 accent-indigo-600';

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

      // A aprovação promove o aluno, e promovido ele sai da prévia. Recarregar mantém a tabela
      // coerente com o que acabou de acontecer, em vez de deixar renovados ainda selecionáveis.
      setPreview(undefined);
      setReloadKey((key) => key + 1);
    } catch (err) {
      toast.error(apiErrorMessage(err, t('renewal.batchError'), language));
    } finally {
      setSubmitting(false);
    }
  };

  const items = preview?.items ?? [];
  const total = items.length;
  const eligibleItems = items.filter((item) => item.eligible);
  const eligibleCount = eligibleItems.length;
  const blockedCount = total - eligibleCount;
  const eligibleIds = eligibleItems.map((item) => item.studentId);
  const allSelected = eligibleIds.length > 0 && eligibleIds.every((id) => selected.has(id));
  const someSelected = eligibleIds.some((id) => selected.has(id));

  const carriedAll = items.reduce((sum, item) => sum + item.carriedDocuments.length, 0);
  const carriedValid = items.reduce(
    (sum, item) => sum + item.carriedDocuments.filter((doc) => !doc.isExpired).length,
    0,
  );

  const columns = [
    {
      // Cabeçalho é o "selecionar todos": em um lote de 30 alunos, marcar um a um é o que
      // fazia a tela ser abandonada. `indeterminate` mostra a seleção parcial.
      header: (
        <input
          type="checkbox"
          aria-label={t('renewal.selectAll')}
          className={CHECKBOX_CLASS}
          checked={allSelected}
          ref={(element) => {
            if (element) element.indeterminate = someSelected && !allSelected;
          }}
          onChange={() => setSelected(allSelected ? new Set() : new Set(eligibleIds))}
        />
      ),
      className: 'w-12',
      accessor: (item: RenewalPreviewItem) => (
        <input
          type="checkbox"
          aria-label={t('renewal.selectStudent', { name: item.studentName })}
          className={CHECKBOX_CLASS}
          disabled={!item.eligible}
          checked={selected.has(item.studentId)}
          onChange={() => toggle(item.studentId, item.eligible)}
        />
      ),
    },
    {
      header: t('students.col.student'),
      accessor: (item: RenewalPreviewItem) => (
        <div className="min-w-0">
          <p className="whitespace-nowrap font-medium text-gray-900">{item.studentName}</p>
          {!item.eligible && (
            <p className="mt-0.5 text-xs text-amber-700">
              {codeMessage(item.blockReason, t('renewal.notEligible'), language)}
            </p>
          )}
        </div>
      ),
    },
    {
      header: t('renewal.col.carried'),
      accessor: (item: RenewalPreviewItem) =>
        item.carriedDocuments.length === 0 ? (
          <span className="text-xs text-gray-400">{t('renewal.noDoc')}</span>
        ) : (
          <ul className="space-y-1 text-xs">
            {item.carriedDocuments.map((doc) => (
              <li key={doc.documentId} className="flex items-center gap-1.5">
                <Check
                  size={13}
                  aria-hidden="true"
                  className={doc.isExpired ? 'text-red-500' : 'text-green-600'}
                />
                <span className="text-gray-700">{doc.documentType}</span>
                <span className={doc.isExpired ? 'text-red-600' : 'text-green-700'}>
                  {doc.isExpired ? t('renewal.expired') : t('renewal.reused')}
                </span>
              </li>
            ))}
          </ul>
        ),
    },
    {
      header: t('renewal.col.pending'),
      // Folga mínima à esquerda. A coluna anterior não tem padding e o rótulo encostava nela:
      // medido no app, 0 px entre o fim do conteúdo anterior e o início de "Pendências".
      className: 'pl-4',
      accessor: (item: RenewalPreviewItem) =>
        item.missingDocumentTypes.length === 0 ? (
          <span className="text-xs text-gray-400">{t('renewal.none')}</span>
        ) : (
          <div className="flex flex-wrap gap-1">
            {item.missingDocumentTypes.map((name) => (
              <Badge key={name} variant="warning">
                {name}
              </Badge>
            ))}
          </div>
        ),
    },
  ];

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
          <EmptyState message={t('renewal.noPeriods')} />
        </Card>
      ) : (
        <>
          <Card tone="glass" className="mb-6">
            <div className="flex flex-wrap items-end gap-4">
              <div className="min-w-[240px] flex-1 sm:max-w-sm">
                <Select
                  label={t('renewal.periodLabel')}
                  className="w-full"
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
              </div>
            </div>
          </Card>

          {!periodId ? (
            <Card tone="glass">
              <EmptyState message={t('renewal.choosePeriod')} />
            </Card>
          ) : previewError ? (
            <LoadError message={previewError} onRetry={reload} />
          ) : loadingPreview ? (
            <Card tone="glass">
              <SkeletonRows rows={6} />
            </Card>
          ) : !preview || total === 0 ? (
            <Card tone="glass">
              <EmptyState message={t('renewal.noCandidates')} />
            </Card>
          ) : (
            <>
              <div className="mb-6 grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-4">
                <StatCard
                  label={t('renewal.kpi.students')}
                  value={total}
                  icon={UsersRound}
                  accent="violet"
                  detail={preview.periodName}
                />
                <StatCard
                  label={t('renewal.kpi.eligible')}
                  value={eligibleCount}
                  icon={CircleCheck}
                  accent="green"
                  detail={t('renewal.kpi.ofTotal', { count: eligibleCount, total })}
                  ratio={share(eligibleCount, total)}
                />
                <StatCard
                  label={t('renewal.kpi.blocked')}
                  value={blockedCount}
                  icon={CircleX}
                  accent="amber"
                  detail={t('renewal.kpi.ofTotal', { count: blockedCount, total })}
                  ratio={share(blockedCount, total)}
                />
                <StatCard
                  label={t('renewal.kpi.carried')}
                  value={carriedValid}
                  icon={FileText}
                  accent="sky"
                  detail={t('renewal.kpi.ofTotal', { count: carriedValid, total: carriedAll })}
                  ratio={share(carriedValid, carriedAll)}
                />
              </div>

              <Card tone="glass" className={result ? 'mb-6' : ''}>
                <CardHeading
                  icon={UsersRound}
                  title={t('renewal.reviewTitle')}
                  accent="violet"
                  action={
                    <div className="flex flex-wrap items-center justify-end gap-3">
                      <span className="rounded-full bg-white/80 px-3 py-1 text-xs font-medium text-gray-600 ring-1 ring-inset ring-gray-200">
                        {t('renewal.selectedCount', { count: selected.size })}
                      </span>
                      <Button onClick={confirm} disabled={submitting || selected.size === 0}>
                        {submitting
                          ? t('renewal.processing')
                          : t('renewal.renewCount', { count: selected.size })}
                      </Button>
                    </div>
                  }
                />

                <Table
                  columns={columns}
                  data={items}
                  keyExtractor={(item) => item.studentId}
                  emptyMessage={t('renewal.noCandidates')}
                />

                <p className="mt-4 text-xs text-gray-500">
                  {t('renewal.summary', { total, eligible: eligibleCount })}
                </p>
              </Card>

              {result && (
                <Card tone="glass">
                  <CardHeading
                    icon={CheckCheck}
                    title={t('renewal.batchTitle', { year: result.anoLetivo })}
                    accent="green"
                  />

                  <div className="mb-4 flex flex-wrap gap-2">
                    <Badge variant="success">{`${t('renewal.outcome.renewed')}: ${result.renewed}`}</Badge>
                    <Badge variant="default">{`${t('renewal.outcome.skipped')}: ${result.skipped}`}</Badge>
                    <Badge variant="warning">{`${t('renewal.outcome.blocked')}: ${result.blocked}`}</Badge>
                    <Badge variant="danger">{`${t('renewal.outcome.failed')}: ${result.failed}`}</Badge>
                  </div>

                  <ul className="space-y-1">
                    {result.items.map((item) => (
                      <li
                        key={item.studentId}
                        className="flex items-start justify-between gap-3 rounded-xl px-2 py-2.5 transition-colors hover:bg-white/60"
                      >
                        <div className="min-w-0">
                          <p className="truncate text-sm font-semibold text-gray-900">
                            {item.studentName}
                          </p>
                          {item.message && (
                            <p className="mt-0.5 text-xs text-gray-500">
                              {codeMessage(item.message, item.message, language)}
                            </p>
                          )}
                        </div>
                        <Badge variant={OUTCOME_VARIANTS[item.outcome]}>
                          {t(outcomeLabelKey(item.outcome))}
                        </Badge>
                      </li>
                    ))}
                  </ul>
                </Card>
              )}
            </>
          )}
        </>
      )}
    </div>
  );
}
