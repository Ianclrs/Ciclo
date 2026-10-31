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
import { formatDate } from '../../utils/format';
import { formatGrade, situacaoLabel, situacaoVariant } from '../../domain/academic';
import type { ChildDetail, ReportCard } from '../../types';

export default function ChildDetail() {
  const { id } = useParams();
  const [child, setChild] = useState<ChildDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [tab, setTab] = useState<'info' | 'docs' | 'grades'>('info');
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
        setError(apiErrorMessage(err, 'Não foi possível carregar os dados do aluno.'));
      });
    return () => { active = false; };
  }, [id, reloadKey]);

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
        setReportError(apiErrorMessage(err, 'Não foi possível carregar o boletim.'));
      });
    return () => { active = false; };
  }, [tab, id, reloadKey]);

  if (error) {
    return (
      <div>
        <Link to="/parent" className="flex items-center gap-1 text-sm text-gray-500 mb-4"><ArrowLeft size={16} /> Voltar</Link>
        <LoadError message={error} onRetry={() => setReloadKey((k) => k + 1)} />
      </div>
    );
  }

  if (!child) return <PageSkeleton />;

  const reportLoading = tab === 'grades' && report === undefined && reportError === null;

  return (
    <div>
      <Link to="/parent" className="flex items-center gap-1 text-sm text-gray-500 mb-4"><ArrowLeft size={16} /> Voltar</Link>
      <PageHeader
        title={child.student.nome}
        subtitle={`${child.student.turma} · ano letivo ${child.student.anoLetivo}`}
        leading={<StudentAvatar name={child.student.nome} photo={child.student.foto} size="lg" />}
      />

      <div className="flex flex-wrap gap-2 mb-6">
        {(['info', 'docs', 'grades'] as const).map((t) => (
          <Button key={t} variant={tab === t ? 'primary' : 'secondary'} size="sm" onClick={() => setTab(t)}>
            {t === 'info' ? 'Informações' : t === 'docs' ? 'Documentos' : 'Notas'}
          </Button>
        ))}
      </div>

      {tab === 'info' && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <Card tone="glass" title="Dados">
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between"><dt className="text-gray-500">Turma:</dt><dd>{child.student.turma}</dd></div>
              <div className="flex justify-between"><dt className="text-gray-500">Ano:</dt><dd>{child.student.anoLetivo}</dd></div>
              <div className="flex justify-between"><dt className="text-gray-500">Status:</dt><dd><StatusBadge status={child.student.status} variant={child.student.status === 'Ativo' ? 'success' : 'warning'} /></dd></div>
            </dl>
          </Card>
          <Card tone="glass" title="Matrícula atual">
            {child.currentEnrollment ? (
              <dl className="space-y-2 text-sm">
                <div className="flex justify-between"><dt className="text-gray-500">Período:</dt><dd>{child.currentEnrollment.periodName}</dd></div>
                <div className="flex justify-between"><dt className="text-gray-500">Status:</dt><dd><StatusBadge status={child.currentEnrollment.status} variant={child.currentEnrollment.status === 'Aprovado' ? 'success' : 'warning'} /></dd></div>
              </dl>
            ) : <p className="text-gray-500">Nenhuma matrícula ativa.</p>}
          </Card>
        </div>
      )}

      {tab === 'docs' && (
        <Card tone="glass" title={`Documentos (${child.documents.length})`}>
          {child.documents.length === 0 ? <p className="text-gray-500">Nenhum documento.</p> : (
            <ul className="divide-y text-sm">
              {child.documents.map((d) => (
                <li key={d.id} className="py-3 flex items-center justify-between">
                  <div><p className="font-medium">{d.nomeArquivo}</p><p className="text-xs text-gray-500">{d.documentTypeName} • {formatDate(d.createdAt)}</p></div>
                  <StatusBadge status={d.status} variant={d.status === 'Aprovado' ? 'success' : d.status === 'Pendente' ? 'warning' : 'danger'} />
                </li>
              ))}
            </ul>
          )}
          <Link to={`/parent/children/${id}/documents`} className="mt-4 inline-flex items-center gap-1 text-sm text-indigo-600 hover:underline"><FileText size={14} /> Fazer upload de documento</Link>
        </Card>
      )}

      {tab === 'grades' && (
        <Card tone="glass" title="Boletim">
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
            <p className="text-gray-500">Nenhuma nota disponível.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b text-left">
                    <th className="py-2 whitespace-nowrap">Componente</th>
                    <th className="py-2 whitespace-nowrap">Média regular</th>
                    <th className="py-2 whitespace-nowrap">Recuperação</th>
                    <th className="py-2 whitespace-nowrap">Média final</th>
                    <th className="py-2 whitespace-nowrap">Situação</th>
                  </tr>
                </thead>
                <tbody>
                  {report.componentes.map((entry) => (
                    <tr key={entry.componente} className="border-b">
                      <td className="py-2 font-medium whitespace-nowrap">{entry.componente}</td>
                      <td className="py-2">{formatGrade(entry.mediaRegular)}</td>
                      <td className="py-2">{formatGrade(entry.notaRecuperacao)}</td>
                      <td className="py-2 font-medium">{formatGrade(entry.mediaFinal)}</td>
                      <td className="py-2">
                        <Badge variant={situacaoVariant(entry.situacao)}>
                          {situacaoLabel(entry.situacao)}
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
