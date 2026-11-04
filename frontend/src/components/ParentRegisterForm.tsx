import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Button } from './Button';
import { Input } from './Input';
import { apiErrorMessage } from '../api/errors';
import { registerParent } from '../api/auth';
import { setAccessToken } from '../api/client';
import { useAuth } from '../hooks/useAuth';
import { passwordError } from '../domain/password';
import { useLocale } from '../i18n/LocaleContext';

/**
 * A5 — cadastro de responsável.
 *
 * O responsável informa o **código do colégio** (slug público, o mesmo entregue ao colégio no
 * cadastro dele). Criar a conta **não** vincula aluno: sem vínculo a pessoa entra e não vê nada —
 * o vínculo é ato da secretaria. É o que permite o autocadastro sem abrir acesso a dado de aluno.
 *
 * Vive fora da página porque tem DOIS hospedeiros: a rota `/register/parent` e o painel da tela
 * de login. Diferente do cadastro de colégio, este fluxo **cria a sessão** e termina dentro de
 * `/parent` — por isso a navegação fica aqui, e não em um `onSuccess` do hospedeiro.
 */
export function ParentRegisterForm() {
  const { t } = useLocale();
  const navigate = useNavigate();
  const { getCurrentUser } = useAuth();
  const [form, setForm] = useState({ schoolSlug: '', name: '', email: '', password: '', confirm: '' });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const update = (field: keyof typeof form) => (event: React.ChangeEvent<HTMLInputElement>) =>
    setForm({ ...form, [field]: event.target.value });

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    setError(null);

    const pwError = passwordError(form.password);
    if (pwError) {
      setError(pwError);
      return;
    }
    if (form.password !== form.confirm) {
      setError(t('reg.passwordMismatch'));
      return;
    }

    setLoading(true);
    try {
      const res = await registerParent({
        schoolSlug: form.schoolSlug.trim().toLowerCase(),
        name: form.name.trim(),
        email: form.email.trim(),
        password: form.password,
      });
      setAccessToken(res.accessToken);
      await getCurrentUser();
      toast.success(t('reg.parentCreated'));
      navigate('/parent', { replace: true });
    } catch (err) {
      setError(apiErrorMessage(err, t('reg.parentCreateFailed')));
    } finally {
      setLoading(false);
    }
  };

  return (
    <>
      <form onSubmit={handleSubmit} className="space-y-3">
        <Input
          label={t('reg.schoolCode')}
          value={form.schoolSlug}
          onChange={update('schoolSlug')}
          placeholder={t('reg.schoolCodePlaceholder')}
          required
        />
        <p className="-mt-2 text-xs text-gray-500">
          {t('reg.schoolCodeHint')}
        </p>
        <Input label={t('reg.yourName')} value={form.name} onChange={update('name')} required />
        <Input type="email" label={t('field.email')} value={form.email} onChange={update('email')} required />
        <Input type="password" label={t('reg.password')} value={form.password} onChange={update('password')} required />
        <Input type="password" label={t('reg.confirmPassword')} value={form.confirm} onChange={update('confirm')} required />

        {error && (
          <p className="text-sm text-red-600" role="alert">
            {error}
          </p>
        )}

        <Button type="submit" className="w-full" disabled={loading}>
          {loading ? t('reg.creating') : t('reg.createAccount')}
        </Button>
      </form>

      <p className="mt-4 text-xs text-gray-500">
        {t('reg.parentLinkHint')}
      </p>
    </>
  );
}
