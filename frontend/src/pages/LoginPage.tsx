import { useState, useEffect } from 'react';
import { useNavigate, useSearchParams, Link } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Eye, EyeOff } from 'lucide-react';
import { apiErrorCode, apiErrorMessage } from '../api/errors';
import { useAuth } from '../hooks/useAuth';
import { setAccessToken } from '../api/client';
import { Button } from '../components/Button';
import { Input } from '../components/Input';
import { buttonClass } from '../components/buttonStyles';
import { ShaderBackground } from '../components/ShaderBackground';
import { DEMO_ACCOUNTS } from '../mocks/accounts';
import { isDemoMode } from '../mocks/mode';

export default function LoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const { login, isAuthenticated, user, getCurrentUser } = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  useEffect(() => {
    if (isAuthenticated && user) {
      navigate(user.role === 'Parent' ? '/parent' : '/admin', { replace: true });
    }
  }, [isAuthenticated, user, navigate]);

  useEffect(() => {
    const token = searchParams.get('token');
    if (token) {
      setAccessToken(token);
      getCurrentUser().then((u) => {
        if (u) {
          navigate(u.role === 'Parent' ? '/parent' : '/admin', { replace: true });
        } else {
          toast.error('Falha na autenticação Google.');
        }
      });
    }
  }, [searchParams, navigate, getCurrentUser]);

  /** Entrada de um clique na demonstração: a senha é ignorada pelo mock. */
  const enterDemo = async (demoEmail: string) => {
    setLoading(true);
    setEmail(demoEmail);
    setPassword('demo');
    try {
      await login(demoEmail, 'demo');
    } catch (err) {
      toast.error(apiErrorMessage(err, 'Não foi possível entrar na demonstração.'));
    } finally {
      setLoading(false);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    try {
      await login(email, password);
      toast.success('Login realizado!');
    } catch (err) {
      // Sem resposta da API não é "senha errada": é falha de conexão.
      const offline = apiErrorCode(err) === null;
      toast.error(
        apiErrorMessage(
          err,
          offline
            ? 'Não foi possível conectar ao servidor. Verifique sua conexão.'
            : 'E-mail ou senha incorretos.',
        ),
      );
    } finally {
      setLoading(false);
    }
  };

  const handleGoogleLogin = () => {
    window.location.href = `${import.meta.env.VITE_API_URL || ''}/api/auth/google`;
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-[#eceff2] px-4 relative overflow-hidden">
      <ShaderBackground />

      <div className="w-full max-w-sm bg-stone-200 rounded-2xl shadow-2xl border border-stone-300 overflow-hidden relative z-10">
        {/* Top accent bar */}
        <div className="h-1 bg-gradient-to-r from-violet-500 via-blue-500 to-emerald-500 animate-gradient" />
        <div className="p-5 sm:p-8 pt-2 sm:pt-3">
          <div className="text-center mb-4 sm:mb-6">
            <img src="/Ciclo_favicon.png" alt="Ciclo" className="w-16 h-16 mx-auto sm:w-20 sm:h-20" />
            <h1 className="text-2xl font-bold text-stone-800 tracking-tight">Ciclo</h1>
            <p className="text-stone-500 mt-0.5 text-sm">Seu Sistema de Gestão Escolar</p>
          </div>
          <form onSubmit={handleSubmit} className="space-y-4">
            <Input label="Email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required className="bg-white !border-stone-400 focus:!ring-stone-500" />
            <div className="relative">
              <Input label="Senha" type={showPassword ? 'text' : 'password'} value={password} onChange={(e) => setPassword(e.target.value)} required className="bg-white !border-stone-400 focus:!ring-stone-500 !pr-10" />
              <button type="button" onClick={() => setShowPassword(!showPassword)} className="absolute right-3 top-[34px] text-stone-400 hover:text-stone-600 transition-colors" tabIndex={-1}>
                {showPassword ? <EyeOff size={18} /> : <Eye size={18} />}
              </button>
            </div>
            <div className="flex justify-end">
              <Link to="/forgot-password" className="text-sm text-stone-600 hover:text-stone-800 transition-colors">
                Esqueci minha senha?
              </Link>
            </div>
            <Button type="submit" className="w-full !bg-stone-700 hover:!bg-stone-800 shadow-md" disabled={loading}>
              {loading ? 'Entrando...' : 'Entrar'}
            </Button>
          </form>
          <Link
            to="/register"
            className={buttonClass('secondary', 'md', 'mt-4 w-full !bg-white !text-stone-700 border border-stone-300 hover:!bg-gray-50 shadow-sm hover:shadow-md')}
          >
            Cadastrar meu colégio
          </Link>
          <Link
            to="/register/parent"
            className="mt-2 block text-center text-sm text-stone-600 hover:text-stone-800 transition-colors"
          >
            Sou responsável e quero criar minha conta
          </Link>
          {isDemoMode && (
            <div className="mt-4 rounded-lg border border-violet-300 bg-violet-50 p-3">
              <p className="text-xs font-medium text-violet-900">
                Demonstração — dados fictícios gravados neste navegador, sem backend.
              </p>
              <div className="mt-2 flex flex-col gap-2">
                <Button
                  type="button"
                  size="sm"
                  disabled={loading}
                  onClick={() => enterDemo(DEMO_ACCOUNTS.admin.email)}
                >
                  Entrar como administração
                </Button>
                <Button
                  type="button"
                  size="sm"
                  variant="secondary"
                  disabled={loading}
                  onClick={() => enterDemo(DEMO_ACCOUNTS.parent.email)}
                >
                  Entrar como responsável
                </Button>
              </div>
            </div>
          )}
          <div className="relative my-6">
            <div className="absolute inset-0 flex items-center"><div className="w-full border-t border-stone-300" /></div>
            <div className="relative flex justify-center text-xs"><span className="bg-stone-200 px-3 text-stone-400">ou</span></div>
          </div>
          <button type="button" onClick={handleGoogleLogin} className="w-full flex items-center justify-center gap-3 px-4 py-2.5 bg-white text-stone-700 font-medium text-sm rounded-lg border border-stone-300 shadow-sm hover:shadow-md hover:bg-gray-50 transition-all">
            <svg className="w-5 h-5" viewBox="0 0 24 24">
              <path fill="#4285F4" d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92a5.06 5.06 0 0 1-2.2 3.32v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.1z"/>
              <path fill="#34A853" d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z"/>
              <path fill="#FBBC05" d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z"/>
              <path fill="#EA4335" d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z"/>
            </svg>
            Entrar com Google
          </button>
        </div>
      </div>
    </div>
  );
}
