import { useCallback, useState, useEffect, useLayoutEffect, useRef, type ChangeEvent, type ReactNode } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { ChevronRight, Settings } from 'lucide-react';
import toast from 'react-hot-toast';
import { useAuth } from '../hooks/useAuth';
import { useLocale } from '../i18n/LocaleContext';
import type { MessageKey } from '../i18n/messages';
import { Button } from './Button';
import { Badge } from './Badge';
import { Modal } from './Modal';
import { StudentAvatar } from './StudentAvatar';
import { ShaderBackground } from './ShaderBackground';
import { TopbarActions } from './TopbarActions';
import { apiErrorMessage } from '../api/errors';
import { toResizedDataUrl } from '../utils/image';
import { cutoutMask, type CutoutRect } from './sidebarCutout';
import type { Accent } from './accentStyles';
import type { TenantFeatureName } from '../domain/features';
import type { User } from '../types';

/** Rótulo do papel no idioma ativo — o valor da API (`Admin`) não é texto para tela. */
const ROLE_KEYS: Record<User['role'], MessageKey> = {
  Admin: 'role.admin',
  Staff: 'role.staff',
  Parent: 'role.parent',
};

export interface SidebarItem {
  /** Chave do catálogo: o texto é resolvido no idioma ativo, não guardado no item. */
  labelKey: MessageKey;
  path: string;
  icon: ReactNode;
  color?: Accent;
  /**
   * Funcionalidade que o item exige (ADR-200-2). Sem ela, o item não aparece — e a rota,
   * aberta direto, recebe 403 `feature_disabled` da API.
   */
  feature?: TenantFeatureName;
}

/**
 * Nem o item ativo nem o hover pintam fundo: o item ativo fica dentro do recorte, onde o
 * shader aparece cru, e um pastel de hover ao lado dele competia com o próprio vinco.
 * O destaque é só a cor de acento no texto e no ícone.
 *
 * A chave é `Accent`: uma cor só existe no menu se tiver estado definido aqui, então uma
 * cor nova na paleta obriga a aparecer nesta tabela (e uma cor fora da paleta não compila).
 */
const colorMap: Record<Accent, { active: string; hover: string }> = {
  violet: { active: 'text-violet-700', hover: 'hover:text-violet-700' },
  blue:   { active: 'text-blue-700',   hover: 'hover:text-blue-700' },
  teal:   { active: 'text-teal-700',   hover: 'hover:text-teal-700' },
  green:  { active: 'text-green-700',  hover: 'hover:text-green-700' },
  amber:  { active: 'text-amber-700',  hover: 'hover:text-amber-700' },
  red:    { active: 'text-red-700',    hover: 'hover:text-red-700' },
  cyan:   { active: 'text-cyan-700',   hover: 'hover:text-cyan-700' },
  indigo: { active: 'text-indigo-700', hover: 'hover:text-indigo-700' },
  sky:    { active: 'text-sky-700',    hover: 'hover:text-sky-700' },
};

interface SidebarProps {
  items: SidebarItem[];
  children: ReactNode;
  /** Rota de notificações do perfil: o sino da topbar leva a ela. */
  notificationsTo: string;
}

export function Sidebar({ items, children, notificationsTo }: SidebarProps) {
  const [mobileOpen, setMobileOpen] = useState(false);
  /**
   * Abre aberta. O menu é a única navegação da área logada: começar recolhida obrigava a procurar
   * o botão que o revela antes de qualquer clique. Recolher continua disponível, e a escolha vale
   * para a sessão corrente — não é preferência persistida, então recarregar volta a abrir.
   */
  const [desktopCollapsed, setDesktopCollapsed] = useState(false);
  /**
   * Ficha da conta. Antes, o bloco de perfil no rodapé era só texto: mostrava nome e papel, sem
   * nada a clicar. Agora ele abre o diálogo da conta — dados de quem está logado, atalho para as
   * configurações (Admin) e a saída do sistema, no mesmo lugar.
   */
  const [accountOpen, setAccountOpen] = useState(false);
  /** Enquanto a foto sobe, os controles ficam desabilitados (evita dois envios concorrentes). */
  const [photoBusy, setPhotoBusy] = useState(false);
  const location = useLocation();
  const navigate = useNavigate();
  const { user, logout, updatePhoto } = useAuth();
  const { t, language } = useLocale();
  const desktopAsideRef = useRef<HTMLElement>(null);
  const mobileAsideRef = useRef<HTMLElement>(null);

  const instantTimer = useRef<number | null>(null);

  /**
   * Desliga a transição do furo por um instante. Rolagem e redimensionamento mexem
   * na medida quadro a quadro: animar ali criaria atraso entre o dedo e o recorte.
   */
  const holdInstant = useCallback((surfaces: HTMLElement[]) => {
    for (const surface of surfaces) surface.setAttribute('data-hole-instant', '');
    if (instantTimer.current !== null) window.clearTimeout(instantTimer.current);
    instantTimer.current = window.setTimeout(() => {
      for (const surface of surfaces) surface.removeAttribute('data-hole-instant');
      instantTimer.current = null;
    }, 160);
  }, []);

  /**
   * Recorta a camada de vidro na faixa do item ativo: começa no canto arredondado da
   * pílula e vai até a borda direita da sidebar, abrindo o recorte para o fundo do
   * conteúdo.
   *
   * As duas asides (desktop e mobile) renderizam o MESMO `navContent`, então a
   * medição é feita por DOM dentro de cada uma — um ref compartilhado apontaria
   * só para a última montada. `getBoundingClientRect` já considera a rolagem do
   * menu e a posição da gaveta no mobile.
   *
   * `instant` desliga a transição nesta passagem; o padrão é animar, que é o que faz
   * o recorte deslizar de um item para o outro.
   */
  const syncCutout = useCallback((instant = false) => {
    const surfaces: HTMLElement[] = [];
    const measurements: Array<{ surface: HTMLElement; rect: CutoutRect | null }> = [];

    for (const aside of [desktopAsideRef.current, mobileAsideRef.current]) {
      if (!aside) continue;

      const surface = aside.querySelector<HTMLElement>('[data-sidebar-surface]');
      if (!surface) continue;
      surfaces.push(surface);

      const active = aside.querySelector<HTMLElement>('[aria-current="page"]');
      if (!active) {
        measurements.push({ surface, rect: null });
        continue;
      }

      const surfaceBox = surface.getBoundingClientRect();
      const activeBox = active.getBoundingClientRect();
      // A borda direita vem do MENU, não da superfície: a superfície anima a largura ao
      // abrir/fechar, e medir ali produziria um furo de tamanho errado durante a animação.
      // A largura do menu é fixa e a mesma da superfície quando a sidebar está aberta.
      const navBox = aside.querySelector('nav')?.getBoundingClientRect();
      measurements.push({
        surface,
        rect: {
          x: activeBox.left - surfaceBox.left,
          y: activeBox.top - surfaceBox.top,
          width: (navBox ? navBox.right : activeBox.right) - activeBox.left,
          height: activeBox.height,
        },
      });
    }

    // O atributo vem ANTES das medidas: na mesma recalculada de estilo a troca de
    // posição já acontece sem transição.
    if (instant) holdInstant(surfaces);

    for (const { surface, rect } of measurements) {
      if (!rect || rect.width <= 0 || rect.height <= 0) {
        surface.removeAttribute('data-hole');
        continue;
      }
      // A máscara é mais alta que o item: a abertura do lado direito cresce para fora,
      // então posição e altura vêm da caixa calculada, não do retângulo medido.
      const mask = cutoutMask(rect);
      surface.style.setProperty('--hole-x', `${mask.box.x}px`);
      surface.style.setProperty('--hole-y', `${mask.box.y}px`);
      surface.style.setProperty('--hole-w', `${mask.box.width}px`);
      surface.style.setProperty('--hole-h', `${mask.box.height}px`);
      surface.style.setProperty('--hole-mask', mask.image);
      surface.setAttribute('data-hole', 'on');
    }
  }, [holdInstant]);

  // Remede animando quando o item ativo muda; sem animação ao rolar ou redimensionar.
  useLayoutEffect(() => {
    syncCutout();

    const instantSync = () => syncCutout(true);
    window.addEventListener('resize', instantSync);

    const navs = [desktopAsideRef.current, mobileAsideRef.current]
      .map((aside) => aside?.querySelector('nav'))
      .filter((nav): nav is HTMLElement => Boolean(nav));
    for (const nav of navs) nav.addEventListener('scroll', instantSync, { passive: true });

    return () => {
      window.removeEventListener('resize', instantSync);
      for (const nav of navs) nav.removeEventListener('scroll', instantSync);
    };
  }, [syncCutout, location.pathname, desktopCollapsed, mobileOpen, items]);

  useEffect(() => () => {
    if (instantTimer.current !== null) window.clearTimeout(instantTimer.current);
  }, []);

  // Fecha mobile ao trocar de rota
  useEffect(() => {
    setMobileOpen(false);
  }, [location.pathname]);

  // Fecha com tecla Escape
  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setMobileOpen(false);
        setDesktopCollapsed(true);
      }
    };
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, []);

  // Bloqueia scroll do body quando overlay mobile está aberto
  useEffect(() => {
    document.body.style.overflow = mobileOpen ? 'hidden' : '';
    return () => { document.body.style.overflow = ''; };
  }, [mobileOpen]);

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  /** Reduz no navegador e envia. A API devolve o usuário já com a foto; o contexto propaga. */
  async function handlePhotoChange(event: ChangeEvent<HTMLInputElement>): Promise<void> {
    const file = event.target.files?.[0];
    // Limpa a seleção para que escolher o MESMO arquivo de novo dispare `change` outra vez.
    event.target.value = '';
    if (!file) return;

    setPhotoBusy(true);
    try {
      const dataUrl = await toResizedDataUrl(file);
      await updatePhoto(dataUrl);
      toast.success(t('profile.photoSaved'));
    } catch (error: unknown) {
      toast.error(apiErrorMessage(error, t('profile.photoError'), language));
    } finally {
      setPhotoBusy(false);
    }
  }

  async function handlePhotoRemove(): Promise<void> {
    setPhotoBusy(true);
    try {
      await updatePhoto(null);
      toast.success(t('profile.photoSaved'));
    } catch (error: unknown) {
      toast.error(apiErrorMessage(error, t('profile.photoError'), language));
    } finally {
      setPhotoBusy(false);
    }
  }

  const toggle = () => {
    if (window.innerWidth < 768) {
      setMobileOpen((prev) => !prev);
    } else {
      setDesktopCollapsed((prev) => !prev);
    }
  };

  // Estado de abertura que o botão da logo controla — só serve para o rótulo acessível.
  const menuOpen = mobileOpen || !desktopCollapsed;

  const isActive = (path: string) => {
    if (path === '/admin' || path === '/parent') {
      return location.pathname === path;
    }
    return location.pathname.startsWith(path);
  };

  const navContent = (
    <>
      <nav className="no-scrollbar flex-1 p-3 space-y-0.5 overflow-y-auto">
        {items.map((item) => {
          const colors = colorMap[item.color ?? 'violet'];
          const active = isActive(item.path);
          return (
          <Link
            key={item.path}
            to={item.path}
            aria-current={active ? 'page' : undefined}
            className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-colors ${
              active ? `${colors.active} font-semibold` : `font-medium text-stone-600 ${colors.hover}`
            }`}
          >
            <span className="shrink-0">{item.icon}</span>
            <span className="truncate">{t(item.labelKey)}</span>
          </Link>
          )
        })}
      </nav>
      <div className="p-4 border-t border-stone-200 shrink-0">
        <button
          type="button"
          onClick={() => setAccountOpen(true)}
          aria-haspopup="dialog"
          className="flex w-full items-center gap-3 rounded-lg p-1.5 text-left transition-colors hover:bg-white/70 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
        >
          <StudentAvatar name={user?.name ?? ''} photo={user?.foto} size="sm" />
          <span className="flex-1 min-w-0">
            <span className="block text-sm font-medium text-stone-800 truncate">{user?.name}</span>
            <span className="block text-xs text-stone-500 truncate">
              {user ? t(ROLE_KEYS[user.role]) : ''}
            </span>
          </span>
        </button>
        <Button
          variant="ghost"
          size="sm"
          className="mt-3 w-full justify-center bg-white text-stone-700 shadow-sm hover:bg-red-100 hover:text-red-700 font-medium transition-all"
          onClick={handleLogout}
        >
          {t('nav.logout')}
        </Button>
      </div>
    </>
  );

  return (
    <div className="relative flex h-screen bg-[#eceff2] overflow-hidden">
      {/* Fundo do layout: aparece na topbar (transparente) e na área de conteúdo. */}
      <ShaderBackground />

      {/* ===== DESKTOP SIDEBAR — altura total, sem recorte da topbar ===== */}
      <aside
        ref={desktopAsideRef}
        className={`relative z-10 hidden md:flex md:flex-col h-full transition-all duration-300 ease-out overflow-hidden ${
          desktopCollapsed ? 'md:w-0' : 'md:w-52'
        }`}
      >
        {/* Camada de vidro na tinta dos cards (`bg-white/70`), sem sombra nem desfoque:
            a borda reta é justamente o que o recorte do item ativo abre. */}
        <div
          data-sidebar-surface
          aria-hidden="true"
          className="sidebar-cutout bg-white/70 absolute inset-0"
        />
        {/* Largura fixa: evita o texto reflowar durante a animação de abertura. */}
        <div className="relative w-52 flex flex-col h-full">{navContent}</div>
      </aside>

      {/* ===== COLUNA DE CONTEÚDO: topbar + main ===== */}
      <div className="relative z-10 flex min-w-0 flex-1 flex-col">
        {/* ===== TOP BAR — apenas à direita da sidebar ===== */}
        <div className="flex items-center h-16 px-4 shrink-0">
          {/* A logo é o controle da sidebar: mesma ação do antigo botão hambúrguer. */}
          <button
            onClick={toggle}
            aria-label={menuOpen ? t('nav.closeMenu') : t('nav.openMenu')}
            aria-expanded={menuOpen}
            className="-ml-2 flex items-center gap-2 rounded-xl p-1 pr-2 hover:bg-white/40 active:scale-95 transition-all"
          >
            <img src="/Ciclo_favicon.png" alt="" className="w-12 h-12 shrink-0" />
            {/* A marca fica ao lado da logo, e o botão segue sendo o controle do menu: o
                `aria-label` sobrepõe o conteúdo, então o leitor de tela continua ouvindo
                "abrir/fechar menu", não o nome. A fonte é a mesma da marca no login. */}
            <span className="font-brand text-lg font-bold tracking-tight text-stone-800">
              Ciclo
            </span>
          </button>

          <TopbarActions notificationsTo={notificationsTo} />
        </div>

        {/* ===== MAIN CONTENT ===== */}
        <main className="no-scrollbar flex-1 overflow-auto">
          <div className="p-4 md:p-6">
            {/*
              `key` pela rota: a camada remonta a cada seção e a animação de entrada
              reinicia. Sem a chave a classe não reaplicaria e a troca seria seca.
              Nenhuma navegação atual troca só o parâmetro da mesma rota, então a
              remontagem não descarta estado que estivesse sendo editado.
            */}
            <div key={location.pathname} className="section-enter">
              {children}
            </div>
          </div>
        </main>
      </div>

      {/* ===== MOBILE BACKDROP ===== */}
      <div
        className={`fixed inset-0 z-40 bg-black/50 transition-opacity duration-300 ease-out md:hidden ${
          mobileOpen ? 'opacity-100' : 'opacity-0 pointer-events-none'
        }`}
        onClick={() => setMobileOpen(false)}
        aria-hidden="true"
      />

      {/* ===== MOBILE SIDEBAR OVERLAY — também em altura total ===== */}
      <aside
        ref={mobileAsideRef}
        className={`fixed inset-y-0 left-0 z-50 w-64 max-w-[85vw] flex flex-col transform transition-transform duration-300 ease-out md:hidden ${
          mobileOpen ? 'translate-x-0' : '-translate-x-full'
        }`}
      >
        <div
          data-sidebar-surface
          aria-hidden="true"
          className="sidebar-cutout bg-white/70 absolute inset-0"
        />
        <div className="relative w-full flex flex-col h-full">{navContent}</div>
      </aside>

      {/* Uma instância só: `navContent` é renderizado nas duas asides, e o diálogo duplicado
          criaria dois `role="dialog"` no DOM. */}
      <Modal
        open={accountOpen}
        onClose={() => setAccountOpen(false)}
        title={t('nav.account')}
        closeLabel={t('common.close')}
      >
        {/* Identidade primeiro: quem está logado é a resposta da pergunta que abriu o diálogo. */}
        <div className="flex items-center gap-4">
          <StudentAvatar name={user?.name ?? ''} photo={user?.foto} size="lg" />
          <div className="min-w-0 flex-1">
            <p className="truncate text-base font-semibold text-gray-900">{user?.name}</p>
            <p className="mt-0.5 truncate text-xs text-gray-500">{user?.email}</p>
          </div>
          {user && <Badge variant="info">{t(ROLE_KEYS[user.role])}</Badge>}
        </div>

        {/* A foto é a mesma da lista de usuários: trocar aqui muda o reconhecimento em todo o app. */}
        <div className="mt-4 flex flex-wrap items-center gap-2">
          <label
            className={`inline-flex cursor-pointer items-center rounded-lg bg-gray-100 px-3 py-1.5 text-xs font-medium text-gray-700 transition-colors hover:bg-gray-200 focus-within:ring-2 focus-within:ring-indigo-500 ${
              photoBusy ? 'pointer-events-none opacity-60' : ''
            }`}
          >
            <input
              type="file"
              accept="image/png,image/jpeg,image/webp"
              className="sr-only"
              onChange={handlePhotoChange}
              disabled={photoBusy}
            />
            {t('profile.changePhoto')}
          </label>
          {user?.foto && (
            <button
              type="button"
              onClick={handlePhotoRemove}
              disabled={photoBusy}
              className="rounded-lg px-3 py-1.5 text-xs font-medium text-red-700 transition-colors hover:bg-red-50 disabled:opacity-60"
            >
              {t('profile.removePhoto')}
            </button>
          )}
        </div>

        {user?.role === 'Admin' && (
          <div className="mt-5 border-t border-gray-200 pt-3">
            <button
              type="button"
              onClick={() => {
                setAccountOpen(false);
                navigate('/admin/settings');
              }}
              className="flex w-full items-center gap-3 rounded-lg px-2 py-2.5 text-left text-sm font-medium text-gray-700 transition-colors hover:bg-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
            >
              <Settings size={18} className="shrink-0 text-gray-500" aria-hidden="true" />
              <span className="flex-1">{t('nav.settings')}</span>
              <ChevronRight size={16} className="shrink-0 text-gray-400" aria-hidden="true" />
            </button>
          </div>
        )}
      </Modal>
    </div>
  );
}
