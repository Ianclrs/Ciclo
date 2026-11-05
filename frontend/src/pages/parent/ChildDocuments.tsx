import { useEffect, useState, useRef } from 'react';
import { useParams, Link } from 'react-router-dom';
import toast from 'react-hot-toast';
import { ArrowLeft, Upload } from 'lucide-react';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { Button } from '../../components/Button';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { DocumentRow } from '../../components/DocumentRow';
import { EmptyState } from '../../components/EmptyState';
import { LoadError } from '../../components/LoadError';
import { SkeletonRows } from '../../components/Skeleton';
import * as api from '../../api/parent';
import { apiErrorMessage } from '../../api/errors';
import { getDocumentTypes } from '../../api/documents';
import { useLocale } from '../../i18n/LocaleContext';
import type { Document, DocumentType } from '../../types';

/** Limite aceito no envio; o mesmo que a API recusa. */
const MAX_FILE_BYTES = 10 * 1024 * 1024;

/**
 * Documentos do filho: envio e histórico, para o responsável.
 *
 * A lista usa a mesma linha da ficha do filho (`DocumentRow`). Antes eram dois blocos parecidos,
 * e este usava `divide-y` sem cor — no Tailwind v4 a divisória herda `currentColor` e aparecia
 * escura, quase preta, entre os documentos.
 */
export default function ChildDocuments() {
  const { id } = useParams();
  const { language, t } = useLocale();
  const [docs, setDocs] = useState<Document[]>([]);
  const [docTypes, setDocTypes] = useState<DocumentType[]>([]);
  const [selectedTypeId, setSelectedTypeId] = useState('');
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);
  const fileRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!id) return;
    let active = true;
    api
      .getChildDocuments(id)
      .then((docs) => {
        if (!active) return;
        setDocs(docs); setError(null); setLoading(false);
      })
      .catch((err) => {
        if (!active) return;
        setError(apiErrorMessage(err, t('docs.loadError'), language)); setLoading(false);
      });
    getDocumentTypes()
      .then((types) => {
        if (!active) return;
        const activeTypes = types.filter((type) => type.isActive);
        setDocTypes(activeTypes);
        // Sem "tipo padrão" no contrato, a primeira opção ativa é a escolha previsível.
        // A heurística por nome ("resid") adivinhava e mudava de escola para escola.
        setSelectedTypeId(activeTypes[0]?.id ?? '');
        setError(null); setLoading(false);
      })
      .catch((err) => {
        if (!active) return;
        setError(apiErrorMessage(err, t('docs.loadTypesError'), language));
      });
    return () => { active = false; };
  }, [id, reloadKey, t, language]);

  const handleUpload = async (e: React.FormEvent) => {
    e.preventDefault();
    const file = fileRef.current?.files?.[0];
    if (!file || !id) return;
    if (!selectedTypeId) {
      toast.error(t('docs.selectType'));
      return;
    }
    if (file.size > MAX_FILE_BYTES) {
      toast.error(t('docs.tooLarge'));
      return;
    }
    setUploading(true);
    try {
      const formData = new FormData();
      formData.append('file', file);
      formData.append('documentTypeId', selectedTypeId);
      const doc = await api.uploadChildDocument(id, formData);
      toast.success(t('docs.uploaded'));
      setDocs((prev) => [doc, ...prev]);
      if (fileRef.current) fileRef.current.value = '';
    } catch (err) {
      toast.error(apiErrorMessage(err, t('docs.uploadError'), language));
    } finally {
      setUploading(false);
    }
  };

  return (
    <div>
      <Link to={`/parent/children/${id}`} className="mb-4 flex items-center gap-1 text-sm text-gray-500">
        <ArrowLeft size={16} /> {t('common.back')}
      </Link>
      <PageHeader title={t('nav.documents')} subtitle={t('docs.subtitle')} />

      {error && (
        <div className="mb-6">
          <LoadError message={error} onRetry={() => setReloadKey((k) => k + 1)} />
        </div>
      )}

      <Card tone="glass" className="mb-6">
        <form onSubmit={handleUpload} className="flex flex-col gap-4 sm:flex-row sm:items-end">
          <div className="flex-1">
            <Select
              label={t('docs.field.type')}
              className="w-full"
              value={selectedTypeId}
              onChange={(e) => setSelectedTypeId(e.target.value)}
              required
            >
              <option value="" disabled>{t('docs.selectPlaceholder')}</option>
              {docTypes.map((type) => (
                <option key={type.id} value={type.id}>
                  {type.nome}{type.isRequired ? t('docs.requiredSuffix') : ''}
                </option>
              ))}
            </Select>
          </div>

          <div className="flex-1">
            <Input
              label={t('docs.field.file')}
              type="file"
              ref={fileRef}
              accept=".pdf,.jpg,.jpeg,.png"
              required
            />
          </div>

          <Button type="submit" disabled={uploading} className="gap-1.5">
            <Upload size={14} aria-hidden="true" />
            {uploading ? t('docs.sending') : t('docs.upload')}
          </Button>
        </form>
      </Card>

      <Card tone="glass">
        {loading ? (
          <SkeletonRows rows={4} />
        ) : docs.length === 0 ? (
          !error && <EmptyState message={t('docs.empty')} />
        ) : (
          <ul className="space-y-1">
            {docs.map((doc) => (
              <DocumentRow key={doc.id} document={doc} />
            ))}
          </ul>
        )}
      </Card>
    </div>
  );
}
