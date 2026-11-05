import { useEffect, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import {
  AlertTriangle,
  BookOpen,
  CalendarDays,
  ClipboardList,
  GraduationCap,
  Pencil,
  Plus,
  Trash2,
} from 'lucide-react';
import { Card } from '../../components/Card';
import { CardHeading } from '../../components/CardHeading';
import { PageHeader } from '../../components/PageHeader';
import { StatCard } from '../../components/StatCard';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { Modal } from '../../components/Modal';
import { EmptyState } from '../../components/EmptyState';
import { LoadError } from '../../components/LoadError';
import { SkeletonRows } from '../../components/Skeleton';
import { accentChip, type Accent } from '../../components/accentStyles';
import * as api from '../../api/academic';
import { apiErrorMessage } from '../../api/errors';
import { formatDateOnly } from '../../domain/academic';
import { share } from '../../utils/format';
import { useLocale } from '../../i18n/LocaleContext';
import type { MessageKey } from '../../i18n/messages';
import type { AcademicYear, Assessment, CurriculumComponent, SchoolClass } from '../../types';

/**
 * Spec 150 — cadastro da estrutura acadêmica: anos letivos, turmas, componentes curriculares e
 * avaliações, com criação, edição e exclusão.
 *
 * A regra (unicidade, faixa de datas, dependência antes de excluir) é do backend
 * (`AcademicService`). A tela só impede o envio obviamente inválido e traduz o código de erro
 * devolvido — nunca decide por conta própria se um ano pode ser excluído.
 *
 * A apresentação segue o vocabulário do painel (`CardHeading` + `StatCard`): o topo resume a
 * estrutura em números e as quatro listas ficam em duas colunas. Nada aqui calcula regra nova —
 * os indicadores são contagens das listas que a tela já carrega.
 */

type FormState =
  | { type: 'year'; id?: string; year: string; startDate: string; endDate: string; isActive: boolean }
  | { type: 'class'; id?: string; name: string; academicYearId: string }
  | { type: 'component'; id?: string; name: string; code: string; isActive: boolean }
  | {
      type: 'assessment';
      id?: string;
      schoolClassId: string;
      curriculumComponentId: string;
      title: string;
      weight: string;
      date: string;
      isRecovery: boolean;
    };

type Translate = (key: MessageKey, params?: Record<string, string | number>) => string;

type YearFormState = Extract<FormState, { type: 'year' }>;
type YearPatch = Partial<Omit<YearFormState, 'type'>>;

/** Período sugerido de um ano letivo: 1º de fevereiro a 15 de dezembro. */
function defaultRange(year: string) {
  return { startDate: `${year}-02-01`, endDate: `${year}-12-15` };
}

const FORM_ENTITY_KEYS: Record<FormState['type'], MessageKey> = {
  year: 'struct.entity.year',
  class: 'struct.entity.class',
  component: 'struct.entity.component',
  assessment: 'struct.entity.assessment',
};

/** Nome curto da entidade no idioma ativo, para títulos e mensagens compostas. */
function entityName(type: FormState['type'], t: Translate): string {
  return t(FORM_ENTITY_KEYS[type]);
}

/**
 * Chip que abre a linha. Existe para as quatro listas terem o mesmo ritmo à esquerda: sem ele,
 * cada cartão alinhava o texto em uma posição diferente e a página parecia quatro telas.
 */
function RowChip({ accent, children }: { accent: Accent; children: ReactNode }) {
  return (
    <span
      className={`flex h-10 min-w-10 shrink-0 items-center justify-center rounded-xl px-2 text-sm font-bold ring-1 ring-inset tabular-nums ${accentChip[accent]}`}
    >
      {children}
    </span>
  );
}

function RowActions({ onEdit, onDelete }: { onEdit: () => void; onDelete: () => void }) {
  const { t } = useLocale();
  return (
    <span className="flex shrink-0 gap-1">
      <Button variant="ghost" size="sm" onClick={onEdit} ariaLabel={t('struct.edit')}>
        <Pencil size={15} />
      </Button>
      <Button variant="ghost" size="sm" onClick={onDelete} ariaLabel={t('struct.delete')}>
        <Trash2 size={15} className="text-red-500" />
      </Button>
    </span>
  );
}

interface EntityRowProps {
  /** Identidade visual da linha: o número do ano letivo ou o ícone da entidade. */
  chip: ReactNode;
  title: string;
  /** Linha secundária: intervalo de datas, código do componente, turma e peso da avaliação. */
  meta?: ReactNode;
  /** Estado à direita do texto — ativo/inativo, recuperação. */
  badge?: ReactNode;
  onEdit: () => void;
  onDelete: () => void;
}

/**
 * Linha das quatro listas. Vive num componente porque o markup era o mesmo nas quatro e, ao
 * mudar qualquer detalhe, era preciso mudar quatro vezes — uma delas sempre ficava para trás.
 */
function EntityRow({ chip, title, meta, badge, onEdit, onDelete }: EntityRowProps) {
  return (
    <li className="flex items-center gap-3 rounded-xl px-2 py-2.5 transition-colors hover:bg-white/60">
      {chip}
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-semibold text-gray-900">{title}</p>
        {meta && (
          <p className="mt-0.5 flex flex-wrap items-center gap-x-1.5 text-xs text-gray-500">{meta}</p>
        )}
      </div>
      {badge}
      <RowActions onEdit={onEdit} onDelete={onDelete} />
    </li>
  );
}

export default function AcademicStructure() {
  const { language, locale, t } = useLocale();
  const navigate = useNavigate();
  const [years, setYears] = useState<AcademicYear[]>([]);
  const [classes, setClasses] = useState<SchoolClass[]>([]);
  const [components, setComponents] = useState<CurriculumComponent[]>([]);
  const [assessments, setAssessments] = useState<Assessment[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const [form, setForm] = useState<FormState | null>(null);
  const [busy, setBusy] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<
    { label: string; run: () => Promise<void> } | null
  >(null);

  useEffect(() => {
    let active = true;
    Promise.all([
      api.listAcademicYears(),
      api.listSchoolClasses(),
      api.listComponents(),
      api.listAssessments(),
    ])
      .then(([yearList, classList, componentList, assessmentList]) => {
        if (!active) return;
        setYears(yearList);
        setClasses(classList);
        setComponents(componentList);
        setAssessments(assessmentList);
        setError(null);
        setLoading(false);
      })
      .catch((err) => {
        if (!active) return;
        setError(apiErrorMessage(err, t('struct.loadError'), language));
        setLoading(false);
      });
    return () => {
      active = false;
    };
    // `t`/`language` entram porque a mensagem de erro é traduzida: trocar de idioma
    // refaz a consulta em vez de manter um texto no idioma anterior.
  }, [reloadKey, t, language]);

  const reload = () => {
    setLoading(true);
    setReloadKey((key) => key + 1);
  };

  const yearName = (id: string) => {
    const found = years.find((item) => item.id === id);
    return found ? `${found.year}` : '—';
  };

  const classLabel = (id: string) => classes.find((item) => item.id === id)?.name ?? '—';
  const componentName = (id: string) => components.find((item) => item.id === id)?.name ?? '—';

  const openCreate = (type: FormState['type']) => {
    if (type === 'year') {
      const current = new Date().getFullYear();
      setForm({
        type,
        year: String(current),
        startDate: `${current}-02-01`,
        endDate: `${current}-12-15`,
        isActive: true,
      });
      return;
    }
    if (type === 'class') {
      setForm({ type, name: '', academicYearId: years[0]?.id ?? '' });
      return;
    }
    if (type === 'component') {
      setForm({ type, name: '', code: '', isActive: true });
      return;
    }
    setForm({
      type,
      schoolClassId: classes[0]?.id ?? '',
      curriculumComponentId: components[0]?.id ?? '',
      title: '',
      weight: '1',
      date: new Date().toISOString().slice(0, 10),
      isRecovery: false,
    });
  };

  const submit = async () => {
    if (!form) return;

    const problem = validate(form, t);
    if (problem) {
      toast.error(problem);
      return;
    }

    setBusy(true);
    try {
      if (form.type === 'year') {
        const body = {
          year: Number(form.year),
          startDate: form.startDate,
          endDate: form.endDate,
          isActive: form.isActive,
        };
        if (form.id) await api.updateAcademicYear(form.id, body);
        else await api.createAcademicYear(body);
      } else if (form.type === 'class') {
        const body = { name: form.name.trim(), academicYearId: form.academicYearId };
        if (form.id) await api.updateSchoolClass(form.id, body);
        else await api.createSchoolClass(body);
      } else if (form.type === 'component') {
        const body = { name: form.name.trim(), code: form.code.trim(), isActive: form.isActive };
        if (form.id) await api.updateComponent(form.id, body);
        else await api.createComponent(body);
      } else {
        const body = {
          schoolClassId: form.schoolClassId,
          curriculumComponentId: form.curriculumComponentId,
          title: form.title.trim(),
          weight: Number(form.weight.replace(',', '.')),
          date: form.date,
          isRecovery: form.isRecovery,
        };
        if (form.id) await api.updateAssessment(form.id, body);
        else await api.createAssessment(body);
      }

      toast.success(t('struct.saved', { entity: entityName(form.type, t) }));
      setForm(null);
      reload();
    } catch (err) {
      // O modal permanece aberto com o que foi digitado: fechar perderia o trabalho.
      toast.error(apiErrorMessage(err, t('struct.saveError'), language));
    } finally {
      setBusy(false);
    }
  };

  const confirmDelete = async () => {
    if (!pendingDelete) return;
    try {
      await pendingDelete.run();
      toast.success(t('struct.deleted'));
      setPendingDelete(null);
      reload();
    } catch (err) {
      toast.error(apiErrorMessage(err, t('struct.deleteError'), language));
      setPendingDelete(null);
    }
  };

  // Indicadores: contagens das listas que a tela já carregou — nenhuma consulta extra.
  // O backend não limita quantos anos letivos ficam ativos ao mesmo tempo, então as turmas
  // são contadas sobre o conjunto de ids ativos: assumir um único ano daria um número menor
  // que o real assim que houvesse dois ativos.
  const activeYearIds = new Set(years.filter((item) => item.isActive).map((item) => item.id));
  const activeYears = activeYearIds.size;
  const activeComponents = components.filter((item) => item.isActive).length;
  const recoveryAssessments = assessments.filter((item) => item.isRecovery).length;
  const classesInActiveYears = classes.filter((item) => activeYearIds.has(item.academicYearId)).length;

  // Pré-requisito que falta para criar avaliação. Serve para o botão do cartão (desabilitado)
  // e para o estado vazio apontar a mesma coisa que falta, em vez de só informar o bloqueio.
  const assessmentBlocker: 'class' | 'component' | null =
    classes.length === 0 ? 'class' : components.length === 0 ? 'component' : null;

  return (
    <div>
      <PageHeader
        title={t('struct.title')}
        subtitle={t('struct.subtitle')}
        actions={
          <div className="flex shrink-0 gap-2">
            {/* O lançamento de notas tem rota própria e vive aqui: o menu já usa todas as cores
                da paleta, e esta é a tela que o usuário está vendo quando vai lançar. */}
            <Button variant="secondary" onClick={() => navigate('/admin/notas')}>
              <ClipboardList size={16} className="mr-1" /> {t('struct.actions.gradeEntry')}
            </Button>
            <Button onClick={() => openCreate('year')}>
              <Plus size={16} className="mr-1" /> {t('struct.actions.newYear')}
            </Button>
          </div>
        }
      />

      {error ? (
        <LoadError message={error} onRetry={reload} />
      ) : (
        <>
          <div className="mb-6 grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-4">
            <StatCard
              label={t('struct.schoolYears')}
              value={years.length}
              icon={CalendarDays}
              accent="violet"
              loading={loading}
              detail={t('struct.kpi.activeOf', { active: activeYears, total: years.length })}
              ratio={share(activeYears, years.length)}
            />
            <StatCard
              label={t('struct.classes')}
              value={classes.length}
              icon={GraduationCap}
              accent="blue"
              loading={loading}
              detail={
                activeYears > 0
                  ? t('struct.kpi.classesInActiveYears', { count: classesInActiveYears })
                  : t('struct.kpi.noActiveYear')
              }
              ratio={activeYears > 0 ? share(classesInActiveYears, classes.length) : 0}
            />
            <StatCard
              label={t('struct.components')}
              value={components.length}
              icon={BookOpen}
              accent="teal"
              loading={loading}
              detail={t('struct.kpi.activeOf', {
                active: activeComponents,
                total: components.length,
              })}
              ratio={share(activeComponents, components.length)}
            />
            <StatCard
              label={t('struct.assessments')}
              value={assessments.length}
              icon={ClipboardList}
              accent="amber"
              loading={loading}
              detail={t('struct.kpi.recovery', {
                count: recoveryAssessments,
                total: assessments.length,
              })}
              ratio={share(recoveryAssessments, assessments.length)}
            />
          </div>

          {loading ? (
            <Card tone="glass">
              <SkeletonRows rows={6} />
            </Card>
          ) : (
            <div className="grid grid-cols-1 items-start gap-6 lg:grid-cols-2">
              <Card tone="glass">
                <CardHeading
                  icon={CalendarDays}
                  title={t('struct.schoolYears')}
                  accent="violet"
                  action={
                    <Button variant="secondary" size="sm" onClick={() => openCreate('year')}>
                      <Plus size={14} className="mr-1" /> {t('struct.add.year')}
                    </Button>
                  }
                />
                {years.length === 0 ? (
                  <EmptyState message={t('struct.empty.schoolYears')} />
                ) : (
                  <ul className="space-y-1">
                    {years.map((item) => (
                      <EntityRow
                        key={item.id}
                        chip={<RowChip accent="violet">{item.year}</RowChip>}
                        title={t('struct.dateRange', {
                          start: formatDateOnly(item.startDate, locale),
                          end: formatDateOnly(item.endDate, locale),
                        })}
                        badge={
                          <Badge variant={item.isActive ? 'success' : 'default'}>
                            {t(item.isActive ? 'struct.active' : 'struct.inactive')}
                          </Badge>
                        }
                        onEdit={() =>
                          setForm({
                            type: 'year',
                            id: item.id,
                            year: String(item.year),
                            startDate: item.startDate.slice(0, 10),
                            endDate: item.endDate.slice(0, 10),
                            isActive: item.isActive,
                          })
                        }
                        onDelete={() =>
                          setPendingDelete({
                            label: `${t('struct.deleteEntity.year')} ${item.year}`,
                            run: () => api.deleteAcademicYear(item.id),
                          })
                        }
                      />
                    ))}
                  </ul>
                )}
              </Card>

              <Card tone="glass">
                <CardHeading
                  icon={GraduationCap}
                  title={t('struct.classes')}
                  accent="blue"
                  action={
                    <Button
                      variant="secondary"
                      size="sm"
                      onClick={() => openCreate('class')}
                      disabled={years.length === 0}
                    >
                      <Plus size={14} className="mr-1" /> {t('struct.add.class')}
                    </Button>
                  }
                />
                {years.length === 0 ? (
                  <EmptyState
                    message={t('struct.empty.classesNeedYear')}
                    action={
                      <Button variant="secondary" size="sm" onClick={() => openCreate('year')}>
                        {t('struct.add.year')}
                      </Button>
                    }
                  />
                ) : classes.length === 0 ? (
                  <EmptyState message={t('struct.empty.classes')} />
                ) : (
                  <ul className="space-y-1">
                    {classes.map((item) => (
                      <EntityRow
                        key={item.id}
                        chip={
                          <RowChip accent="blue">
                            <GraduationCap size={18} />
                          </RowChip>
                        }
                        title={item.name}
                        meta={t('struct.yearLabel', { year: yearName(item.academicYearId) })}
                        onEdit={() =>
                          setForm({
                            type: 'class',
                            id: item.id,
                            name: item.name,
                            academicYearId: item.academicYearId,
                          })
                        }
                        onDelete={() =>
                          setPendingDelete({
                            label: `${t('struct.deleteEntity.class')} ${item.name}`,
                            run: () => api.deleteSchoolClass(item.id),
                          })
                        }
                      />
                    ))}
                  </ul>
                )}
              </Card>

              <Card tone="glass">
                <CardHeading
                  icon={BookOpen}
                  title={t('struct.components')}
                  accent="teal"
                  action={
                    <Button variant="secondary" size="sm" onClick={() => openCreate('component')}>
                      <Plus size={14} className="mr-1" /> {t('struct.add.component')}
                    </Button>
                  }
                />
                {components.length === 0 ? (
                  <EmptyState message={t('struct.empty.components')} />
                ) : (
                  <ul className="space-y-1">
                    {components.map((item) => (
                      <EntityRow
                        key={item.id}
                        chip={
                          <RowChip accent="teal">
                            <BookOpen size={18} />
                          </RowChip>
                        }
                        title={item.name}
                        meta={item.code}
                        badge={
                          <Badge variant={item.isActive ? 'success' : 'default'}>
                            {t(item.isActive ? 'struct.active' : 'struct.inactive')}
                          </Badge>
                        }
                        onEdit={() =>
                          setForm({
                            type: 'component',
                            id: item.id,
                            name: item.name,
                            code: item.code,
                            isActive: item.isActive,
                          })
                        }
                        onDelete={() =>
                          setPendingDelete({
                            label: `${t('struct.deleteEntity.component')} ${item.name}`,
                            run: () => api.deleteComponent(item.id),
                          })
                        }
                      />
                    ))}
                  </ul>
                )}
              </Card>

              <Card tone="glass">
                <CardHeading
                  icon={ClipboardList}
                  title={t('struct.assessments')}
                  accent="amber"
                  action={
                    <Button
                      variant="secondary"
                      size="sm"
                      onClick={() => openCreate('assessment')}
                      disabled={assessmentBlocker !== null}
                    >
                      <Plus size={14} className="mr-1" /> {t('struct.add.assessment')}
                    </Button>
                  }
                />
                {assessmentBlocker !== null ? (
                  <EmptyState
                    message={t('struct.empty.assessmentsNeedClass')}
                    action={
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => openCreate(assessmentBlocker)}
                      >
                        {t(assessmentBlocker === 'class' ? 'struct.add.class' : 'struct.add.component')}
                      </Button>
                    }
                  />
                ) : assessments.length === 0 ? (
                  <EmptyState message={t('struct.empty.assessments')} />
                ) : (
                  <ul className="space-y-1">
                    {assessments.map((item) => (
                      <EntityRow
                        key={item.id}
                        chip={
                          <RowChip accent="amber">
                            <ClipboardList size={18} />
                          </RowChip>
                        }
                        title={item.title}
                        meta={
                          <>
                            <span>{classLabel(item.schoolClassId)}</span>
                            <span aria-hidden="true" className="text-gray-300">·</span>
                            <span>{componentName(item.curriculumComponentId)}</span>
                            <span aria-hidden="true" className="text-gray-300">·</span>
                            <span>{formatDateOnly(item.date, locale)}</span>
                            <span aria-hidden="true" className="text-gray-300">·</span>
                            <span>{t('struct.assessmentWeight', { weight: item.weight })}</span>
                          </>
                        }
                        badge={
                          item.isRecovery ? (
                            <Badge variant="warning">{t('struct.recoveryTag')}</Badge>
                          ) : undefined
                        }
                        onEdit={() =>
                          setForm({
                            type: 'assessment',
                            id: item.id,
                            schoolClassId: item.schoolClassId,
                            curriculumComponentId: item.curriculumComponentId,
                            title: item.title,
                            weight: String(item.weight),
                            date: item.date.slice(0, 10),
                            isRecovery: item.isRecovery,
                          })
                        }
                        onDelete={() =>
                          setPendingDelete({
                            label: `${t('struct.deleteEntity.assessment')} ${item.title}`,
                            run: () => api.deleteAssessment(item.id),
                          })
                        }
                      />
                    ))}
                  </ul>
                )}
              </Card>
            </div>
          )}
        </>
      )}

      <Modal
        open={form !== null}
        onClose={() => setForm(null)}
        title={form ? t('struct.form.title', { action: t(form.id ? 'struct.form.edit' : 'struct.form.new'), entity: entityName(form.type, t) }) : ''}
        confirmLabel={t('common.save')}
        confirmDisabled={busy || (form !== null && validate(form, t) !== null)}
        onConfirm={submit}
      >
        {form?.type === 'year' && (
          <YearForm form={form} onChange={(patch) => setForm({ ...form, ...patch })} />
        )}

        {form?.type === 'class' && (
          <div className="space-y-4">
            <Input
              label={t('struct.field.name')}
              value={form.name}
              onChange={(event) => setForm({ ...form, name: event.target.value })}
            />
            <Select
              label={t('struct.field.schoolYear')}
              value={form.academicYearId}
              onChange={(event) => setForm({ ...form, academicYearId: event.target.value })}
            >
              {years.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.year}
                </option>
              ))}
            </Select>
          </div>
        )}

        {form?.type === 'component' && (
          <div className="space-y-4">
            <Input
              label={t('struct.field.name')}
              value={form.name}
              onChange={(event) => setForm({ ...form, name: event.target.value })}
            />
            <Input
              label={t('struct.field.code')}
              value={form.code}
              onChange={(event) => setForm({ ...form, code: event.target.value })}
            />
            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(event) => setForm({ ...form, isActive: event.target.checked })}
              />
              {t('struct.form.componentActive')}
            </label>
          </div>
        )}

        {form?.type === 'assessment' && (
          <div className="space-y-4">
            <Select
              label={t('struct.field.class')}
              value={form.schoolClassId}
              onChange={(event) => setForm({ ...form, schoolClassId: event.target.value })}
            >
              {classes.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name} · {item.year}
                </option>
              ))}
            </Select>
            <Select
              label={t('struct.field.component')}
              value={form.curriculumComponentId}
              onChange={(event) => setForm({ ...form, curriculumComponentId: event.target.value })}
            >
              {components.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                </option>
              ))}
            </Select>
            <Input
              label={t('struct.field.title')}
              value={form.title}
              onChange={(event) => setForm({ ...form, title: event.target.value })}
            />
            <Input
              label={t('struct.field.weight')}
              value={form.weight}
              onChange={(event) => setForm({ ...form, weight: event.target.value })}
            />
            <Input
              label={t('struct.field.date')}
              type="date"
              value={form.date}
              onChange={(event) => setForm({ ...form, date: event.target.value })}
            />
            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input
                type="checkbox"
                checked={form.isRecovery}
                onChange={(event) => setForm({ ...form, isRecovery: event.target.checked })}
              />
              {t('struct.form.isRecovery')}
            </label>
          </div>
        )}
      </Modal>

      <Modal
        open={pendingDelete !== null}
        onClose={() => setPendingDelete(null)}
        title={t('struct.deleteTitle')}
        confirmLabel={t('struct.delete')}
        confirmVariant="danger"
        onConfirm={confirmDelete}
      >
        <p>{t('struct.deletePrompt', { entity: pendingDelete?.label ?? '' })}</p>
      </Modal>
    </div>
  );
}

/**
 * Formulário do ano letivo.
 *
 * Três defeitos que o modal tinha e este componente fecha:
 *
 * 1. **Botão travado sem motivo.** O "Salvar" desabilitava com o formulário inválido e nenhuma
 *    frase dizia por quê — a explicação só existia num toast depois de apertar, e apertar era
 *    justamente o que estava bloqueado.
 * 2. **Ano e datas discordavam.** O ano novo entrava preenchido com o ano corrente e as datas
 *    ficavam em 01/02 a 15/12 *daquele* ano; trocar para 2027 criava "Ano letivo 2027: 
 *    01/02/2026 a 15/12/2026". Enquanto as datas forem as sugeridas, elas acompanham o ano; no
 *    primeiro ajuste manual param de acompanhar, porque aí quem digitou manda.
 * 3. **Intervalo invertido só era recusado no servidor.** A tela recusava a ausência das
 *    datas, mas não o fim anterior ao início — o usuário descobria pelo erro 400.
 */
function YearForm({ form, onChange }: { form: YearFormState; onChange: (patch: YearPatch) => void }) {
  const { t } = useLocale();
  const problem = validate(form, t);

  const changeYear = (value: string) => {
    const patch: YearPatch = { year: value };
    const previous = defaultRange(form.year.trim());
    const next = value.trim();

    if (/^\d{4}$/.test(next)) {
      if (form.startDate === previous.startDate) patch.startDate = defaultRange(next).startDate;
      if (form.endDate === previous.endDate) patch.endDate = defaultRange(next).endDate;
    }

    onChange(patch);
  };

  return (
    <div className="space-y-4">
      <Input
        label={t('struct.field.year')}
        type="number"
        inputMode="numeric"
        value={form.year}
        onChange={(event) => changeYear(event.target.value)}
      />

      {/* Datas lado a lado: são o mesmo dado (um intervalo) e o modal tem largura para isso. */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Input
          label={t('struct.field.start')}
          type="date"
          value={form.startDate}
          onChange={(event) => onChange({ startDate: event.target.value })}
        />
        <Input
          label={t('struct.field.end')}
          type="date"
          value={form.endDate}
          onChange={(event) => onChange({ endDate: event.target.value })}
        />
      </div>

      <label className="flex items-center gap-2.5 rounded-lg border border-gray-200 bg-white/80 px-3 py-2.5 text-sm text-gray-700">
        <input
          type="checkbox"
          className="h-4 w-4 rounded border-gray-300 accent-indigo-600"
          checked={form.isActive}
          onChange={(event) => onChange({ isActive: event.target.checked })}
        />
        {t('struct.form.yearActive')}
      </label>

      {problem && (
        <p
          role="alert"
          className="flex items-start gap-2 rounded-lg bg-red-50 px-3 py-2 text-xs text-red-700"
        >
          <AlertTriangle size={14} className="mt-0.5 shrink-0" aria-hidden="true" />
          {problem}
        </p>
      )}
    </div>
  );
}

/** Validação mínima de formulário; a regra de negócio é do backend. */
function validate(form: FormState, t: Translate): string | null {
  if (form.type === 'year') {
    if (!/^\d{4}$/.test(form.year.trim())) return t('struct.error.yearDigits');
    if (!form.startDate || !form.endDate) return t('struct.error.dates');
    // Mesma regra do backend (`academic_year_range_invalid`): o fim não pode preceder o início.
    if (form.endDate < form.startDate) return t('struct.error.dateOrder');
    return null;
  }
  if (form.type === 'class') {
    if (form.name.trim() === '') return t('struct.error.className');
    if (form.academicYearId === '') return t('struct.error.selectYear');
    return null;
  }
  if (form.type === 'component') {
    if (form.name.trim() === '') return t('struct.error.componentName');
    if (form.code.trim() === '') return t('struct.error.componentCode');
    return null;
  }

  if (form.title.trim() === '') return t('struct.error.assessmentTitle');
  if (!form.schoolClassId) return t('struct.error.selectClass');
  if (!form.curriculumComponentId) return t('struct.error.selectComponent');
  if (!form.date) return t('struct.error.assessmentDate');
  const weight = Number(form.weight.replace(',', '.'));
  if (!Number.isFinite(weight) || weight <= 0) return t('struct.error.weight');
  return null;
}
