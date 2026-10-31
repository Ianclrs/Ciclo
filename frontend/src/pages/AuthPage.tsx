import { useState, useEffect } from 'react';
import { useNavigate, useSearchParams, useLocation, Link } from 'react-router-dom';
import toast from 'react-hot-toast';
import {
  ArrowLeft,
  ArrowRight,
  BookOpenCheck,
  Check,
  Eye,
  EyeOff,
  FileCheck2,
  GraduationCap,
  ShieldCheck,
} from 'lucide-react';
import { apiErrorCode, apiErrorMessage } from '../api/errors';
import { useAuth } from '../hooks/useAuth';
import { setAccessToken } from '../api/client';
import { Button } from '../components/Button';
import { ShaderBackground } from '../components/ShaderBackground';
import { SchoolRegisterForm } from '../components/SchoolRegisterForm';
import { ParentRegisterForm } from '../components/ParentRegisterForm';
import { DEMO_ACCOUNTS } from '../mocks/accounts';
import { isDemoMode } from '../mocks/mode';

/**
 * Visual dos campos de texto da tela. Vive em uma constante única porque os dois campos
 * (`e-mail` e `senha`) compartilham a mesma aparência e têm de mudar juntos — o botão da
 * senha reaproveita a classe com padding à direita para não ficar sob o ícone.
 */
const fieldClass =
  'w-full rounded-xl border border-slate-200/90 bg-white/95 py-2.5 pl-10 text-sm text-slate-800 placeholder-slate-400 outline-none transition-all focus:border-indigo-600 focus:ring-4 focus:ring-indigo-600/10';

/**
 * Cards do painel institucional. São capacidades REAIS da plataforma (alunos, matrículas,
 * documentos, avisos, perfis). Não há número nem métrica aqui de propósito: a tela de login
 * não tem acesso a dado de colégio nenhum, então qualquer contagem seria inventada.
 *
 * Cada ícone diz o que o card faz: chapéu de formatura para o fluxo acadêmico, documento
 * conferido para o que é enviado e acompanhado, escudo para o recorte por perfil.
 */
const CAPABILITIES = [
  {
    title: 'Matrículas e turmas',
    text: 'Alunos, turmas e matrículas organizados em um fluxo só.',
    icon: GraduationCap,
    tone: 'bg-amber-50 border-amber-100 text-amber-600',
  },
  {
    title: 'Documentos e avisos',
    text: 'Envio de documentos e comunicados com acompanhamento.',
    icon: FileCheck2,
    tone: 'bg-emerald-50 border-emerald-100 text-emerald-600',
  },
  {
    title: 'Acesso por perfil',
    text: 'Administração e responsáveis veem apenas o que é de cada um.',
    icon: ShieldCheck,
    tone: 'bg-indigo-50 border-indigo-100 text-indigo-600',
  },
];

/**
 * Os três conteúdos que o painel hospeda. Cada entrada é uma rota, com o texto da marca ao
 * lado da logo, o selo, o título e o subtítulo. Acrescentar um conteúdo é acrescentar uma
 * entrada aqui — o JSX não carrega texto de modo nenhum.
 */
const PANEL_COPY = {
  login: {
    brand: 'Sua Plataforma Educacional',
    chip: 'Acesso institucional',
    title: 'Acesse seu espaço de trabalho',
    subtitle: 'Entre com suas credenciais para acompanhar alunos, matrículas e documentos.',
  },
  register: {
    brand: 'Cadastro de colégio',
    chip: 'Ambiente institucional',
    title: 'Cadastre seu colégio',
    subtitle: 'Leve a gestão escolar da sua escola para o Ciclo em três passos.',
  },
  parent: {
    brand: 'Conta de responsável',
    chip: 'Ambiente institucional',
    title: 'Crie sua conta de responsável',
    subtitle: 'Informe o código do colégio e defina seu acesso.',
  },
} as const;

type PanelMode = keyof typeof PANEL_COPY;

/** Endereço de cada conteúdo. É a rota que manda no modo, não o contrário. */
const MODE_ROUTES: Record<PanelMode, string> = {
  login: '/login',
  register: '/register',
  parent: '/register/parent',
};

function modeFromPath(pathname: string): PanelMode {
  if (pathname.startsWith(MODE_ROUTES.parent)) return 'parent';
  if (pathname.startsWith(MODE_ROUTES.register)) return 'register';
  return 'login';
}

export default function AuthPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  /**
   * Marcado por padrão: é a expectativa de quem usa o sistema todo dia. Desmarcado, a sessão
   * vira cookie de navegador e acaba ao fechar a aba — a escolha é do servidor, não do cliente.
   */
  const [remember, setRemember] = useState(true);
  const { login, isAuthenticated, user, getCurrentUser } = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { pathname } = useLocation();

  /**
   * O conteúdo vem da ROTA: `/login`, `/register` e `/register/parent` mostram esta mesma
   * tela com o painel trocado, e a URL acompanha a troca. Assim um link direto para qualquer
   * um dos três endereços abre a tela certa, e o botão voltar do navegador funciona.
   */
  const mode = modeFromPath(pathname);
  const copy = PANEL_COPY[mode];

  useEffect(() => {
    // Só o login expulsa quem já está autenticado; nas rotas de cadastro a pessoa pode estar
    // apenas consultando o formulário.
    if (mode !== 'login') return;
    if (isAuthenticated && user) {
      navigate(user.role === 'Parent' ? '/parent' : '/admin', { replace: true });
    }
  }, [mode, isAuthenticated, user, navigate]);

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
      // A entrada de demonstração respeita a mesma escolha de "lembrar" do formulário.
      await login(demoEmail, 'demo', remember);
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
      await login(email, password, remember);
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
    <div className="relative flex min-h-screen w-full flex-col items-center justify-center bg-[#eceff2] p-4 font-brand text-slate-800 antialiased md:flex-row md:items-stretch md:justify-start md:p-0">
      <ShaderBackground />

      {/*
       * Painel de login. Abaixo de `md` ele é um CARD centralizado (largura máxima fixa):
       * ocupar a tela inteira numa janela estreita deixava o formulário esticado sem
       * necessidade. A partir de `md` — metade da tela já incluída — é a MESMA coluna de
       * altura cheia do desktop: painel de largura fixa à esquerda e conteúdo aberto sobre
       * o shader à direita, só mais estreitos.
       */}
      {/*
       * No modo cadastro o painel ROLA por dentro e a barra fica escondida: o formulário é
       * longo e a barra cinza sobre o vidro quebrava a superfície. Esconder a barra não
       * desliga a rolagem — roda do mouse, toque, teclado e leitores de tela seguem iguais
       * (`.no-scrollbar`). No celular o painel também ganha altura máxima para rolar dentro
       * dele, em vez de esticar o documento inteiro.
       */}
      <aside
        className={`relative z-20 flex w-full max-w-md flex-col justify-between rounded-2xl border border-white/40 bg-white/85 p-6 shadow-xl backdrop-blur-xl sm:p-8 md:h-screen md:w-[360px] md:max-w-none md:overflow-y-auto md:rounded-none md:border-t-0 md:border-b-0 md:border-l-0 md:border-r lg:w-[410px] xl:w-[420px] ${
          mode !== 'login'
            ? 'no-scrollbar max-h-[calc(100dvh-2rem)] overflow-y-auto md:max-h-none'
            : ''
        }`}
      >
        <div>
          {/* Cabeçalho institucional */}
          <div className="mb-6 flex items-center gap-3">
            <img src="/Ciclo_favicon.png" alt="" className="h-16 w-16 shrink-0 object-contain" />
            <span className="block text-base leading-tight font-bold tracking-tight text-slate-900">
              Ciclo <span className="mx-1 font-normal text-slate-400">|</span>{' '}
              <span className="text-xs font-semibold text-slate-600">{copy.brand}</span>
            </span>
          </div>

          {/* Título e subtítulo — acompanham o conteúdo que está no painel. */}
          <div className="mb-5">
            <div className="mb-3 inline-flex items-center gap-2 rounded-full border border-indigo-100 bg-indigo-50 px-2.5 py-1 text-[11px] font-semibold text-indigo-700">
              <span className="h-1.5 w-1.5 rounded-full bg-indigo-600" />
              {copy.chip}
            </div>
            <h1 className="text-2xl font-extrabold tracking-tight text-slate-900 sm:text-3xl">
              {copy.title}
            </h1>
            <p className="mt-1.5 text-xs leading-relaxed text-slate-500 sm:text-sm">{copy.subtitle}</p>
          </div>

          {mode === 'register' ? (
            <SchoolRegisterForm onSuccess={() => navigate(MODE_ROUTES.login)} />
          ) : mode === 'parent' ? (
            <ParentRegisterForm />
          ) : (
          <form onSubmit={handleSubmit} className="space-y-3.5">
            <div>
              <label
                htmlFor="login-email"
                className="mb-1.5 block text-xs font-semibold text-slate-700"
              >
                E-mail
              </label>
              <div className="relative">
                <span className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3.5 text-slate-400">
                  <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path
                      d="M16 12a4 4 0 10-8 0 4 4 0 008 0zm0 0v1.5a2.5 2.5 0 005 0V12a9 9 0 10-9 9m4.5-1.206a8.959 8.959 0 01-4.5 1.206"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      strokeWidth="2"
                    />
                  </svg>
                </span>
                <input
                  id="login-email"
                  type="email"
                  autoComplete="email"
                  autoFocus
                  required
                  placeholder="nome@colegio.com.br"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className={`${fieldClass} pr-4`}
                />
              </div>
            </div>

            <div>
              <div className="mb-1.5 flex items-center justify-between gap-3">
                <label
                  htmlFor="login-password"
                  className="block text-xs font-semibold text-slate-700"
                >
                  Senha
                </label>
                <Link
                  to="/forgot-password"
                  className="text-xs font-semibold text-indigo-600 transition-colors hover:text-indigo-800 hover:underline"
                >
                  Esqueceu a senha?
                </Link>
              </div>
              <div className="relative">
                <span className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3.5 text-slate-400">
                  <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path
                      d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      strokeWidth="2"
                    />
                  </svg>
                </span>
                <input
                  id="login-password"
                  type={showPassword ? 'text' : 'password'}
                  autoComplete="current-password"
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className={`${fieldClass} pr-10`}
                />
                <button
                  type="button"
                  onClick={() => setShowPassword(!showPassword)}
                  aria-label={showPassword ? 'Ocultar senha' : 'Mostrar senha'}
                  aria-pressed={showPassword}
                  className="absolute inset-y-0 right-0 flex items-center pr-3.5 text-slate-400 transition-colors hover:text-slate-600"
                >
                  {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                </button>
              </div>
            </div>

            {/*
             * "Lembrar o acesso" não é enfeite: muda o TIPO do cookie de refresh no servidor
             * (persistente por 30 dias x cookie de sessão, que morre ao fechar o navegador).
             *
             * O quadradinho nativo é escondido (`sr-only`) e o desenho é o <span> ao lado: é a
             * única forma de ter um círculo com o MESMO gradiente do botão de entrar. O input
             * continua sendo o controle de verdade — teclado, leitor de tela e foco seguem
             * nele (`peer`), e o anel de foco é pintado no desenho.
             */}
            <label
              htmlFor="login-remember"
              className="flex cursor-pointer items-center gap-2 pt-1 text-xs text-slate-600 select-none"
            >
              <input
                id="login-remember"
                type="checkbox"
                checked={remember}
                onChange={(e) => setRemember(e.target.checked)}
                className="peer sr-only"
              />
              <span
                aria-hidden="true"
                className="flex h-4 w-4 shrink-0 items-center justify-center rounded-full border border-slate-300 bg-white transition-all peer-checked:border-transparent peer-checked:bg-gradient-to-r peer-checked:from-indigo-600 peer-checked:to-indigo-700 peer-focus-visible:ring-4 peer-focus-visible:ring-indigo-600/25"
              >
                <Check
                  className={`h-2.5 w-2.5 text-white transition-opacity ${remember ? 'opacity-100' : 'opacity-0'}`}
                  strokeWidth={4}
                />
              </span>
              Lembrar neste navegador por 30 dias
            </label>

            <div className="pt-2">
              <button
                type="submit"
                disabled={loading}
                className="flex w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-r from-indigo-600 to-indigo-700 px-4 py-3 text-sm font-semibold text-white shadow-md transition-all hover:-translate-y-0.5 hover:from-indigo-700 hover:to-indigo-800 hover:shadow-indigo-500/25 active:translate-y-0 disabled:translate-y-0 disabled:cursor-not-allowed disabled:opacity-60"
              >
                <span>{loading ? 'Entrando...' : 'Entrar na plataforma'}</span>
                {!loading && <ArrowRight className="h-4 w-4" />}
              </button>
            </div>

            <div className="relative flex items-center justify-center py-2">
              <div className="w-full border-t border-slate-200/90" />
              <span className="absolute rounded-full bg-white/95 px-3 text-[11px] font-medium tracking-tight text-slate-400 lowercase">
                ou acesso com Google
              </span>
            </div>

            <button
              type="button"
              onClick={handleGoogleLogin}
              className="flex w-full items-center justify-center gap-2.5 rounded-xl border border-slate-200/90 bg-white/90 px-3 py-2.5 text-xs font-semibold text-slate-700 shadow-sm transition-all hover:border-slate-300 hover:bg-white"
            >
              <svg className="h-4 w-4 shrink-0" viewBox="0 0 24 24">
                <path
                  fill="#4285F4"
                  d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92a5.06 5.06 0 0 1-2.2 3.32v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.1z"
                />
                <path
                  fill="#34A853"
                  d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z"
                />
                <path
                  fill="#FBBC05"
                  d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z"
                />
                <path
                  fill="#EA4335"
                  d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z"
                />
              </svg>
              Entrar com Google
            </button>

            {isDemoMode && (
              <div className="rounded-xl border border-violet-300 bg-violet-50 p-3">
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
          </form>
          )}
        </div>

        {/* Rodapé: a outra porta de entrada (ou a volta para o login). */}
        <div className="mt-6 space-y-2 border-t border-slate-200/70 pt-4 text-center md:mt-10">
          {mode === 'login' ? (
            <>
              <p className="text-xs text-slate-500">
                Precisa cadastrar sua instituição?{' '}
                <button
                  type="button"
                  onClick={() => navigate(MODE_ROUTES.register)}
                  className="font-semibold text-indigo-600 transition-colors hover:text-indigo-800 hover:underline"
                >
                  Cadastrar meu colégio
                </button>
              </p>
              <p className="text-xs text-slate-500">
                É responsável por um aluno?{' '}
                <button
                  type="button"
                  onClick={() => navigate(MODE_ROUTES.parent)}
                  className="font-semibold text-indigo-600 transition-colors hover:text-indigo-800 hover:underline"
                >
                  Criar conta de responsável
                </button>
              </p>
            </>
          ) : (
            <button
              type="button"
              onClick={() => navigate(MODE_ROUTES.login)}
              className="inline-flex items-center gap-1.5 text-xs font-semibold text-indigo-600 transition-colors hover:text-indigo-800 hover:underline"
            >
              <ArrowLeft className="h-3.5 w-3.5" />
              Voltar para o login
            </button>
          )}
        </div>
      </aside>

      {/* Área ampla: aberta sobre o shader. Informativa, sem nenhum controle de login. */}
      <main className="relative z-10 hidden flex-1 flex-col justify-between p-6 sm:p-8 md:flex lg:p-12">
        <header className="flex w-full items-center justify-between gap-4">
          <div className="glass-pill flex items-center gap-2 rounded-full px-3.5 py-1.5 text-xs font-medium text-slate-700 shadow-sm">
            Sua Plataforma Educacional
          </div>
        </header>

        <div className="my-auto flex flex-col items-end py-12">
          <div className="glass-card animate-float mb-8 flex max-w-md items-center gap-3.5 rounded-2xl px-5 py-4">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-indigo-100 bg-indigo-50">
              <BookOpenCheck className="h-5 w-5 text-indigo-600" />
            </div>
            <div>
              <span className="block text-[11px] font-bold tracking-wider text-indigo-700 uppercase">
                Visão pedagógica
              </span>
              <span className="text-sm font-semibold text-slate-800">
                Gestão escolar próxima e organizada.
              </span>
            </div>
          </div>

          <div className="flex w-full max-w-md flex-col gap-4">
            {CAPABILITIES.map(({ title, text, icon: Icon, tone }, index) => (
              <div
                key={title}
                className={`glass-card flex items-center gap-3.5 rounded-2xl p-4 sm:p-5 ${
                  index === 1 ? 'animate-float' : 'animate-float-delayed'
                }`}
              >
                <div
                  className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border ${tone}`}
                >
                  <Icon className="h-[22px] w-[22px]" strokeWidth={1.9} />
                </div>
                <div className="min-w-0">
                  <p className="text-xs font-medium text-slate-500">{title}</p>
                  <p className="text-sm font-bold text-slate-900">{text}</p>
                </div>
              </div>
            ))}
          </div>
        </div>

        <footer className="flex w-full items-center justify-between gap-3 pt-4 text-xs text-slate-600">
          <span className="font-medium text-slate-700">
            © 2026 Ciclo Plataforma Educacional
          </span>
          <span className="hidden sm:inline">Todos os direitos reservados</span>
        </footer>
      </main>
    </div>
  );
}
