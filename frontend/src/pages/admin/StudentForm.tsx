import { useState, useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Upload } from 'lucide-react';
import { Button } from '../../components/Button';
import { buttonClass } from '../../components/buttonStyles';
import { Input } from '../../components/Input';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { StudentAvatar } from '../../components/StudentAvatar';
import { useLocale } from '../../i18n/LocaleContext';
import type { MessageKey } from '../../i18n/messages';
import * as api from '../../api/students';
import { apiErrorMessage } from '../../api/errors';

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
  const { language, t } = useLocale();
  const isEdit = !!id;
  const [form, setForm] = useState({ nome: '', turma: '', anoLetivo: new Date().getFullYear(), dataNascimento: '', cpf: '', observacoes: '' });
  const [foto, setFoto] = useState<string | null>(null);
  const [fotoError, setFotoError] = useState<MessageKey | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!id) return;
    api.getStudent(id)
      .then((s) => {
        setForm({ nome: s.nome, turma: s.turma, anoLetivo: s.anoLetivo, dataNascimento: s.dataNascimento?.split('T')[0] || '', cpf: s.cpf || '', observacoes: s.observacoes || '' });
        setFoto(s.foto);
      })
      .catch((err) => {
        toast.error(apiErrorMessage(err, t('students.loadError'), language));
        navigate('/admin/students');
      });
  }, [id, navigate, t, language]);

  const handleFoto = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    // Limpa o input para permitir escolher o mesmo arquivo novamente.
    event.target.value = '';
    if (!file) return;

    if (!file.type.startsWith('image/')) {
      setFotoError('students.photoTypeError');
      return;
    }

    try {
      setFoto(await toResizedDataUrl(file));
      setFotoError(null);
    } catch {
      setFotoError('students.photoProcessError');
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    try {
      // `foto` ausente remove a foto no backend (o campo é atribuído direto).
      const data = { ...form, cpf: form.cpf || undefined, observacoes: form.observacoes || undefined, foto: foto ?? undefined };
      if (isEdit) { await api.updateStudent(id!, data); toast.success(t('students.updated')); }
      else { await api.createStudent(data); toast.success(t('students.created')); }
      navigate('/admin/students');
    } catch (err) { toast.error(apiErrorMessage(err, t('students.saveError'), language)); }
    finally { setLoading(false); }
  };

  return (
    <div>
      <PageHeader
        title={t(isEdit ? 'students.form.editTitle' : 'students.new')}
        subtitle={t(
          isEdit ? 'students.form.editSubtitle' : 'students.form.newSubtitle',
        )}
      />
      <Card tone="glass">
        <form onSubmit={handleSubmit} className="space-y-4 max-w-lg">
          <div>
            <span className="block text-sm font-medium text-gray-700 mb-1">
              {t('students.field.photo')}
            </span>
            <div className="flex items-center gap-4">
              <StudentAvatar name={form.nome || t('students.col.student')} photo={foto} size="lg" />
              <div className="flex flex-col items-start gap-2">
                <label className={buttonClass('secondary', 'sm', 'cursor-pointer')}>
                  <Upload size={14} className="mr-1" /> {t('students.field.choosePhoto')}
                  <input type="file" accept="image/*" className="hidden" onChange={handleFoto} />
                </label>
                {foto && (
                  <Button type="button" variant="ghost" size="sm" onClick={() => setFoto(null)}>
                    {t('students.field.removePhoto')}
                  </Button>
                )}
              </div>
            </div>
            <p className="mt-2 text-xs text-gray-500">
              {t('students.field.photoHint', { size: MAX_SIDE })}
            </p>
            {fotoError && <p className="mt-1 text-sm text-red-600">{t(fotoError)}</p>}
          </div>

          <Input label={t('field.name')} value={form.nome} onChange={(e) => setForm({ ...form, nome: e.target.value })} required />
          <Input label={t('students.field.birthDate')} type="date" value={form.dataNascimento} onChange={(e) => setForm({ ...form, dataNascimento: e.target.value })} required />
          <Input label={t('students.field.cpf')} value={form.cpf} onChange={(e) => setForm({ ...form, cpf: e.target.value })} />
          <Input label={t('students.field.class')} value={form.turma} onChange={(e) => setForm({ ...form, turma: e.target.value })} required />
          <Input label={t('students.field.schoolYear')} type="number" value={String(form.anoLetivo)} onChange={(e) => setForm({ ...form, anoLetivo: Number(e.target.value) })} required />
          <Input label={t('students.field.notes')} value={form.observacoes} onChange={(e) => setForm({ ...form, observacoes: e.target.value })} />
          <div className="flex gap-3">
            <Button type="submit" disabled={loading}>
              {loading ? t('common.saving') : t('common.save')}
            </Button>
            <Button variant="secondary" type="button" onClick={() => navigate('/admin/students')}>
              {t('common.cancel')}
            </Button>
          </div>
        </form>
      </Card>
    </div>
  );
}
