import { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { StatusBadge } from '../../components/StatusBadge';
import { Button } from '../../components/Button';
import { Modal } from '../../components/Modal';
import { Input } from '../../components/Input';
import { ArrowLeft } from 'lucide-react';
import * as api from '../../api/enrollments';
import { apiErrorMessage } from '../../api/errors';
import { LoadError } from '../../components/LoadError';
import { PageSkeleton } from '../../components/Skeleton';
import { useLocale } from '../../i18n/LocaleContext';
import { formatDate } from '../../utils/format';
import type { Enrollment } from '../../types';

export default function EnrollmentDetail() {
  const { id } = useParams();
  const { language, locale, t } = useLocale();
  const [enrollment, setEnrollment] = useState<Enrollment | null>(null);
  const [showReject, setShowReject] = useState(false);
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const carried = enrollment?.carriedDocuments ?? [];
  const missing = enrollment?.missingDocumentTypes ?? [];

  useEffect(() => {
    if (!id) return;
    let active = true;
    api
      .getEnrollment(id)
      .then((data) => {
        if (!active) return;
        setEnrollment(data); setError(null);
      })
      .catch((err) => {
        if (!active) return;
        setError(apiErrorMessage(err, t('enrollments.loadError'), language));
      });
    return () => { active = false; };
  }, [id, reloadKey, t, language]);

  const handleApprove = async () => {
    if (!id) return;
    try { await api.approveEnrollment(id); toast.success(t('enrollments.approved')); setEnrollment(await api.getEnrollment(id)); }
    catch (err) { toast.error(apiErrorMessage(err, t('enrollments.approveError'), language)); }
  };
  const handleReject = async () => {
    if (!id) return;
    try { await api.rejectEnrollment(id, motivo); toast.success(t('enrollments.rejected')); setShowReject(false); setEnrollment(await api.getEnrollment(id)); }
    catch (err) { toast.error(apiErrorMessage(err, t('enrollments.rejectError'), language)); }
  };

  const backLink = (
    <Link to="/admin/enrollments" className="flex items-center gap-1 text-sm text-gray-500 mb-4">
      <ArrowLeft size={16} /> {t('common.back')}
    </Link>
  );

  if (error) {
    return (
      <div>
        {backLink}
        <LoadError message={error} onRetry={() => setReloadKey((k) => k + 1)} />
      </div>
    );
  }

  if (!enrollment) return <PageSkeleton />;

  return (
    <div>
      {backLink}
      <PageHeader title={t('enrollments.detailTitle')} subtitle={enrollment.studentName} />
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Card tone="glass" title={t('enrollments.info')}>
          <dl className="space-y-2 text-sm">
            <div className="flex justify-between"><dt className="text-gray-500">{t('students.col.student')}:</dt><dd>{enrollment.studentName}</dd></div>
            <div className="flex justify-between"><dt className="text-gray-500">{t('enrollments.field.period')}:</dt><dd>{enrollment.periodName}</dd></div>
            <div className="flex justify-between"><dt className="text-gray-500">{t('common.status')}:</dt><dd><StatusBadge status={enrollment.status} variant={enrollment.status === 'Aprovado' ? 'success' : 'warning'} /></dd></div>
            {enrollment.motivoRejeicao && <div className="flex justify-between"><dt className="text-gray-500">{t('enrollments.field.reason')}:</dt><dd className="text-red-600">{enrollment.motivoRejeicao}</dd></div>}
            <div className="flex justify-between"><dt className="text-gray-500">{t('enrollments.field.date')}:</dt><dd>{formatDate(enrollment.createdAt, locale)}</dd></div>
            {enrollment.approvedAt && <div className="flex justify-between"><dt className="text-gray-500">{t('enrollments.field.approvedAt')}:</dt><dd>{formatDate(enrollment.approvedAt, locale)}</dd></div>}
          </dl>
        </Card>
        {/* A4 passou a devolver a situação documental no detalhe; sem esta exibição o dado não
            chegava ao usuário — e é ele que explica POR QUE a matrícula está em documentação
            pendente ou por que a aprovação será recusada. */}
        <Card tone="glass" title={t('enrollments.documents')}>
          {missing.length > 0 ? (
            <div className="mb-4">
              <p className="text-sm font-medium text-amber-700">{t('enrollments.missingDocs')}</p>
              <ul className="mt-1 space-y-1">
                {missing.map((nome) => (
                  <li key={nome} className="text-sm text-amber-700">• {nome}</li>
                ))}
              </ul>
            </div>
          ) : (
            <p className="text-sm text-green-700 mb-4">{t('enrollments.docsOk')}</p>
          )}

          {carried.length > 0 && (
            <>
              <p className="text-sm font-medium text-gray-700">{t('enrollments.studentDocs')}</p>
              <ul className="mt-1 space-y-1">
                {carried.map((doc) => (
                  <li key={doc.documentId} className="flex justify-between gap-3 text-sm">
                    <span className="text-gray-700">{doc.documentType}</span>
                    <span className={doc.isExpired ? 'text-red-600' : 'text-gray-500'}>
                      {doc.isExpired
                        ? t('enrollments.expired')
                        : doc.dataValidade
                          ? t('enrollments.validUntil', { date: formatDate(doc.dataValidade, locale) })
                          : t('enrollments.noExpiry')}
                    </span>
                  </li>
                ))}
              </ul>
            </>
          )}

          {carried.length === 0 && missing.length === 0 && (
            <p className="text-sm text-gray-500">{t('enrollments.noDocs')}</p>
          )}
        </Card>
        {enrollment.status === 'Pendente' && (
          <Card tone="glass" title={t('enrollments.actions')}>
            <div className="flex gap-3">
              <Button onClick={handleApprove}>{t('enrollment.action.approve')}</Button>
              <Button variant="danger" onClick={() => setShowReject(true)}>{t('enrollment.action.reject')}</Button>
            </div>
          </Card>
        )}
      </div>
      <Modal open={showReject} onClose={() => setShowReject(false)} title={t('enrollments.rejectTitle')} onConfirm={handleReject} confirmLabel={t('enrollment.action.reject')} confirmVariant="danger">
        <Input label={t('enrollments.rejectReason')} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
      </Modal>
    </div>
  );
}
