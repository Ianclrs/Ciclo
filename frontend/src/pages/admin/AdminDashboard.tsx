import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowRight, Bell, CalendarDays, FileText, GraduationCap, Users } from 'lucide-react';
import { Card } from '../../components/Card';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { StatCard } from '../../components/StatCard';
import { RecentNotifications } from '../../components/RecentNotifications';
import { useAuth } from '../../hooks/useAuth';
import * as studentsApi from '../../api/students';
import * as enrollmentsApi from '../../api/enrollments';
import * as documentsApi from '../../api/documents';
import * as notificationsApi from '../../api/notifications';
import type { EnrollmentPeriod } from '../../types';

const DAY_MS = 86_400_000;

interface Stats {
  students: number;
  enrollments: number;
  documents: number;
  notifications: number;
}

/** Dias de calendário entre hoje e a data informada (ignora a hora, evita erro de arredondamento). */
function daysUntil(iso: string): number {
  const dayStart = (d: Date) => Date.UTC(d.getFullYear(), d.getMonth(), d.getDate());
  return Math.round((dayStart(new Date(iso)) - dayStart(new Date())) / DAY_MS);
}

/** Percentual do período já transcorrido, limitado a 0–100. */
function elapsedPercent(period: EnrollmentPeriod): number {
  const start = new Date(period.dataInicio).getTime();
  const end = new Date(period.dataFim).getTime();
  if (end <= start) return 100;
  const percent = ((Date.now() - start) / (end - start)) * 100;
  return Math.min(100, Math.max(0, Math.round(percent)));
}

function remainingBadge(days: number): { text: string; variant: 'success' | 'warning' | 'danger' | 'default' } {
  if (days < 0) return { text: 'Encerrado', variant: 'default' };
  if (days === 0) return { text: 'Encerra hoje', variant: 'danger' };
  if (days <= 7) return { text: `${days} dia${days === 1 ? '' : 's'} restante${days === 1 ? '' : 's'}`, variant: 'warning' };
  return { text: `${days} dias restantes`, variant: 'success' };
}

export default function AdminDashboard() {
  const { user } = useAuth();
  const [stats, setStats] = useState<Stats>({ students: 0, enrollments: 0, documents: 0, notifications: 0 });
  const [period, setPeriod] = useState<EnrollmentPeriod | null>(null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        // allSettled: uma fonte com erro não zera as demais nem esconde a falha do usuário.
        const [students, enrollments, documents, unread, periods] = await Promise.allSettled([
          studentsApi.getStudents({ pageSize: 1 }).then((r) => r.total),
          enrollmentsApi.getEnrollments({ pageSize: 1 }).then((r) => r.total),
          documentsApi.getPendingDocuments({ pageSize: 1 }).then((r) => r.total),
          notificationsApi.getUnreadCount().then((r) => r.count),
          enrollmentsApi.getEnrollmentPeriods(),
        ]);

        if (cancelled) return;

        const count = (result: PromiseSettledResult<number>) =>
          result.status === 'fulfilled' ? result.value : 0;
        const activePeriods = periods.status === 'fulfilled' ? periods.value : [];

        setStats({
          students: count(students),
          enrollments: count(enrollments),
          documents: count(documents),
          notifications: count(unread),
        });
        // O endpoint já devolve apenas períodos ativos, do ano letivo mais recente primeiro.
        setPeriod(activePeriods.length > 0 ? activePeriods[0] : null);
        setFailed([students, enrollments, documents, unread, periods].some((r) => r.status === 'rejected'));
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

  const progress = period ? elapsedPercent(period) : 0;
  const badge = period ? remainingBadge(daysUntil(period.dataFim)) : null;

  return (
    <div>
      <div className="mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Dashboard</h2>
        {user && <p className="text-sm text-gray-500 mt-1">Olá, {user.name}. Resumo da sua escola.</p>}
      </div>

      {failed && (
        <div className="mb-6 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3">
          <p className="text-sm text-amber-800">
            Alguns indicadores não puderam ser carregados — os valores abaixo podem estar incompletos.
          </p>
          <Button variant="secondary" size="sm" onClick={retry}>
            Tentar de novo
          </Button>
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
        <StatCard label="Total de Alunos" value={stats.students} icon={Users} accent="blue" to="/admin/students" loading={loading} />
        <StatCard label="Matrículas" value={stats.enrollments} icon={GraduationCap} accent="green" to="/admin/enrollments" loading={loading} />
        <StatCard label="Docs Pendentes" value={stats.documents} icon={FileText} accent="amber" to="/admin/documents" loading={loading} />
        <StatCard label="Não Lidas" value={stats.notifications} icon={Bell} accent="red" to="/admin/notifications" loading={loading} />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mt-6">
        <Card>
          <div className="flex items-center gap-3 mb-4">
            <div className="p-2 rounded-lg bg-violet-100 text-violet-700">
              <CalendarDays size={20} />
            </div>
            <h3 className="font-semibold text-gray-800">Período de matrícula ativo</h3>
          </div>

          {period && badge ? (
            <div>
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="text-lg font-semibold text-gray-900 truncate">{period.nome}</p>
                  <p className="text-sm text-gray-500 mt-0.5">
                    Ano letivo {period.anoLetivo} ·{' '}
                    {new Date(period.dataInicio).toLocaleDateString('pt-BR')} a{' '}
                    {new Date(period.dataFim).toLocaleDateString('pt-BR')}
                  </p>
                </div>
                <Badge variant={badge.variant}>{badge.text}</Badge>
              </div>

              <div className="mt-5">
                <div className="h-2 rounded-full bg-gray-100 overflow-hidden">
                  <div className="h-full rounded-full bg-violet-500" style={{ width: `${progress}%` }} />
                </div>
                <p className="text-xs text-gray-400 mt-2">{progress}% do período transcorrido</p>
              </div>

              <Link
                to="/admin/enrollments"
                className="inline-flex items-center gap-1 text-sm text-violet-600 hover:text-violet-700 mt-4"
              >
                Ver matrículas <ArrowRight size={14} />
              </Link>
            </div>
          ) : (
            <p className="text-sm text-gray-500">Nenhum período de matrícula aberto.</p>
          )}
        </Card>

        <RecentNotifications viewAllTo="/admin/notifications" />
      </div>
    </div>
  );
}
