import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import axios from 'axios';
import { ArrowLeft, Eye, EyeOff, KeyRound } from 'lucide-react';
import { Button } from '../components/Button';
import { Input } from '../components/Input';
import { ShaderBackground } from '../components/ShaderBackground';
import { forgotPassword, resetPassword } from '../api/auth';
import { useLocale } from '../i18n/LocaleContext';
import type { MessageKey } from '../i18n/messages';

/** Devolve a chave da regra violada, ou `null` quando a senha atende ao padrão. */
function passwordError(pw: string): MessageKey | null {
  if (pw.length < 8) return 'password.min';
  if (!/[A-Z]/.test(pw)) return 'password.upper';
  if (!/\d/.test(pw)) return 'password.digit';
  if (!/[^A-Za-z0-9]/.test(pw)) return 'password.symbol';
  return null;
}

const REQUIREMENTS: MessageKey[] = [
  'forgot.req.length',
  'forgot.req.upper',
  'forgot.req.digit',
  'forgot.req.symbol',
];

export default function ForgotPasswordPage() {
  const { t } = useLocale();
  const [step, setStep] = useState<'email' | 'reset'>('email');
  const [email, setEmail] = useState('');
  const [resetToken, setResetToken] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [errors, setErrors] = useState<{ password?: MessageKey; confirm?: MessageKey }>({});
  const navigate = useNavigate();

  const handleEmailSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    try {
      const res = await forgotPassword(email);
      if (!res.resetToken) {
        // Produção: o token seria enviado por email
        toast.success(t('forgot.sent'));
        navigate('/login');
        return;
      }
      setResetToken(res.resetToken);
      setStep('reset');
      toast.success(t('forgot.verified'));
    } catch {
      toast.error(t('forgot.requestFailed'));
    } finally {
      setLoading(false);
    }
  };

  const handleResetSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    const pwError = passwordError(newPassword);
    const confirmError: MessageKey | null =
      newPassword !== confirmPassword ? 'forgot.mismatch' : null;
    setErrors({ password: pwError ?? undefined, confirm: confirmError ?? undefined });
    if (pwError || confirmError) return;

    setLoading(true);
    try {
      await resetPassword(email, resetToken, newPassword);
      toast.success(t('forgot.changed'));
      navigate('/login');
    } catch (err) {
      const message =
        axios.isAxiosError(err) && typeof err.response?.data?.title === 'string'
          ? err.response.data.title
          : t('forgot.resetFailed');
      toast.error(message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-[#eceff2] px-4 relative overflow-hidden">
      <ShaderBackground />

      <div className="w-full max-w-sm bg-stone-200 rounded-2xl shadow-2xl border border-stone-300 overflow-hidden relative z-10">
        <div className="h-1 bg-gradient-to-r from-violet-500 via-blue-500 to-emerald-500 animate-gradient" />
        <div className="p-5 sm:p-8 pt-2 sm:pt-3">
          <div className="text-center mb-4 sm:mb-6">
            <div className="w-16 h-16 mx-auto sm:w-20 sm:h-20 rounded-full bg-stone-700/90 flex items-center justify-center">
              <KeyRound className="w-8 h-8 sm:w-10 sm:h-10 text-white" />
            </div>
            <h1 className="text-2xl font-bold text-stone-800 tracking-tight mt-2">
              {t(step === 'email' ? 'forgot.title.email' : 'forgot.title.reset')}
            </h1>
            <p className="text-stone-500 mt-0.5 text-sm">
              {t(step === 'email' ? 'forgot.subtitle.email' : 'forgot.subtitle.reset')}
            </p>
          </div>

          {step === 'email' ? (
            <form onSubmit={handleEmailSubmit} className="space-y-4">
              <Input
                label={t('forgot.emailLabel')}
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                placeholder={t('field.emailExample')}
                className="bg-white !border-stone-400 focus:!ring-stone-500"
              />
              <Button type="submit" className="w-full !bg-stone-700 hover:!bg-stone-800 shadow-md" disabled={loading}>
                {loading ? t('forgot.verifying') : t('forgot.continue')}
              </Button>
            </form>
          ) : (
            <form onSubmit={handleResetSubmit} className="space-y-4">
              <div className="relative">
                <Input
                  label={t('forgot.newPassword')}
                  type={showPassword ? 'text' : 'password'}
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  required
                  error={errors.password ? t(errors.password) : undefined}
                  className="bg-white !border-stone-400 focus:!ring-stone-500 !pr-10"
                />
                <button
                  type="button"
                  onClick={() => setShowPassword(!showPassword)}
                  aria-label={showPassword ? t('auth.hidePassword') : t('auth.showPassword')}
                  aria-pressed={showPassword}
                  className="absolute right-3 top-[34px] text-stone-400 hover:text-stone-600 transition-colors"
                  tabIndex={-1}
                >
                  {showPassword ? <EyeOff size={18} /> : <Eye size={18} />}
                </button>
              </div>
              <Input
                label={t('forgot.confirmPassword')}
                type={showPassword ? 'text' : 'password'}
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                required
                error={errors.confirm ? t(errors.confirm) : undefined}
                className="bg-white !border-stone-400 focus:!ring-stone-500"
              />
              <div className="bg-stone-100 border border-stone-300 rounded-lg p-3 text-xs text-stone-600">
                <p className="font-medium mb-1">{t('forgot.requirements')}</p>
                <ul className="list-disc pl-4 space-y-0.5">
                  {REQUIREMENTS.map((key) => (
                    <li key={key}>{t(key)}</li>
                  ))}
                </ul>
              </div>
              <Button type="submit" className="w-full !bg-stone-700 hover:!bg-stone-800 shadow-md" disabled={loading}>
                {loading ? t('forgot.changing') : t('forgot.change')}
              </Button>
            </form>
          )}

          <div className="mt-4 text-center">
            <Link to="/login" className="inline-flex items-center gap-1 text-sm text-stone-600 hover:text-stone-800 transition-colors">
              <ArrowLeft size={16} />
              {t('auth.backToLogin')}
            </Link>
          </div>
        </div>
      </div>
    </div>
  );
}
