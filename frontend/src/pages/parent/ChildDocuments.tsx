import { useEffect, useState, useRef } from 'react';
import { useParams, Link } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { StatusBadge } from '../../components/StatusBadge';
import { Button } from '../../components/Button';
import { ArrowLeft, Upload } from 'lucide-react';
import * as api from '../../api/parent';
import { apiErrorMessage } from '../../api/errors';
import { LoadError } from '../../components/LoadError';
import { getDocumentTypes } from '../../api/documents';
import { formatDate } from '../../utils/format';
import type { Document, DocumentType } from '../../types';

function uploadErrorMessage(err: unknown): string {
  return apiErrorMessage(err, 'Erro no upload. Tente novamente.');
}

export default function ChildDocuments() {
  const { id } = useParams();
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
        setError(apiErrorMessage(err, 'Não foi possível carregar os documentos.')); setLoading(false);
      });
    getDocumentTypes()
      .then((types) => {
        if (!active) return;
        const activeTypes = types.filter((t) => t.isActive);
        setDocTypes(activeTypes);
        // Sem "tipo padrão" no contrato, a primeira opção ativa é a escolha previsível.
        // A heurística por nome ("resid") adivinhava e mudava de escola para escola.
        setSelectedTypeId(activeTypes[0]?.id ?? '');
        setError(null); setLoading(false);
      })
      .catch((err) => {
        if (!active) return;
        setError(apiErrorMessage(err, 'Não foi possível carregar os tipos de documento.'));
      });
    return () => { active = false; };
  }, [id, reloadKey]);

  const handleUpload = async (e: React.FormEvent) => {
    e.preventDefault();
    const file = fileRef.current?.files?.[0];
    if (!file || !id) return;
    if (!selectedTypeId) {
      toast.error('Selecione o tipo de documento.');
      return;
    }
    if (file.size > 10 * 1024 * 1024) {
      toast.error('Arquivo muito grande (máximo 10MB).');
      return;
    }
    setUploading(true);
    try {
      const formData = new FormData();
      formData.append('file', file);
      formData.append('documentTypeId', selectedTypeId);
      const doc = await api.uploadChildDocument(id, formData);
      toast.success('Upload concluído!');
      setDocs((prev) => [doc, ...prev]);
      if (fileRef.current) fileRef.current.value = '';
    } catch (err) {
      toast.error(uploadErrorMessage(err));
    } finally {
      setUploading(false);
    }
  };

  return (
    <div>
      <Link to={`/parent/children/${id}`} className="flex items-center gap-1 text-sm text-gray-500 mb-4"><ArrowLeft size={16} /> Voltar</Link>
      <PageHeader title="Documentos" subtitle="Envie e acompanhe os documentos do aluno." />
      {error && <div className="mb-6"><LoadError message={error} onRetry={() => setReloadKey((k) => k + 1)} /></div>}
      <Card tone="glass" className="mb-6">
        <form onSubmit={handleUpload} className="flex flex-col sm:flex-row gap-4 items-stretch sm:items-end">
          <div className="flex-1">
            <label className="block text-sm font-medium text-gray-700 mb-1">Tipo de documento</label>
            <select
              value={selectedTypeId}
              onChange={(e) => setSelectedTypeId(e.target.value)}
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-indigo-500"
              required
            >
              <option value="" disabled>Selecione...</option>
              {docTypes.map((t) => (
                <option key={t.id} value={t.id}>{t.nome}{t.isRequired ? ' (obrigatório)' : ''}</option>
              ))}
            </select>
          </div>
          <div className="flex-1">
            <label className="block text-sm font-medium text-gray-700 mb-1">Arquivo</label>
            <input type="file" ref={fileRef} className="text-sm w-full" accept=".pdf,.jpg,.jpeg,.png" required />
          </div>
          <Button type="submit" disabled={uploading}><Upload size={14} className="mr-1" />{uploading ? 'Enviando...' : 'Upload'}</Button>
        </form>
      </Card>
      <Card tone="glass">
        {loading ? (
          <p className="text-gray-500 text-center py-4">Carregando...</p>
        ) : docs.length === 0 ? (!error && <p className="text-gray-500 text-center py-4">Nenhum documento.</p>) : (
          <ul className="divide-y">
            {docs.map((d) => (
              <li key={d.id} className="py-3 flex items-center justify-between">
                <div>
                  <p className="text-sm font-medium">{d.nomeArquivo}</p>
                  <p className="text-xs text-gray-500">{d.documentTypeName} • {formatDate(d.createdAt)}</p>
                  {/* A7: o motivo já vinha no DTO e não era exibido — sem ele o responsável sabe que
                      o documento foi recusado, mas não o que corrigir para reenviar. */}
                  {d.motivoRejeicao && (
                    <p className="text-xs text-red-600 mt-1">Motivo da rejeição: {d.motivoRejeicao}</p>
                  )}
                </div>
                <StatusBadge status={d.status} variant={d.status === 'Aprovado' ? 'success' : d.status === 'Pendente' ? 'warning' : 'danger'} />
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  );
}
