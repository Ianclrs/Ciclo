import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import axios from 'axios';
import { ArrowLeft, Eye, EyeOff, KeyRound } from 'lucide-react';
import { Button } from '../components/Button';
import { Input } from '../components/Input';
import { forgotPassword, resetPassword } from '../api/auth';

function passwordError(pw: string): string | null {
  if (pw.length < 8) return 'A senha deve ter no mínimo 8 caracteres.';
  if (!/[A-Z]/.test(pw)) return 'A senha deve ter pelo menos uma letra maiúscula.';
  if (!/\d/.test(pw)) return 'A senha deve ter pelo menos um número.';
  if (!/[^A-Za-z0-9]/.test(pw)) return 'A senha deve ter pelo menos um símbolo (ex.: !@#$%).';
  return null;
}

export default function ForgotPasswordPage() {
  const [step, setStep] = useState<'email' | 'reset'>('email');
  const [email, setEmail] = useState('');
  const [resetToken, setResetToken] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [errors, setErrors] = useState<{ password?: string; confirm?: string }>({});
  const navigate = useNavigate();

  const handleEmailSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    try {
      const res = await forgotPassword(email);
      if (!res.resetToken) {
        // Produção: o token seria enviado por email
        toast.success('Se o email estiver cadastrado, você receberá as instruções de recuperação.');
        navigate('/login');
        return;
      }
      setResetToken(res.resetToken);
      setStep('reset');
      toast.success('Email verificado! Agora defina sua nova senha.');
    } catch {
      toast.error('Não foi possível processar a solicitação. Tente novamente.');
    } finally {
      setLoading(false);
    }
  };

  const handleResetSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    const pwError = passwordError(newPassword);
    const confirmError = newPassword !== confirmPassword ? 'As senhas não conferem.' : null;
    setErrors({ password: pwError ?? undefined, confirm: confirmError ?? undefined });
    if (pwError || confirmError) return;

    setLoading(true);
    try {
      await resetPassword(email, resetToken, newPassword);
      toast.success('Senha alterada com sucesso! Faça login com a sua nova senha.');
      navigate('/login');
    } catch (err) {
      const message =
        axios.isAxiosError(err) && typeof err.response?.data?.title === 'string'
          ? err.response.data.title
          : 'Não foi possível redefinir a senha.';
      toast.error(message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-stone-100 via-stone-50 to-stone-100 animate-gradient px-4 relative overflow-hidden">
      <div className="absolute -top-40 -right-40 w-96 h-96 bg-violet-300/40 rounded-full blur-3xl animate-blob" style={{ animationDelay: '0s', animationDuration: '24s' }} />
      <div className="absolute top-1/2 -left-20 w-72 h-72 bg-blue-300/35 rounded-full blur-3xl animate-blob" style={{ animationDelay: '2s', animationDuration: '31s', animationDirection: 'reverse' }} />
      <div className="absolute -bottom-32 right-1/4 w-80 h-80 bg-emerald-300/35 rounded-full blur-3xl animate-blob" style={{ animationDelay: '4s', animationDuration: '27s' }} />
      <div className="absolute top-1/4 left-1/3 w-60 h-60 bg-amber-300/35 rounded-full blur-3xl animate-blob" style={{ animationDelay: '6s', animationDuration: '35s', animationDirection: 'reverse' }} />
      <div className="absolute top-3/4 right-10 w-40 h-40 bg-violet-300/25 rounded-full blur-2xl animate-blob" style={{ animationDelay: '1s', animationDuration: '29s' }} />
      <div className="absolute -top-24 -left-24 w-72 h-72 bg-red-400/30 rounded-full blur-3xl animate-blob" style={{ animationDelay: '8s', animationDuration: '25s' }} />
      <div className="absolute bottom-10 right-0 w-60 h-60 bg-fuchsia-300/30 rounded-full blur-3xl animate-blob" style={{ animationDelay: '10s', animationDuration: '28s', animationDirection: 'reverse' }} />
      <div className="absolute top-2/3 left-1/2 w-52 h-52 bg-orange-300/35 rounded-full blur-3xl animate-blob" style={{ animationDelay: '12s', animationDuration: '34s' }} />

      <div className="w-full max-w-sm bg-stone-200 rounded-2xl shadow-2xl border border-stone-300 overflow-hidden relative z-10">
        <div className="h-1 bg-gradient-to-r from-violet-500 via-blue-500 to-emerald-500 animate-gradient" />
        <div className="p-5 sm:p-8 pt-2 sm:pt-3">
          <div className="text-center mb-4 sm:mb-6">
            <div className="w-16 h-16 mx-auto sm:w-20 sm:h-20 rounded-full bg-stone-700/90 flex items-center justify-center">
              <KeyRound className="w-8 h-8 sm:w-10 sm:h-10 text-white" />
            </div>
            <h1 className="text-2xl font-bold text-stone-800 tracking-tight mt-2">
              {step === 'email' ? 'Esqueci minha senha' : 'Nova senha'}
            </h1>
            <p className="text-stone-500 mt-0.5 text-sm">
              {step === 'email'
                ? 'Informe o email cadastrado para continuar.'
                : 'Defina sua nova senha seguindo o padrão de segurança.'}
            </p>
          </div>

          {step === 'email' ? (
            <form onSubmit={handleEmailSubmit} className="space-y-4">
              <Input
                label="Email cadastrado"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                placeholder="voce@email.com"
                className="bg-white !border-stone-400 focus:!ring-stone-500"
              />
              <Button type="submit" className="w-full !bg-stone-700 hover:!bg-stone-800 shadow-md" disabled={loading}>
                {loading ? 'Verificando...' : 'Continuar'}
              </Button>
            </form>
          ) : (
            <form onSubmit={handleResetSubmit} className="space-y-4">
              <div className="relative">
                <Input
                  label="Nova senha"
                  type={showPassword ? 'text' : 'password'}
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  required
                  error={errors.password}
                  className="bg-white !border-stone-400 focus:!ring-stone-500 !pr-10"
                />
                <button
                  type="button"
                  onClick={() => setShowPassword(!showPassword)}
                  className="absolute right-3 top-[34px] text-stone-400 hover:text-stone-600 transition-colors"
                  tabIndex={-1}
                >
                  {showPassword ? <EyeOff size={18} /> : <Eye size={18} />}
                </button>
              </div>
              <Input
                label="Confirmar nova senha"
                type={showPassword ? 'text' : 'password'}
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                required
                error={errors.confirm}
                className="bg-white !border-stone-400 focus:!ring-stone-500"
              />
              <div className="bg-stone-100 border border-stone-300 rounded-lg p-3 text-xs text-stone-600">
                <p className="font-medium mb-1">Requisitos da senha:</p>
                <ul className="list-disc pl-4 space-y-0.5">
                  <li>Mínimo de 8 caracteres</li>
                  <li>Pelo menos 1 letra maiúscula</li>
                  <li>Pelo menos 1 número</li>
                  <li>Pelo menos 1 símbolo (ex.: !@#$%)</li>
                </ul>
              </div>
              <Button type="submit" className="w-full !bg-stone-700 hover:!bg-stone-800 shadow-md" disabled={loading}>
                {loading ? 'Alterando senha...' : 'Alterar senha'}
              </Button>
            </form>
          )}

          <div className="mt-4 text-center">
            <Link to="/login" className="inline-flex items-center gap-1 text-sm text-stone-600 hover:text-stone-800 transition-colors">
              <ArrowLeft size={16} />
              Voltar para o login
            </Link>
          </div>
        </div>
      </div>
    </div>
  );
}
