import { useEffect, useState } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { ArrowLeft, FileText, GraduationCap, Upload } from 'lucide-react';
import { Card } from '../../components/Card';
import { CardHeading } from '../../components/CardHeading';
import { StatCard } from '../../components/StatCard';
import { Badge } from '../../components/Badge';
import { PageHeader } from '../../components/PageHeader';
import { StatusBadge } from '../../components/StatusBadge';
import { StudentAvatar } from '../../components/StudentAvatar';
import { DocumentRow } from '../../components/DocumentRow';
import { Button } from '../../components/Button';
import { Table } from '../../components/Table';
import { EmptyState } from '../../components/EmptyState';
import { LoadError } from '../../components/LoadError';
import { PageSkeleton, SkeletonRows } from '../../components/Skeleton';
import * as api from '../../api/parent';
import { apiErrorMessage } from '../../api/errors';
import { useLocale } from '../../i18n/LocaleContext';
import { share } from '../../utils/format';
import { formatGrade, situacaoLabelKey, situacaoVariant } from '../../domain/academic';
import type { ChildDetail, ReportCard, ReportCardEntry } from '../../types';

type Tab = 'info' | 'docs' | 'grades';

/**
 * Ficha do filho para o responsável: dados, documentos e boletim.
 *
 * As três abas carregam em momentos diferentes de propósito — o boletim só é buscado quando a
 * aba é aberta (Spec 150 FR-007), porque a maioria das visitas não passa por ela.
 */
export default function ChildDetail() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { language, locale, t } = useLocale();
  const [child, setChild] = useState<ChildDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [tab, setTab] = useState<Tab>('info');
  // `undefined` = boletim ainda não pedido. O carregamento é derivado desse estado, em vez de um
  // `setLoading(true)` síncrono dentro do efeito — que dispara uma segunda renderização à toa.
  const [report, setReport] = useState<ReportCard | undefined>(undefined);
  const [reportError, setReportError] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;
    let active = true;
    api
      .getChildDetail(id)
      .then((data) => {
        if (!active) return;
        setChild(data); setError(null);
      })
      .catch((err) => {
        if (!active) return;
        setError(apiErrorMessage(err, t('students.loadError'), language));
      });
    return () => { active = false; };
  }, [id, reloadKey, t, language]);

  // O boletim só é buscado quando a aba é aberta (Spec 150 FR-007): carregá-lo junto do detalhe
  // faria toda visita à ficha pagar uma consulta que a maioria não usa.
  useEffect(() => {
    if (tab !== 'grades' || !id) return;
    let active = true;
    api
      .getChildReportCard(id)
      .then((data) => {
        if (!active) return;
        setReport(data);
        setReportError(null);
      })
      .catch((err) => {
        if (!active) return;
        setReportError(apiErrorMessage(err, t('child.reportError'), language));
      });
    return () => { active = false; };
  }, [tab, id, reloadKey, t, language]);

  const backLink = (
    <Link to="/parent" className="mb-4 flex items-center gap-1 text-sm text-gray-500">
      <ArrowLeft size={16} /> {t('common.back')}
    </Link>
  );

  if (error) {
    return (
      <div>
        {backLink}
        <LoadError message={error} onRetry={() => setReloadKey((k) => k + 1)} />
      </div>
    );
  }

  if (!child) return <PageSkeleton />;

  const reportLoading = tab === 'grades' && report === undefined && reportError === null;

  const TAB_LABELS: Record<Tab, string> = {
    info: t('enrollments.info'),
    docs: t('nav.documents'),
    grades: t('child.tab.grades'),
  };

  const componentes = report?.componentes ?? [];
  const aprovados = componentes.filter((entry) => entry.situacao === 'aprovado').length;
  const emRecuperacao = componentes.filter((entry) => entry.situacao === 'recuperacao').length;
  const reprovados = componentes.filter((entry) => entry.situacao === 'reprovado').length;

  const gradeColumns = [
    {
      header: t('child.col.component'),
      accessor: (entry: ReportCardEntry) => (
        <span className="font-medium text-gray-900">{entry.componente}</span>
      ),
    },
    {
      header: t('child.col.regular'),
      className: 'text-center',
      accessor: (entry: ReportCardEntry) => (
        <span className="text-gray-700 tabular-nums">{formatGrade(entry.mediaRegular, locale)}</span>
      ),
    },
    {
      header: t('child.col.recovery'),
      className: 'text-center',
      accessor: (entry: ReportCardEntry) => (
        <span className="text-gray-700 tabular-nums">
          {formatGrade(entry.notaRecuperacao, locale)}
        </span>
      ),
    },
    {
      header: t('child.col.final'),
      className: 'text-center',
      accessor: (entry: ReportCardEntry) => (
        <span className="font-semibold text-gray-900 tabular-nums">
          {formatGrade(entry.mediaFinal, locale)}
        </span>
      ),
    },
    {
      header: t('users.col.status'),
      className: 'text-center',
      accessor: (entry: ReportCardEntry) => (
        <Badge variant={situacaoVariant(entry.situacao)}>
          {t(situacaoLabelKey(entry.situacao))}
        </Badge>
      ),
    },
  ];

  return (
    <div>
      {backLink}
      <PageHeader
        title={child.student.nome}
        subtitle={t('students.detail.subtitle', {
          class: child.student.turma,
          year: child.student.anoLetivo,
        })}
        leading={<StudentAvatar name={child.student.nome} photo={child.student.foto} size="lg" />}
      />

      {/* Seletor de abas: um controle só, com o estado ativo visível — antes eram três botões
          primário/secundário lado a lado, que pareciam três ações em vez de uma navegação. */}
      <div className="mb-6 inline-flex rounded-xl border border-white/60 bg-white/70 p-1 shadow-sm backdrop-blur-md">
        {(['info', 'docs', 'grades'] as const).map((tabKey) => (
          <button
            key={tabKey}
            type="button"
            aria-pressed={tab === tabKey}
            onClick={() => setTab(tabKey)}
            className={`rounded-lg px-3 py-1.5 text-sm font-medium transition-colors ${
              tab === tabKey ? 'bg-white text-gray-900 shadow-sm' : 'text-gray-600 hover:text-gray-900'
            }`}
          >
            {TAB_LABELS[tabKey]}
          </button>
        ))}
      </div>

      {tab === 'info' && (
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <Card tone="glass" title={t('child.data')}>
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between"><dt className="text-gray-500">{t('students.field.class')}:</dt><dd>{child.student.turma}</dd></div>
              <div className="flex justify-between"><dt className="text-gray-500">{t('child.field.year')}:</dt><dd>{child.student.anoLetivo}</dd></div>
              <div className="flex justify-between"><dt className="text-gray-500">{t('common.status')}:</dt><dd><StatusBadge status={child.student.status} variant={child.student.status === 'Ativo' ? 'success' : 'warning'} /></dd></div>
            </dl>
          </Card>
          <Card tone="glass" title={t('child.enrollment')}>
            {child.currentEnrollment ? (
              <dl className="space-y-2 text-sm">
                <div className="flex justify-between"><dt className="text-gray-500">{t('enrollments.field.period')}:</dt><dd>{child.currentEnrollment.periodName}</dd></div>
                <div className="flex justify-between"><dt className="text-gray-500">{t('common.status')}:</dt><dd><StatusBadge status={child.currentEnrollment.status} variant={child.currentEnrollment.status === 'Aprovado' ? 'success' : 'warning'} /></dd></div>
              </dl>
            ) : <p className="text-gray-500">{t('child.noEnrollment')}</p>}
          </Card>
        </div>
      )}

      {tab === 'docs' && (
        <Card tone="glass">
          <CardHeading
            icon={FileText}
            title={t('child.docsTitle', { count: child.documents.length })}
            accent="teal"
            action={
              <Button
                size="sm"
                variant="secondary"
                className="gap-1.5"
                onClick={() => navigate(`/parent/children/${id}/documents`)}
              >
                <Upload size={14} aria-hidden="true" />
                {t('child.uploadDoc')}
              </Button>
            }
          />

          {child.documents.length === 0 ? (
            <EmptyState message={t('docs.empty')} />
          ) : (
            <ul className="space-y-1">
              {child.documents.map((doc) => (
                <DocumentRow key={doc.id} document={doc} />
              ))}
            </ul>
          )}
        </Card>
      )}

      {tab === 'grades' && reportError && (
        <Card tone="glass">
          <LoadError
            message={reportError}
            onRetry={() => {
              // `report` volta a "não pedido": o retry mostra o esqueleto e refaz a consulta.
              setReport(undefined);
              setReportError(null);
              setReloadKey((k) => k + 1);
            }}
          />
        </Card>
      )}

      {tab === 'grades' && reportLoading && (
        <Card tone="glass">
          <SkeletonRows rows={4} />
        </Card>
      )}

      {tab === 'grades' && !reportError && !reportLoading && componentes.length === 0 && (
        <Card tone="glass">
          <EmptyState message={t('child.noGrades')} />
        </Card>
      )}

      {tab === 'grades' && !reportError && !reportLoading && componentes.length > 0 && (
        <>
          {/* Resumo do boletim: as contagens saem do próprio relatório, sem consulta extra. */}
          <div className="mb-6 grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-4">
            <StatCard
              label={t('child.kpi.components')}
              value={componentes.length}
              icon={GraduationCap}
              accent="violet"
              detail={t('child.kpi.ofTotal', {
                count: aprovados,
                total: componentes.length,
              })}
            />
            <StatCard
              label={t('academic.status.approved')}
              value={aprovados}
              icon={GraduationCap}
              accent="green"
              detail={t('child.kpi.ofTotal', { count: aprovados, total: componentes.length })}
              ratio={share(aprovados, componentes.length)}
            />
            <StatCard
              label={t('academic.status.recovery')}
              value={emRecuperacao}
              icon={GraduationCap}
              accent="amber"
              detail={t('child.kpi.ofTotal', { count: emRecuperacao, total: componentes.length })}
              ratio={share(emRecuperacao, componentes.length)}
            />
            <StatCard
              label={t('academic.status.failed')}
              value={reprovados}
              icon={GraduationCap}
              accent="red"
              detail={t('child.kpi.ofTotal', { count: reprovados, total: componentes.length })}
              ratio={share(reprovados, componentes.length)}
            />
          </div>

          <Card tone="glass">
            <CardHeading
              icon={GraduationCap}
              title={t('child.report')}
              accent="violet"
            />
            <Table
              columns={gradeColumns}
              data={componentes}
              keyExtractor={(entry) => entry.componente}
              emptyMessage={t('child.noGrades')}
            />
          </Card>
        </>
      )}
    </div>
  );
}
