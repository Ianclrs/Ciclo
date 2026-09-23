import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Bell, FileText, GraduationCap, Users } from 'lucide-react';
import { Card } from '../../components/Card';
import { Badge } from '../../components/Badge';
import { StatusBadge } from '../../components/StatusBadge';
import { StudentAvatar } from '../../components/StudentAvatar';
import { Button } from '../../components/Button';
import { StatCard } from '../../components/StatCard';
import { RecentNotifications } from '../../components/RecentNotifications';
import { useAuth } from '../../hooks/useAuth';
import * as api from '../../api/parent';
import * as notifApi from '../../api/notifications';
import type { Grade, ParentDashboard as ParentDashboardData } from '../../types';

/** Desempenho do filho no card: `media: null` = busca ok, sem notas lançadas. */
type Performance = { media: number | null; count: number };

function summarize(grades: Grade[]): Performance {
  const notas = grades.map((g) => g.nota).filter((n): n is number => n !== null);
  if (notas.length === 0) return { media: null, count: 0 };
  return { media: notas.reduce((total, n) => total + n, 0) / notas.length, count: notas.length };
}

function formatAverage(media: number): string {
  return media.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
}

function PerformanceLine({ value, loading }: { value: Performance | null | undefined; loading: boolean }) {
  if (loading) {
    return <span className="mt-4 block h-4 w-32 rounded bg-gray-200 animate-pulse" />;
  }
  // null (falha na busca) e undefined (ainda não buscado) são ambos "indisponível".
  if (value === null || value === undefined) {
    return <p className="mt-4 text-xs text-gray-400">Desempenho indisponível.</p>;
  }
  if (value.media === null) {
    return <p className="mt-4 text-xs text-gray-400">Sem notas lançadas.</p>;
  }
  return (
    <p className="mt-4 text-sm text-gray-600">
      Média <span className="font-semibold text-gray-900">{formatAverage(value.media)}</span>
      <span className="text-gray-400">
        {' '}
        · {value.count} disciplina{value.count === 1 ? '' : 's'}
      </span>
    </p>
  );
}

export default function ParentDashboard() {
  const { user } = useAuth();
  const [data, setData] = useState<ParentDashboardData | null>(null);
  // Valor `null` no mapa = a busca de notas daquele filho falhou (falha isolada).
  const [performance, setPerformance] = useState<Record<string, Performance | null>>({});
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        const [dashboard, unread] = await Promise.all([api.getDashboard(), notifApi.getUnreadCount()]);

        // Notas são uma busca por filho: uma falha isolada não derruba o dashboard nem mente sobre os dados.
        const results = await Promise.allSettled(
          dashboard.children.map((child) => api.getChildGrades(child.studentId)),
        );

        if (cancelled) return;

        setData({ ...dashboard, unreadNotifications: unread.count });

        const map: Record<string, Performance | null> = {};
        dashboard.children.forEach((child, index) => {
          const result = results[index];
          map[child.studentId] = result.status === 'fulfilled' ? summarize(result.value) : null;
        });
        setPerformance(map);
        setFailed(false);
      } catch {
        if (!cancelled) setFailed(true);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    // Descarta respostas antigas: evita que um recarregamento lento sobrescreva um mais novo.
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  const retry = () => {
    setLoading(true);
    setFailed(false);
    setReloadKey((key) => key + 1);
  };

  const children = data?.children ?? [];

  return (
    <div>
      <div className="mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Meus Filhos</h2>
        {user && (
          <p className="text-sm text-gray-500 mt-1">
            Olá, {user.name}. Acompanhe a vida escolar dos seus filhos.
          </p>
        )}
      </div>

      {failed && (
        <div className="mb-6 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3">
          <p className="text-sm text-amber-800">Não foi possível carregar os dados.</p>
          <Button variant="secondary" size="sm" onClick={retry}>
            Tentar de novo
          </Button>
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
        <StatCard label="Filhos vinculados" value={data?.totalChildren ?? 0} icon={Users} accent="violet" loading={loading} />
        <StatCard label="Matrículas ativas" value={data?.activeEnrollments ?? 0} icon={GraduationCap} accent="green" loading={loading} />
        <StatCard label="Docs Pendentes" value={data?.pendingDocuments ?? 0} icon={FileText} accent="amber" loading={loading} />
        <StatCard
          label="Não Lidas"
          value={data?.unreadNotifications ?? 0}
          icon={Bell}
          accent="red"
          to="/parent/notifications"
          loading={loading}
        />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 mt-6">
        <div className="lg:col-span-2">
          <h3 className="font-semibold text-gray-800 mb-4">Meus filhos</h3>
          {children.length === 0 ? (
            <Card>
              <p className="text-gray-500 text-center">Nenhum filho vinculado.</p>
            </Card>
          ) : (
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-6">
              {children.map((child) => (
                <Link key={child.studentId} to={`/parent/children/${child.studentId}`}>
                  <Card className="hover:shadow-md transition-shadow cursor-pointer h-full">
                    <div className="flex items-center gap-3 mb-3">
                      <StudentAvatar name={child.nome} photo={child.studentFoto} />
                      <div className="min-w-0">
                        <h3 className="font-semibold text-gray-900 truncate">{child.nome}</h3>
                        <p className="text-sm text-gray-500">
                          {child.turma} • {child.anoLetivo}
                        </p>
                      </div>
                    </div>
                    <div className="flex gap-2 flex-wrap">
                      {child.enrollmentStatus && <StatusBadge status={child.enrollmentStatus} variant="success" />}
                      {child.pendingDocuments > 0 && (
                        <Badge variant="warning">{`${child.pendingDocuments} doc(s) pendente(s)`}</Badge>
                      )}
                    </div>
                    <PerformanceLine value={performance[child.studentId]} loading={loading} />
                  </Card>
                </Link>
              ))}
            </div>
          )}
        </div>

        <RecentNotifications viewAllTo="/parent/notifications" />
      </div>
    </div>
  );
}
