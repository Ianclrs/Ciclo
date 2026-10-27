import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { ArrowLeft } from 'lucide-react';
import { Button } from '../components/Button';
import { Input } from '../components/Input';
import { Card } from '../components/Card';
import { apiErrorMessage } from '../api/errors';
import { registerParent } from '../api/auth';
import { setAccessToken } from '../api/client';
import { useAuth } from '../hooks/useAuth';
import { passwordError } from '../domain/password';

/**
 * A5 — cadastro de responsável.
 *
 * O responsável informa o **código do colégio** (slug público, o mesmo entregue ao colégio no
 * cadastro dele). Criar a conta **não** vincula aluno: sem vínculo a pessoa entra e não vê nada —
 * o vínculo é ato da secretaria. É o que permite o autocadastro sem abrir acesso a dado de aluno.
 */
export default function ParentRegisterPage() {
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
      setError('As senhas não conferem.');
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
      toast.success('Conta criada. A secretaria precisa vincular seu filho para você ver os dados.');
      navigate('/parent', { replace: true });
    } catch (err) {
      setError(apiErrorMessage(err, 'Não foi possível criar a conta.'));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-stone-100 px-4 py-10">
      <div className="mx-auto w-full max-w-lg">
        <Link to="/login" className="flex items-center gap-1 text-sm text-gray-500 mb-4">
          <ArrowLeft size={16} /> Voltar ao login
        </Link>

        <Card title="Cadastro de responsável">
          <form onSubmit={handleSubmit} className="space-y-4">
            <Input
              label="Código do colégio"
              value={form.schoolSlug}
              onChange={update('schoolSlug')}
              placeholder="ex.: colegio-modelo"
              required
            />
            <p className="text-xs text-gray-500 -mt-2">
              O colégio informa este código no primeiro contato. É o mesmo que aparece no cadastro da escola.
            </p>
            <Input label="Seu nome" value={form.name} onChange={update('name')} required />
            <Input type="email" label="E-mail" value={form.email} onChange={update('email')} required />
            <Input type="password" label="Senha" value={form.password} onChange={update('password')} required />
            <Input type="password" label="Confirmar senha" value={form.confirm} onChange={update('confirm')} required />

            {error && <p className="text-sm text-red-600" role="alert">{error}</p>}

            <Button type="submit" className="w-full" disabled={loading}>
              {loading ? 'Criando conta...' : 'Criar conta'}
            </Button>
          </form>

          <p className="text-xs text-gray-500 mt-4">
            Depois do cadastro, a secretaria do colégio precisa vincular você ao seu filho.
            Até lá a conta existe, mas não mostra dados de aluno.
          </p>
        </Card>
      </div>
    </div>
  );
}
