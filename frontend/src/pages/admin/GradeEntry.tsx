import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Save } from 'lucide-react';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { Button } from '../../components/Button';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { LoadError } from '../../components/LoadError';
import { SkeletonRows } from '../../components/Skeleton';
import * as api from '../../api/academic';
import { apiErrorMessage } from '../../api/errors';
import { formatDateOnly, parseGradeInput } from '../../domain/academic';
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
 */
export default function GradeEntry() {
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
        setError(apiErrorMessage(err, 'Não foi possível carregar as turmas.'));
        setLoadingClasses(false);
      });
    return () => {
      active = false;
    };
  }, [reloadKey]);

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
        setError(apiErrorMessage(err, 'Não foi possível carregar a turma.'));
        setLoadingRoster(false);
      });

    return () => {
      active = false;
    };
  }, [classId, reloadKey]);

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

  const save = async (student: Student) => {
    if (!assessmentId) return;

    const parsed = parseGradeInput(grades[student.id] ?? '');
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
      toast.success(`Nota de ${student.nome} salva.`);
    } catch (err) {
      // O valor digitado permanece no campo: a tela só informa a falha.
      toast.error(apiErrorMessage(err, `Não foi possível salvar a nota de ${student.nome}.`));
    } finally {
      setSavingId(null);
    }
  };

  const selectedAssessment = assessments.find((item) => item.id === assessmentId);

  return (
    <div>
      <PageHeader
        title="Lançamento de notas"
        subtitle="Escolha a turma e a avaliação, depois grave a nota de cada aluno."
      />

      {error ? (
        <LoadError message={error} onRetry={reload} />
      ) : loadingClasses ? (
        <Card tone="glass">
          <SkeletonRows rows={5} />
        </Card>
      ) : classes.length === 0 ? (
        <Card tone="glass">
          <p className="py-2 text-sm text-gray-600">
            Nenhuma turma cadastrada. Cadastre a estrutura acadêmica primeiro em{' '}
            <Link to="/admin/academico" className="text-indigo-600 hover:underline">
              Estrutura acadêmica
            </Link>
            .
          </p>
        </Card>
      ) : (
        <Card tone="glass">
          <div className="mb-5 flex flex-wrap gap-4">
            <Select
              label="Turma"
              value={classId}
              onChange={(event) => changeClass(event.target.value)}
            >
              {classes.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name} · {item.year}
                </option>
              ))}
            </Select>
            <Select
              label="Avaliação"
              value={assessmentId}
              disabled={assessments.length === 0}
              onChange={(event) => setAssessmentId(event.target.value)}
            >
              {assessments.length === 0 ? (
                <option value="">Nenhuma avaliação nesta turma</option>
              ) : (
                assessments.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.title} · {formatDateOnly(item.date)} · peso {item.weight}
                    {item.isRecovery ? ' · recuperação' : ''}
                  </option>
                ))
              )}
            </Select>
          </div>

          {loadingRoster ? (
            <SkeletonRows rows={5} />
          ) : assessments.length === 0 ? (
            <p className="py-2 text-sm text-gray-500">
              Esta turma não tem avaliações. Cadastre uma em{' '}
              <Link to="/admin/academico" className="text-indigo-600 hover:underline">
                Estrutura acadêmica
              </Link>
              .
            </p>
          ) : students.length === 0 ? (
            <p className="py-2 text-sm text-gray-500">Nenhum aluno nesta turma.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b text-left">
                    <th className="py-2 whitespace-nowrap">Aluno</th>
                    <th className="py-2 whitespace-nowrap">Nota (0 a 10)</th>
                    <th className="py-2 whitespace-nowrap" />
                  </tr>
                </thead>
                <tbody>
                  {students.map((student) => (
                    <tr key={student.id} className="border-b align-top">
                      <td className="py-2 font-medium whitespace-nowrap">{student.nome}</td>
                      <td className="py-2">
                        <Input
                          aria-label={`Nota de ${student.nome}`}
                          inputMode="decimal"
                          className="w-24"
                          value={grades[student.id] ?? ''}
                          error={fieldErrors[student.id]}
                          onChange={(event) =>
                            setGrades((current) => ({ ...current, [student.id]: event.target.value }))
                          }
                        />
                      </td>
                      <td className="py-2 text-right">
                        <Button
                          size="sm"
                          disabled={savingId !== null}
                          onClick={() => save(student)}
                        >
                          <Save size={14} className="mr-1" />
                          {savingId === student.id ? 'Salvando…' : 'Salvar'}
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {selectedAssessment && (
            <p className="mt-4 text-xs text-gray-500">
              Notas já lançadas antes desta sessão não aparecem aqui — a API ainda não expõe leitura
              por avaliação. Lançar novamente a mesma nota atualiza o registro.
            </p>
          )}
        </Card>
      )}
    </div>
  );
}
