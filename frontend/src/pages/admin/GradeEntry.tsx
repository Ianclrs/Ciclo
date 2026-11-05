import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { ArrowLeft, ClipboardList, Save } from 'lucide-react';
import { Card } from '../../components/Card';
import { CardHeading } from '../../components/CardHeading';
import { PageHeader } from '../../components/PageHeader';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { Table } from '../../components/Table';
import { EmptyState } from '../../components/EmptyState';
import { LoadError } from '../../components/LoadError';
import { SkeletonRows } from '../../components/Skeleton';
import * as api from '../../api/academic';
import { apiErrorMessage } from '../../api/errors';
import { formatDateOnly, parseGradeInput } from '../../domain/academic';
import { useLocale } from '../../i18n/LocaleContext';
import type { Assessment, SchoolClass, Student } from '../../types';

/**
 * Spec 150 — lançamento de notas por avaliação.
 *
 * O roster vem de `listClassStudents` e cada linha grava a própria nota com `upsertGrade`
 * (idempotente no servidor). A faixa 0 a 10 é recusada aqui antes do envio com a mesma regra de
 * `parseGradeInput`, que é o dono único dessa validação no frontend.
 *
 * A tela não recarrega notas já lançadas: a API não expõe leitura de notas por avaliação nesta
 * versão. Campo vazio significa "ainda não lançada nesta sessão", e relançar sobrescreve.
 *
 * A apresentação separa as duas decisões da tela em dois cartões — **qual** avaliação e **o
 * que** lançar nela. Antes eram dois seletores e uma tabela dentro do mesmo bloco: o contexto
 * da avaliação (data, peso, recuperação) ficava escondido dentro da opção do `select`, e quem
 * digitava precisava lembrar de cabeça para qual prova estava lançando.
 */
export default function GradeEntry() {
  const { language, locale, t } = useLocale();
  const navigate = useNavigate();
  const [classes, setClasses] = useState<SchoolClass[]>([]);
  const [assessments, setAssessments] = useState<Assessment[]>([]);
  const [students, setStudents] = useState<Student[]>([]);

  const [classId, setClassId] = useState('');
  const [assessmentId, setAssessmentId] = useState('');

  const [grades, setGrades] = useState<Record<string, string>>({});
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [savingId, setSavingId] = useState<string | null>(null);

  const [loadingClasses, setLoadingClasses] = useState(true);
  const [loadingRoster, setLoadingRoster] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let active = true;
    api
      .listSchoolClasses()
      .then((list) => {
        if (!active) return;
        setClasses(list);
        setClassId((current) => current || list[0]?.id || '');
        setError(null);
        setLoadingClasses(false);
      })
      .catch((err) => {
        if (!active) return;
        setError(apiErrorMessage(err, t('grades.classesError'), language));
        setLoadingClasses(false);
      });
    return () => {
      active = false;
    };
  }, [reloadKey, t, language]);

  // Trocar de turma recarrega avaliações e roster. A limpeza das notas fica no manipulador do
  // seletor, e não no efeito: elas pertenciam à turma anterior e o efeito não é evento.
  useEffect(() => {
    if (!classId) return;

    let active = true;

    Promise.all([api.listAssessments({ schoolClassId: classId }), api.listClassStudents(classId)])
      .then(([assessmentList, studentList]) => {
        if (!active) return;
        setAssessments(assessmentList);
        setStudents(studentList);
        setAssessmentId(assessmentList[0]?.id ?? '');
        setError(null);
        setLoadingRoster(false);
      })
      .catch((err) => {
        if (!active) return;
        setError(apiErrorMessage(err, t('grades.rosterError'), language));
        setLoadingRoster(false);
      });

    return () => {
      active = false;
    };
  }, [classId, reloadKey, t, language]);

  const reload = () => {
    setError(null);
    setLoadingClasses(true);
    setLoadingRoster(true);
    setReloadKey((key) => key + 1);
  };

  const changeClass = (next: string) => {
    setClassId(next);
    setAssessments([]);
    setStudents([]);
    setAssessmentId('');
    setGrades({});
    setFieldErrors({});
    setLoadingRoster(true);
  };

  /**
   * Trocar de avaliação descarta as notas digitadas.
   *
   * Elas pertenciam à avaliação anterior: mantê-las no campo faria o próximo "Salvar" gravar o
   * número de outra prova. A limpeza fica no evento, como em `changeClass` — o efeito não é o
   * lugar de decidir isso, porque ele também roda no recarregamento.
   */
  const changeAssessment = (next: string) => {
    setAssessmentId(next);
    setGrades({});
    setFieldErrors({});
  };

  const save = async (student: Student) => {
    if (!assessmentId) return;

    const parsed = parseGradeInput(grades[student.id] ?? '', t);
    if (!parsed.ok) {
      setFieldErrors((current) => ({ ...current, [student.id]: parsed.error }));
      return;
    }

    setSavingId(student.id);
    try {
      await api.upsertGrade(assessmentId, student.id, parsed.value);
      setFieldErrors((current) => {
        const next = { ...current };
        delete next[student.id];
        return next;
      });
      toast.success(t('grades.saved', { name: student.nome }));
    } catch (err) {
      // O valor digitado permanece no campo: a tela só informa a falha.
      toast.error(apiErrorMessage(err, t('grades.saveError', { name: student.nome }), language));
    } finally {
      setSavingId(null);
    }
  };

  const selectedAssessment = assessments.find((item) => item.id === assessmentId);

  const columns = [
    {
      header: t('students.col.student'),
      accessor: (student: Student) => (
        <span className="whitespace-nowrap font-medium text-gray-900">{student.nome}</span>
      ),
    },
    {
      header: t('grades.col.grade'),
      className: 'w-40',
      accessor: (student: Student) => (
        <Input
          aria-label={t('grades.gradeAria', { name: student.nome })}
          inputMode="decimal"
          className="w-28"
          value={grades[student.id] ?? ''}
          error={fieldErrors[student.id]}
          onChange={(event) =>
            setGrades((current) => ({ ...current, [student.id]: event.target.value }))
          }
        />
      ),
    },
    {
      header: '',
      className: 'text-right',
      accessor: (student: Student) => (
        <Button size="sm" disabled={savingId !== null} onClick={() => save(student)}>
          <Save size={14} className="mr-1" />
          {savingId === student.id ? t('common.saving') : t('common.save')}
        </Button>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('grades.title')}
        subtitle={t('grades.subtitle')}
        actions={
          <Button variant="secondary" size="sm" onClick={() => navigate('/admin/academico')}>
            <ArrowLeft size={14} className="mr-1" /> {t('grades.academicStructure')}
          </Button>
        }
      />

      {error ? (
        <LoadError message={error} onRetry={reload} />
      ) : loadingClasses ? (
        <Card tone="glass">
          <SkeletonRows rows={5} />
        </Card>
      ) : classes.length === 0 ? (
        <Card tone="glass">
          <EmptyState
            message={t('grades.noClasses')}
            action={
              <Button variant="secondary" size="sm" onClick={() => navigate('/admin/academico')}>
                {t('grades.academicStructure')}
              </Button>
            }
          />
        </Card>
      ) : (
        <>
          <Card tone="glass" className="mb-6">
            <div className="flex flex-wrap items-end gap-4">
              <div className="min-w-[200px] flex-1 sm:max-w-xs">
                <Select
                  label={t('students.field.class')}
                  className="w-full"
                  value={classId}
                  onChange={(event) => changeClass(event.target.value)}
                >
                  {classes.map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.name} · {item.year}
                    </option>
                  ))}
                </Select>
              </div>
              <div className="min-w-[240px] flex-1 sm:max-w-md">
                <Select
                  label={t('grades.field.assessment')}
                  className="w-full"
                  value={assessmentId}
                  disabled={assessments.length === 0}
                  onChange={(event) => changeAssessment(event.target.value)}
                >
                  {assessments.length === 0 ? (
                    <option value="">{t('grades.noAssessment')}</option>
                  ) : (
                    assessments.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.title} · {formatDateOnly(item.date, locale)} · {t('grades.weight')}{' '}
                        {item.weight}
                        {item.isRecovery ? ` · ${t('grades.recovery')}` : ''}
                      </option>
                    ))
                  )}
                </Select>
              </div>
            </div>
          </Card>

          <Card tone="glass">
            <CardHeading
              icon={ClipboardList}
              title={selectedAssessment ? selectedAssessment.title : t('grades.field.assessment')}
              accent="violet"
              action={
                !loadingRoster && students.length > 0 ? (
                  <span className="rounded-full bg-white/80 px-3 py-1 text-xs font-medium text-gray-600 ring-1 ring-inset ring-gray-200">
                    {t('grades.studentCount', { count: students.length })}
                  </span>
                ) : undefined
              }
            />

            {/* Contexto da avaliação: antes só existia dentro da opção do select, e quem
                digitava precisava lembrar de cabeça para qual prova estava lançando. */}
            {selectedAssessment && (
              <p className="mb-4 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-gray-500">
                <span>{formatDateOnly(selectedAssessment.date, locale)}</span>
                <span aria-hidden="true" className="text-gray-300">·</span>
                <span>
                  {t('grades.weight')} {selectedAssessment.weight}
                </span>
                {selectedAssessment.isRecovery && (
                  <>
                    <span aria-hidden="true" className="text-gray-300">·</span>
                    <Badge variant="warning">{t('grades.recovery')}</Badge>
                  </>
                )}
              </p>
            )}

            {loadingRoster ? (
              <SkeletonRows rows={5} />
            ) : assessments.length === 0 ? (
              <EmptyState
                message={t('grades.noAssessments')}
                action={
                  <Button variant="secondary" size="sm" onClick={() => navigate('/admin/academico')}>
                    {t('grades.academicStructure')}
                  </Button>
                }
              />
            ) : (
              <Table
                columns={columns}
                data={students}
                keyExtractor={(student) => student.id}
                emptyMessage={t('grades.noStudents')}
              />
            )}

            {selectedAssessment && (
              <p className="mt-4 text-xs text-gray-500">{t('grades.footnote')}</p>
            )}
          </Card>
        </>
      )}
    </div>
  );
}
