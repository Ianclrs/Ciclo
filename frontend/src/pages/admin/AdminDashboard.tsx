import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowRight, BellDot, CalendarDays, ClipboardList, FileClock, RefreshCw, UsersRound } from 'lucide-react';
import { Card } from '../../components/Card';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { StatCard } from '../../components/StatCard';
import { CardHeading } from '../../components/CardHeading';
import { RecentNotifications } from '../../components/RecentNotifications';
import { useAuth } from '../../hooks/useAuth';
import * as studentsApi from '../../api/students';
import * as enrollmentsApi from '../../api/enrollments';
import * as documentsApi from '../../api/documents';
import * as notificationsApi from '../../api/notifications';
import { ENROLLMENT_STATUSES, actionsFor } from '../../domain/enrollmentStatus';
import type { EnrollmentPeriod } from '../../types';

const DAY_MS = 86_400_000;

/**
 * Estados em que o Admin ainda precisa decidir. Derivado da máquina de estados em vez de
 * redigitado: se o backend ganhar um estado que também espera aprovação, este número acompanha.
 */
const AWAITING_REVIEW = ENROLLMENT_STATUSES.filter((status) =>
  actionsFor(status, 'Admin').includes('approve'),
);

interface Stats {
  students: number;
  activeStudents: number;
  enrollments: number;
  awaitingEnrollments: number;
  documents: number;
  expiringDocuments: number;
  notifications: number;
  totalNotifications: number;
}

const EMPTY_STATS: Stats = {
  students: 0,
  activeStudents: 0,
  enrollments: 0,
  awaitingEnrollments: 0,
  documents: 0,
  expiringDocuments: 0,
  notifications: 0,
  totalNotifications: 0,
};

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

/** Fração parte/total, com 0 quando não há base — evita divisão por zero e barra cheia falsa. */
function share(part: number, total: number): number {
  return total > 0 ? part / total : 0;
}

export default function AdminDashboard() {
  const { user } = useAuth();
  const [stats, setStats] = useState<Stats>(EMPTY_STATS);
  const [period, setPeriod] = useState<EnrollmentPeriod | null>(null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const [updatedAt, setUpdatedAt] = useState<Date | null>(null);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        // allSettled: uma fonte com erro não zera as demais nem esconde a falha do usuário.
        const [results, awaitingResults] = await Promise.all([
          Promise.allSettled([
            studentsApi.getStudents({ pageSize: 1 }).then((r) => r.total),
            enrollmentsApi.getEnrollments({ pageSize: 1 }).then((r) => r.total),
            documentsApi.getPendingDocuments({ pageSize: 1 }).then((r) => r.total),
            notificationsApi.getUnreadCount().then((r) => r.count),
            enrollmentsApi.getEnrollmentPeriods(),
            // Contexto dos KPIs: recortes do mesmo recurso, via filtro já suportado pela API.
            studentsApi.getStudents({ status: 'Ativo', pageSize: 1 }).then((r) => r.total),
            documentsApi.getExpiringDocuments(30).then((list) => list.length),
            notificationsApi.getNotifications({ page: 1, pageSize: 1 }).then((r) => r.total),
          ]),
          Promise.allSettled(
            AWAITING_REVIEW.map((status) =>
              enrollmentsApi.getEnrollments({ status, pageSize: 1 }).then((r) => r.total),
            ),
          ),
        ]);

        if (cancelled) return;

        const [students, enrollments, documents, unread, periods, active, expiring, allNotifications] = results;
        const count = (result: PromiseSettledResult<number>) =>
          result.status === 'fulfilled' ? result.value : 0;
        const activePeriods = periods.status === 'fulfilled' ? periods.value : [];
        const awaitingEnrollments = awaitingResults.reduce((sum, result) => sum + count(result), 0);

        setStats({
          students: count(students),
          activeStudents: count(active),
          enrollments: count(enrollments),
          awaitingEnrollments,
          documents: count(documents),
          expiringDocuments: count(expiring),
          notifications: count(unread),
          totalNotifications: count(allNotifications),
        });
        // O endpoint já devolve apenas períodos ativos, do ano letivo mais recente primeiro.
        setPeriod(activePeriods.length > 0 ? activePeriods[0] : null);
        setFailed(
          results.some((r) => r.status === 'rejected') ||
            awaitingResults.some((r) => r.status === 'rejected'),
        );
        setUpdatedAt(new Date());
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
      <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
        <div className="min-w-0">
          <h2 className="text-3xl font-bold tracking-tight text-gray-900">Dashboard</h2>
          <p className="mt-1 text-base text-gray-700">
            {user ? `Olá, ${user.name}. Visão geral da escola.` : 'Visão geral da escola.'}
          </p>
        </div>

        <div className="flex items-center gap-3">
          {updatedAt && !loading && (
            <span className="rounded-full bg-white/80 px-3 py-1 text-sm font-medium text-gray-700">
              Atualizado às{' '}
              {updatedAt.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}
            </span>
          )}
          <Button variant="secondary" size="sm" className="gap-2" onClick={retry} disabled={loading}>
            <RefreshCw size={14} className={loading ? 'animate-spin' : ''} />
            Atualizar
          </Button>
        </div>
      </div>

      {failed && (
        <div className="mb-6 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-amber-200/70 bg-amber-50/90 px-4 py-3 backdrop-blur-md">
          <p className="text-sm text-amber-800">
            Alguns indicadores não puderam ser carregados — os valores abaixo podem estar incompletos.
          </p>
          <Button variant="secondary" size="sm" onClick={retry}>
            Tentar de novo
          </Button>
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
        <StatCard
          label="Alunos"
          value={stats.students}
          icon={UsersRound}
          accent="blue"
          to="/admin/students"
          loading={loading}
          detail={`${stats.activeStudents} ativos de ${stats.students}`}
          ratio={share(stats.activeStudents, stats.students)}
        />
        <StatCard
          label="Matrículas"
          value={stats.enrollments}
          icon={ClipboardList}
          accent="green"
          to="/admin/enrollments"
          loading={loading}
          detail={`${stats.awaitingEnrollments} aguardando análise`}
          ratio={share(stats.awaitingEnrollments, stats.enrollments)}
        />
        <StatCard
          label="Documentos pendentes"
          value={stats.documents}
          icon={FileClock}
          accent="amber"
          to="/admin/documents"
          loading={loading}
          detail={`${stats.expiringDocuments} vencem em 30 dias`}
          ratio={share(stats.expiringDocuments, stats.documents)}
        />
        <StatCard
          label="Não lidas"
          value={stats.notifications}
          icon={BellDot}
          accent="red"
          to="/admin/notifications"
          loading={loading}
          detail={`de ${stats.totalNotifications} no total`}
          ratio={share(stats.notifications, stats.totalNotifications)}
        />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mt-6">
        <Card tone="glass">
          <CardHeading
            icon={CalendarDays}
            title="Período de matrícula"
            action={badge ? <Badge variant={badge.variant}>{badge.text}</Badge> : undefined}
          />

          {period && badge ? (
            <div>
              <p className="truncate text-lg font-semibold text-gray-900">{period.nome}</p>
              <p className="mt-0.5 flex flex-wrap items-center gap-x-2 text-sm text-gray-600">
                <span>Ano letivo {period.anoLetivo}</span>
                <span aria-hidden="true" className="text-gray-400">·</span>
                <span>
                  {new Date(period.dataInicio).toLocaleDateString('pt-BR')} a{' '}
                  {new Date(period.dataFim).toLocaleDateString('pt-BR')}
                </span>
              </p>

              <div className="mt-5 border-t border-gray-200/70 pt-4">
                <div className="flex items-center justify-between text-xs text-gray-600">
                  <span>{progress}% do período transcorrido</span>
                  <span>{daysUntil(period.dataFim) > 0 ? `restam ${daysUntil(period.dataFim)} dias` : 'último dia'}</span>
                </div>
                <div className="mt-2 h-2 overflow-hidden rounded-full bg-gray-200">
                  <div className="h-full rounded-full bg-violet-500" style={{ width: `${progress}%` }} />
                </div>
              </div>

              <Link
                to="/admin/enrollments"
                className="mt-4 inline-flex items-center gap-1 text-sm font-medium text-violet-700 hover:text-violet-800"
              >
                Ver matrículas <ArrowRight size={14} />
              </Link>
            </div>
          ) : (
            <div className="flex flex-col items-center py-8 text-center">
              <CalendarDays size={28} className="text-gray-300" />
              <p className="mt-2 text-sm text-gray-600">Nenhum período de matrícula aberto.</p>
            </div>
          )}
        </Card>

        <RecentNotifications viewAllTo="/admin/notifications" />
      </div>
    </div>
  );
}
