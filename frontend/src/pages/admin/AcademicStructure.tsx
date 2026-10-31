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

const FORM_TITLES: Record<FormState['type'], string> = {
  year: 'ano letivo',
  class: 'turma',
  component: 'componente curricular',
  assessment: 'avaliação',
};

function Empty({ children }: { children: string }) {
  return <p className="py-2 text-sm text-gray-500">{children}</p>;
}

function RowActions({ onEdit, onDelete }: { onEdit: () => void; onDelete: () => void }) {
  return (
    <span className="flex shrink-0 gap-1">
      <Button variant="secondary" size="sm" onClick={onEdit} aria-label="Editar">
        <Pencil size={14} />
      </Button>
      <Button variant="secondary" size="sm" onClick={onDelete} aria-label="Excluir">
        <Trash2 size={14} className="text-red-600" />
      </Button>
    </span>
  );
}

export default function AcademicStructure() {
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
        setError(apiErrorMessage(err, 'Não foi possível carregar a estrutura acadêmica.'));
        setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [reloadKey]);

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

    const problem = validate(form);
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

      toast.success(`Cadastro de ${FORM_TITLES[form.type]} salvo.`);
      setForm(null);
      reload();
    } catch (err) {
      // O modal permanece aberto com o que foi digitado: fechar perderia o trabalho.
      toast.error(apiErrorMessage(err, 'Não foi possível salvar o cadastro.'));
    } finally {
      setBusy(false);
    }
  };

  const confirmDelete = async () => {
    if (!pendingDelete) return;
    try {
      await pendingDelete.run();
      toast.success('Cadastro excluído.');
      setPendingDelete(null);
      reload();
    } catch (err) {
      toast.error(apiErrorMessage(err, 'Não foi possível excluir o cadastro.'));
      setPendingDelete(null);
    }
  };

  return (
    <div>
      <PageHeader
        title="Estrutura acadêmica"
        subtitle="Anos letivos, turmas, componentes curriculares e avaliações."
        actions={
          <div className="flex shrink-0 gap-2">
            {/* O lançamento de notas tem rota própria e vive aqui: o menu já usa todas as cores
                da paleta, e esta é a tela que o usuário está vendo quando vai lançar. */}
            <Button variant="secondary" onClick={() => navigate('/admin/notas')}>
              <ClipboardList size={16} className="mr-1" /> Lançar notas
            </Button>
            <Button onClick={() => openCreate('year')}>
              <Plus size={16} className="mr-1" /> Novo ano letivo
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
          <Card tone="glass" title="Anos letivos">
            <ListHeader onAdd={() => openCreate('year')} addLabel="Novo ano" disabled={false} />
            {years.length === 0 ? (
              <Empty>Nenhum ano letivo cadastrado.</Empty>
            ) : (
              <ul className="divide-y text-sm">
                {years.map((item) => (
                  <li key={item.id} className="flex items-center justify-between gap-3 py-2">
                    <div>
                      <p className="font-medium">{item.year}</p>
                      <p className="text-xs text-gray-500">
                        {formatDateOnly(item.startDate)} a {formatDateOnly(item.endDate)}
                        {item.isActive ? ' · ativo' : ' · inativo'}
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
                          label: `o ano letivo ${item.year}`,
                          run: () => api.deleteAcademicYear(item.id),
                        })
                      }
                    />
                  </li>
                ))}
              </ul>
            )}
          </Card>

          <Card tone="glass" title="Turmas">
            <ListHeader onAdd={() => openCreate('class')} addLabel="Nova turma" disabled={years.length === 0} />
            {years.length === 0 ? (
              <Empty>Cadastre um ano letivo antes de criar turmas.</Empty>
            ) : classes.length === 0 ? (
              <Empty>Nenhuma turma cadastrada.</Empty>
            ) : (
              <ul className="divide-y text-sm">
                {classes.map((item) => (
                  <li key={item.id} className="flex items-center justify-between gap-3 py-2">
                    <div>
                      <p className="font-medium">{item.name}</p>
                      <p className="text-xs text-gray-500">Ano letivo {yearName(item.academicYearId)}</p>
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
                          label: `a turma ${item.name}`,
                          run: () => api.deleteSchoolClass(item.id),
                        })
                      }
                    />
                  </li>
                ))}
              </ul>
            )}
          </Card>

          <Card tone="glass" title="Componentes curriculares">
            <ListHeader onAdd={() => openCreate('component')} addLabel="Novo componente" disabled={false} />
            {components.length === 0 ? (
              <Empty>Nenhum componente curricular cadastrado.</Empty>
            ) : (
              <ul className="divide-y text-sm">
                {components.map((item) => (
                  <li key={item.id} className="flex items-center justify-between gap-3 py-2">
                    <div>
                      <p className="font-medium">{item.name}</p>
                      <p className="text-xs text-gray-500">
                        {item.code}
                        {item.isActive ? ' · ativo' : ' · inativo'}
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
                          label: `o componente ${item.name}`,
                          run: () => api.deleteComponent(item.id),
                        })
                      }
                    />
                  </li>
                ))}
              </ul>
            )}
          </Card>

          <Card tone="glass" title="Avaliações">
            <ListHeader
              onAdd={() => openCreate('assessment')}
              addLabel="Nova avaliação"
              disabled={classes.length === 0 || components.length === 0}
            />
            {classes.length === 0 || components.length === 0 ? (
              <Empty>Cadastre uma turma e um componente antes de criar avaliações.</Empty>
            ) : assessments.length === 0 ? (
              <Empty>Nenhuma avaliação cadastrada.</Empty>
            ) : (
              <ul className="divide-y text-sm">
                {assessments.map((item) => (
                  <li key={item.id} className="flex items-center justify-between gap-3 py-2">
                    <div>
                      <p className="font-medium">{item.title}</p>
                      <p className="text-xs text-gray-500">
                        {classLabel(item.schoolClassId)} · {componentName(item.curriculumComponentId)} ·{' '}
                        {formatDateOnly(item.date)} · peso {item.weight}
                        {item.isRecovery ? ' · recuperação' : ''}
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
                          label: `a avaliação ${item.title}`,
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
        title={`${form?.id ? 'Editar' : 'Novo'} ${form ? FORM_TITLES[form.type] : ''}`}
        confirmLabel="Salvar"
        confirmDisabled={busy || (form !== null && validate(form) !== null)}
        onConfirm={submit}
      >
        {form?.type === 'year' && (
          <div className="space-y-4">
            <Input
              label="Ano"
              type="number"
              value={form.year}
              onChange={(event) => setForm({ ...form, year: event.target.value })}
            />
            <Input
              label="Início"
              type="date"
              value={form.startDate}
              onChange={(event) => setForm({ ...form, startDate: event.target.value })}
            />
            <Input
              label="Fim"
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
              Ano letivo ativo
            </label>
          </div>
        )}

        {form?.type === 'class' && (
          <div className="space-y-4">
            <Input
              label="Nome"
              value={form.name}
              onChange={(event) => setForm({ ...form, name: event.target.value })}
            />
            <Select
              label="Ano letivo"
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
              label="Nome"
              value={form.name}
              onChange={(event) => setForm({ ...form, name: event.target.value })}
            />
            <Input
              label="Código"
              value={form.code}
              onChange={(event) => setForm({ ...form, code: event.target.value })}
            />
            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(event) => setForm({ ...form, isActive: event.target.checked })}
              />
              Componente ativo
            </label>
          </div>
        )}

        {form?.type === 'assessment' && (
          <div className="space-y-4">
            <Select
              label="Turma"
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
              label="Componente"
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
              label="Título"
              value={form.title}
              onChange={(event) => setForm({ ...form, title: event.target.value })}
            />
            <Input
              label="Peso"
              value={form.weight}
              onChange={(event) => setForm({ ...form, weight: event.target.value })}
            />
            <Input
              label="Data"
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
              É avaliação de recuperação
            </label>
          </div>
        )}
      </Modal>

      <Modal
        open={pendingDelete !== null}
        onClose={() => setPendingDelete(null)}
        title="Confirmar exclusão"
        confirmLabel="Excluir"
        confirmVariant="danger"
        onConfirm={confirmDelete}
      >
        <p>Excluir {pendingDelete?.label}? Cadastros com dependentes são recusados pela API.</p>
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
function validate(form: FormState): string | null {
  if (form.type === 'year') {
    if (!/^\d{4}$/.test(form.year.trim())) return 'Informe o ano com quatro dígitos.';
    if (!form.startDate || !form.endDate) return 'Informe o início e o fim do ano letivo.';
    return null;
  }
  if (form.type === 'class') {
    if (form.name.trim() === '') return 'Informe o nome da turma.';
    if (form.academicYearId === '') return 'Selecione o ano letivo.';
    return null;
  }
  if (form.type === 'component') {
    if (form.name.trim() === '') return 'Informe o nome do componente.';
    if (form.code.trim() === '') return 'Informe o código do componente.';
    return null;
  }

  if (form.title.trim() === '') return 'Informe o título da avaliação.';
  if (!form.schoolClassId) return 'Selecione a turma.';
  if (!form.curriculumComponentId) return 'Selecione o componente.';
  if (!form.date) return 'Informe a data da avaliação.';
  const weight = Number(form.weight.replace(',', '.'));
  if (!Number.isFinite(weight) || weight <= 0) return 'O peso deve ser maior que zero.';
  return null;
}
