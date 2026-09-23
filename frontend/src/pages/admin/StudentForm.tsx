import { useState, useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Upload } from 'lucide-react';
import { Button } from '../../components/Button';
import { buttonClass } from '../../components/buttonStyles';
import { Input } from '../../components/Input';
import { Card } from '../../components/Card';
import { StudentAvatar } from '../../components/StudentAvatar';
import * as api from '../../api/students';

const MAX_SIDE = 256;
const JPEG_QUALITY = 0.82;

function loadImage(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error('image_load_failed'));
    image.src = url;
  });
}

/**
 * Reduz a imagem no próprio navegador antes de enviar: a foto é guardada como texto
 * no banco, então o tamanho do payload importa. O servidor valida o tamanho de novo.
 */
async function toResizedDataUrl(file: File): Promise<string> {
  const objectUrl = URL.createObjectURL(file);
  try {
    const image = await loadImage(objectUrl);
    const scale = Math.min(1, MAX_SIDE / Math.max(image.width, image.height));
    const width = Math.max(1, Math.round(image.width * scale));
    const height = Math.max(1, Math.round(image.height * scale));

    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext('2d');
    if (!context) throw new Error('canvas_unavailable');
    context.drawImage(image, 0, 0, width, height);

    return canvas.toDataURL('image/jpeg', JPEG_QUALITY);
  } finally {
    URL.revokeObjectURL(objectUrl);
  }
}

export default function StudentForm() {
  const { id } = useParams();
  const navigate = useNavigate();
  const isEdit = !!id;
  const [form, setForm] = useState({ nome: '', turma: '', anoLetivo: new Date().getFullYear(), dataNascimento: '', cpf: '', observacoes: '' });
  const [foto, setFoto] = useState<string | null>(null);
  const [fotoError, setFotoError] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!id) return;
    api.getStudent(id)
      .then((s) => {
        setForm({ nome: s.nome, turma: s.turma, anoLetivo: s.anoLetivo, dataNascimento: s.dataNascimento?.split('T')[0] || '', cpf: s.cpf || '', observacoes: s.observacoes || '' });
        setFoto(s.foto);
      })
      .catch(() => navigate('/admin/students'));
  }, [id, navigate]);

  const handleFoto = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    // Limpa o input para permitir escolher o mesmo arquivo novamente.
    event.target.value = '';
    if (!file) return;

    if (!file.type.startsWith('image/')) {
      setFotoError('Selecione um arquivo de imagem.');
      return;
    }

    try {
      setFoto(await toResizedDataUrl(file));
      setFotoError('');
    } catch {
      setFotoError('Não foi possível processar a imagem.');
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    try {
      // `foto` ausente remove a foto no backend (o campo é atribuído direto).
      const data = { ...form, cpf: form.cpf || undefined, observacoes: form.observacoes || undefined, foto: foto ?? undefined };
      if (isEdit) { await api.updateStudent(id!, data); toast.success('Aluno atualizado!'); }
      else { await api.createStudent(data); toast.success('Aluno criado!'); }
      navigate('/admin/students');
    } catch { toast.error('Erro ao salvar.'); }
    finally { setLoading(false); }
  };

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-6">{isEdit ? 'Editar Aluno' : 'Novo Aluno'}</h2>
      <Card>
        <form onSubmit={handleSubmit} className="space-y-4 max-w-lg">
          <div>
            <span className="block text-sm font-medium text-gray-700 mb-1">Foto do aluno</span>
            <div className="flex items-center gap-4">
              <StudentAvatar name={form.nome || 'Aluno'} photo={foto} size="lg" />
              <div className="flex flex-col items-start gap-2">
                <label className={buttonClass('secondary', 'sm', 'cursor-pointer')}>
                  <Upload size={14} className="mr-1" /> Escolher foto
                  <input type="file" accept="image/*" className="hidden" onChange={handleFoto} />
                </label>
                {foto && (
                  <Button type="button" variant="ghost" size="sm" onClick={() => setFoto(null)}>
                    Remover foto
                  </Button>
                )}
              </div>
            </div>
            <p className="mt-2 text-xs text-gray-500">A imagem é redimensionada para {MAX_SIDE}px antes do envio.</p>
            {fotoError && <p className="mt-1 text-sm text-red-600">{fotoError}</p>}
          </div>

          <Input label="Nome" value={form.nome} onChange={(e) => setForm({ ...form, nome: e.target.value })} required />
          <Input label="Data de Nascimento" type="date" value={form.dataNascimento} onChange={(e) => setForm({ ...form, dataNascimento: e.target.value })} required />
          <Input label="CPF" value={form.cpf} onChange={(e) => setForm({ ...form, cpf: e.target.value })} />
          <Input label="Turma" value={form.turma} onChange={(e) => setForm({ ...form, turma: e.target.value })} required />
          <Input label="Ano Letivo" type="number" value={String(form.anoLetivo)} onChange={(e) => setForm({ ...form, anoLetivo: Number(e.target.value) })} required />
          <Input label="Observações" value={form.observacoes} onChange={(e) => setForm({ ...form, observacoes: e.target.value })} />
          <div className="flex gap-3">
            <Button type="submit" disabled={loading}>{loading ? 'Salvando...' : 'Salvar'}</Button>
            <Button variant="secondary" type="button" onClick={() => navigate('/admin/students')}>Cancelar</Button>
          </div>
        </form>
      </Card>
    </div>
  );
}
