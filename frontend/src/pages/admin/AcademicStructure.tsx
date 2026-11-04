import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { ClipboardList, Pencil, Plus, Trash2 } from 'lucide-react';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { Button } from '../../components/Button';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { Modal } from '../../components/Modal';
import { LoadError } from '../../components/LoadError';
import { SkeletonRows } from '../../components/Skeleton';
import * as api from '../../api/academic';
import { apiErrorMessage } from '../../api/errors';
import { formatDateOnly } from '../../domain/academic';
import type { AcademicYear, Assessment, CurriculumComponent, SchoolClass } from '../../types';

/**
 * Spec 150 — cadastro da estrutura acadêmica: anos letivos, turmas, componentes curriculares e
 * avaliações, com criação, edição e exclusão.
 *
 * A regra (unicidade, faixa de datas, dependência antes de excluir) é do backend
 * (`AcademicService`). A tela só impede o envio obviamente inválido e traduz o código de erro
 * devolvido — nunca decide por conta própria se um ano pode ser excluído.
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

import { useLocale } from '../../i18n/LocaleContext';
import type { MessageKey } from '../../i18n/messages';

type Translate = (key: MessageKey, params?: Record<string, string | number>) => string;

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

function Empty({ children }: { children: string }) {
  return <p className="py-2 text-sm text-gray-500">{children}</p>;
}

function RowActions({ onEdit, onDelete }: { onEdit: () => void; onDelete: () => void }) {
  const { t } = useLocale();
  return (
    <span className="flex shrink-0 gap-1">
      <Button variant="secondary" size="sm" onClick={onEdit} aria-label={t('struct.edit')}>
        <Pencil size={14} />
      </Button>
      <Button variant="secondary" size="sm" onClick={onDelete} aria-label={t('struct.delete')}>
        <Trash2 size={14} className="text-red-600" />
      </Button>
    </span>
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
      ) : loading ? (
        <Card tone="glass">
          <SkeletonRows rows={6} />
        </Card>
      ) : (
        <div className="space-y-6">
          <Card tone="glass" title={t('struct.schoolYears')}>
            <ListHeader onAdd={() => openCreate('year')} addLabel={t('struct.add.year')} disabled={false} />
            {years.length === 0 ? (
              <Empty>{t('struct.empty.schoolYears')}</Empty>
            ) : (
              <ul className="divide-y text-sm">
                {years.map((item) => (
                  <li key={item.id} className="flex items-center justify-between gap-3 py-2">
                    <div>
                      <p className="font-medium">{item.year}</p>
                      <p className="text-xs text-gray-500">
                        {t('struct.dateRange', {
                          start: formatDateOnly(item.startDate, locale),
                          end: formatDateOnly(item.endDate, locale),
                        })}
                        {item.isActive
                          ? ` · ${t('struct.active')}`
                          : ` · ${t('struct.inactive')}`}
                      </p>
                    </div>
                    <RowActions
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
                  </li>
                ))}
              </ul>
            )}
          </Card>

          <Card tone="glass" title={t('struct.classes')}>
            <ListHeader onAdd={() => openCreate('class')} addLabel={t('struct.add.class')} disabled={years.length === 0} />
            {years.length === 0 ? (
              <Empty>{t('struct.empty.classesNeedYear')}</Empty>
            ) : classes.length === 0 ? (
              <Empty>{t('struct.empty.classes')}</Empty>
            ) : (
              <ul className="divide-y text-sm">
                {classes.map((item) => (
                  <li key={item.id} className="flex items-center justify-between gap-3 py-2">
                    <div>
                      <p className="font-medium">{item.name}</p>
                      <p className="text-xs text-gray-500">{t('struct.yearLabel', { year: yearName(item.academicYearId) })}</p>
                    </div>
                    <RowActions
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
                  </li>
                ))}
              </ul>
            )}
          </Card>

          <Card tone="glass" title={t('struct.components')}>
            <ListHeader onAdd={() => openCreate('component')} addLabel={t('struct.add.component')} disabled={false} />
            {components.length === 0 ? (
              <Empty>{t('struct.empty.components')}</Empty>
            ) : (
              <ul className="divide-y text-sm">
                {components.map((item) => (
                  <li key={item.id} className="flex items-center justify-between gap-3 py-2">
                    <div>
                      <p className="font-medium">{item.name}</p>
                      <p className="text-xs text-gray-500">
                        {item.code}
                        {item.isActive
                          ? ` · ${t('struct.active')}`
                          : ` · ${t('struct.inactive')}`}
                      </p>
                    </div>
                    <RowActions
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
                  </li>
                ))}
              </ul>
            )}
          </Card>

          <Card tone="glass" title={t('struct.assessments')}>
            <ListHeader
              onAdd={() => openCreate('assessment')}
              addLabel={t('struct.add.assessment')}
              disabled={classes.length === 0 || components.length === 0}
            />
            {classes.length === 0 || components.length === 0 ? (
              <Empty>{t('struct.empty.assessmentsNeedClass')}</Empty>
            ) : assessments.length === 0 ? (
              <Empty>{t('struct.empty.assessments')}</Empty>
            ) : (
              <ul className="divide-y text-sm">
                {assessments.map((item) => (
                  <li key={item.id} className="flex items-center justify-between gap-3 py-2">
                    <div>
                      <p className="font-medium">{item.title}</p>
                      <p className="text-xs text-gray-500">
                        {classLabel(item.schoolClassId)} · {componentName(item.curriculumComponentId)} ·{' '}
                        {formatDateOnly(item.date, locale)} ·{' '}
                        {t('struct.assessmentWeight', { weight: item.weight })}
                        {item.isRecovery ? ` · ${t('struct.recoveryTag')}` : ''}
                      </p>
                    </div>
                    <RowActions
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
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>
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
          <div className="space-y-4">
            <Input
              label={t('struct.field.year')}
              type="number"
              value={form.year}
              onChange={(event) => setForm({ ...form, year: event.target.value })}
            />
            <Input
              label={t('struct.field.start')}
              type="date"
              value={form.startDate}
              onChange={(event) => setForm({ ...form, startDate: event.target.value })}
            />
            <Input
              label={t('struct.field.end')}
              type="date"
              value={form.endDate}
              onChange={(event) => setForm({ ...form, endDate: event.target.value })}
            />
            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(event) => setForm({ ...form, isActive: event.target.checked })}
              />
              {t('struct.form.yearActive')}
            </label>
          </div>
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

function ListHeader({
  onAdd,
  addLabel,
  disabled,
}: {
  onAdd: () => void;
  addLabel: string;
  disabled: boolean;
}) {
  return (
    <div className="mb-3 flex justify-end">
      <Button variant="secondary" size="sm" onClick={onAdd} disabled={disabled}>
        <Plus size={14} className="mr-1" /> {addLabel}
      </Button>
    </div>
  );
}

/** Validação mínima de formulário; a regra de negócio é do backend. */
function validate(form: FormState, t: Translate): string | null {
  if (form.type === 'year') {
    if (!/^\d{4}$/.test(form.year.trim())) return t('struct.error.yearDigits');
    if (!form.startDate || !form.endDate) return t('struct.error.dates');
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
