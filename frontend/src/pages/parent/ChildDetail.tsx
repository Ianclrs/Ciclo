import { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Card } from '../../components/Card';
import { Badge } from '../../components/Badge';
import { PageHeader } from '../../components/PageHeader';
import { StatusBadge } from '../../components/StatusBadge';
import { StudentAvatar } from '../../components/StudentAvatar';
import { Button } from '../../components/Button';
import { ArrowLeft, FileText } from 'lucide-react';
import * as api from '../../api/parent';
import { apiErrorMessage } from '../../api/errors';
import { LoadError } from '../../components/LoadError';
import { PageSkeleton, SkeletonRows } from '../../components/Skeleton';
import { useLocale } from '../../i18n/LocaleContext';
import { formatDate } from '../../utils/format';
import { formatGrade, situacaoLabelKey, situacaoVariant } from '../../domain/academic';
import type { ChildDetail, ReportCard } from '../../types';

type Tab = 'info' | 'docs' | 'grades';

export default function ChildDetail() {
  const { id } = useParams();
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
    <Link to="/parent" className="flex items-center gap-1 text-sm text-gray-500 mb-4">
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

      <div className="flex flex-wrap gap-2 mb-6">
        {(['info', 'docs', 'grades'] as const).map((tabKey) => (
          <Button key={tabKey} variant={tab === tabKey ? 'primary' : 'secondary'} size="sm" onClick={() => setTab(tabKey)}>
            {TAB_LABELS[tabKey]}
          </Button>
        ))}
      </div>

      {tab === 'info' && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
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
        <Card tone="glass" title={t('child.docsTitle', { count: child.documents.length })}>
          {child.documents.length === 0 ? <p className="text-gray-500">{t('docs.empty')}</p> : (
            <ul className="divide-y text-sm">
              {child.documents.map((d) => (
                <li key={d.id} className="py-3 flex items-center justify-between">
                  <div><p className="font-medium">{d.nomeArquivo}</p><p className="text-xs text-gray-500">{d.documentTypeName} • {formatDate(d.createdAt, locale)}</p></div>
                  <StatusBadge status={d.status} variant={d.status === 'Aprovado' ? 'success' : d.status === 'Pendente' ? 'warning' : 'danger'} />
                </li>
              ))}
            </ul>
          )}
          <Link to={`/parent/children/${id}/documents`} className="mt-4 inline-flex items-center gap-1 text-sm text-indigo-600 hover:underline">
            <FileText size={14} /> {t('child.uploadDoc')}
          </Link>
        </Card>
      )}

      {tab === 'grades' && (
        <Card tone="glass" title={t('child.report')}>
          {reportError ? (
            <LoadError
              message={reportError}
              onRetry={() => {
                // `report` volta a "não pedido": o retry mostra o esqueleto e refaz a consulta.
                setReport(undefined);
                setReportError(null);
                setReloadKey((k) => k + 1);
              }}
            />
          ) : reportLoading ? (
            <SkeletonRows rows={4} />
          ) : !report || report.componentes.length === 0 ? (
            <p className="text-gray-500">{t('child.noGrades')}</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b text-left">
                    <th className="py-2 whitespace-nowrap">{t('child.col.component')}</th>
                    <th className="py-2 whitespace-nowrap">{t('child.col.regular')}</th>
                    <th className="py-2 whitespace-nowrap">{t('child.col.recovery')}</th>
                    <th className="py-2 whitespace-nowrap">{t('child.col.final')}</th>
                    <th className="py-2 whitespace-nowrap">{t('users.col.status')}</th>
                  </tr>
                </thead>
                <tbody>
                  {report.componentes.map((entry) => (
                    <tr key={entry.componente} className="border-b">
                      <td className="py-2 font-medium whitespace-nowrap">{entry.componente}</td>
                      <td className="py-2">{formatGrade(entry.mediaRegular, locale)}</td>
                      <td className="py-2">{formatGrade(entry.notaRecuperacao, locale)}</td>
                      <td className="py-2 font-medium">{formatGrade(entry.mediaFinal, locale)}</td>
                      <td className="py-2">
                        <Badge variant={situacaoVariant(entry.situacao)}>
                          {t(situacaoLabelKey(entry.situacao))}
                        </Badge>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      )}
    </div>
  );
}
