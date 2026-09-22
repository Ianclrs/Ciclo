import { useEffect, useState, useRef } from 'react';
import { useParams, Link } from 'react-router-dom';
import toast from 'react-hot-toast';
import axios from 'axios';
import { Card } from '../../components/Card';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { ArrowLeft, Upload } from 'lucide-react';
import * as api from '../../api/parent';
import { getDocumentTypes } from '../../api/documents';
import type { Document, DocumentType } from '../../types';

function uploadErrorMessage(err: unknown): string {
  const title =
    axios.isAxiosError(err) && typeof err.response?.data?.title === 'string'
      ? err.response.data.title
      : '';
  switch (title) {
    case 'document_type_not_found':
      return 'Tipo de documento não encontrado. Selecione um tipo válido.';
    case 'invalid_extension':
      return 'Formato de arquivo não permitido. Use PDF, JPG ou PNG.';
    case 'file_too_large':
      return 'Arquivo muito grande (máximo 10MB).';
    case 'not_linked_to_student':
      return 'Você não está vinculado a este aluno.';
    default:
      return 'Erro no upload. Tente novamente.';
  }
}

export default function ChildDocuments() {
  const { id } = useParams();
  const [docs, setDocs] = useState<Document[]>([]);
  const [docTypes, setDocTypes] = useState<DocumentType[]>([]);
  const [selectedTypeId, setSelectedTypeId] = useState('');
  const [uploading, setUploading] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (id) api.getChildDocuments(id).then(setDocs).catch(() => {});
    getDocumentTypes()
      .then((types) => {
        const active = types.filter((t) => t.isActive);
        setDocTypes(active);
        const residencia = active.find((t) => t.nome.toLowerCase().includes('resid'));
        setSelectedTypeId((residencia ?? active[0])?.id ?? '');
      })
      .catch(() => {});
  }, [id]);

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
      setDocs([doc, ...docs]);
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
      <h2 className="text-2xl font-bold text-gray-900 mb-6">Documentos</h2>
      <Card className="mb-6">
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
      <Card>
        {docs.length === 0 ? <p className="text-gray-500 text-center py-4">Nenhum documento.</p> : (
          <ul className="divide-y">
            {docs.map((d) => (
              <li key={d.id} className="py-3 flex items-center justify-between">
                <div>
                  <p className="text-sm font-medium">{d.nomeArquivo}</p>
                  <p className="text-xs text-gray-500">{d.documentTypeName} • {new Date(d.createdAt).toLocaleDateString()}</p>
                </div>
                <Badge variant={d.status === 'Aprovado' ? 'success' : d.status === 'Pendente' ? 'warning' : 'danger'}>{d.status}</Badge>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  );
}
