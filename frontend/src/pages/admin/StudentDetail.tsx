import { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { StatusBadge } from '../../components/StatusBadge';
import { StudentAvatar } from '../../components/StudentAvatar';
import { Button } from '../../components/Button';
import { Input } from '../../components/Input';
import { ArrowLeft } from 'lucide-react';
import { PageSkeleton } from '../../components/Skeleton';
import * as api from '../../api/students';
import { apiErrorMessage } from '../../api/errors';
import { useLocale } from '../../i18n/LocaleContext';
import type { Student } from '../../types';

export default function StudentDetail() {
  const { id } = useParams();
  const { language, t } = useLocale();
  const [student, setStudent] = useState<Student | null>(null);
  const [parentEmail, setParentEmail] = useState('');

  useEffect(() => {
    if (id) {
      api
        .getStudent(id)
        .then(setStudent)
        .catch((err) => toast.error(apiErrorMessage(err, t('students.loadError'), language)));
    }
  }, [id, t, language]);

  const handleLinkParent = async () => {
    if (!parentEmail || !id) return;
    try {
      await api.linkParent(id, parentEmail);
      toast.success(t('students.parentLinked'));
      setParentEmail('');
      const s = await api.getStudent(id);
      setStudent(s);
    } catch (err) {
      toast.error(apiErrorMessage(err, t('students.parentLinkError'), language));
    }
  };

  const handleUnlink = async (parentId: string) => {
    if (!id) return;
    try {
      await api.unlinkParent(id, parentId);
      toast.success(t('students.parentUnlinked'));
      setStudent(await api.getStudent(id));
    } catch (err) {
      toast.error(apiErrorMessage(err, t('students.parentUnlinkError'), language));
    }
  };

  if (!student) return <PageSkeleton />;

  return (
    <div>
      <Link
        to="/admin/students"
        className="flex items-center gap-1 text-sm text-gray-500 hover:text-gray-700 mb-4"
      >
        <ArrowLeft size={16} /> {t('common.back')}
      </Link>
      <PageHeader
        title={student.nome}
        subtitle={t('students.detail.subtitle', { class: student.turma, year: student.anoLetivo })}
        leading={<StudentAvatar name={student.nome} photo={student.foto} size="lg" />}
      />
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Card tone="glass" title={t('students.dataTitle')}>
          <dl className="space-y-2 text-sm">
            <div className="flex justify-between">
              <dt className="text-gray-500">{t('students.field.class')}:</dt>
              <dd className="font-medium">{student.turma}</dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-gray-500">{t('students.field.schoolYear')}:</dt>
              <dd className="font-medium">{student.anoLetivo}</dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-gray-500">{t('students.field.cpf')}:</dt>
              <dd className="font-medium">{student.cpf || '—'}</dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-gray-500">{t('common.status')}:</dt>
              <dd>
                <StatusBadge
                  status={student.status}
                  variant={student.status === 'Ativo' ? 'success' : 'warning'}
                />
              </dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-gray-500">{t('students.field.notes')}:</dt>
              <dd className="font-medium">{student.observacoes || '—'}</dd>
            </div>
          </dl>
        </Card>
        <Card tone="glass" title={t('students.parents')}>
          {student.parents.length === 0 && (
            <p className="text-sm text-gray-500 mb-4">{t('students.noParents')}</p>
          )}
          <ul className="space-y-2 mb-4">
            {student.parents.map((p) => (
              <li key={p.parentId} className="flex items-center justify-between text-sm py-1">
                <span>
                  {p.parentName} <span className="text-gray-400">({p.parentEmail})</span>
                </span>
                <Button variant="ghost" size="sm" onClick={() => handleUnlink(p.parentId)}>
                  {t('common.remove')}
                </Button>
              </li>
            ))}
          </ul>
          <div className="flex gap-2">
            <Input
              placeholder={t('students.parentEmailPlaceholder')}
              value={parentEmail}
              onChange={(e) => setParentEmail(e.target.value)}
            />
            <Button size="sm" onClick={handleLinkParent}>
              {t('students.linkParent')}
            </Button>
          </div>
        </Card>
      </div>
    </div>
  );
}
