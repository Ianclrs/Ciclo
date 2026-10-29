import { useCallback, useState, useEffect, useLayoutEffect, useRef, type ReactNode } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';
import { Button } from './Button';
import { ShaderBackground } from './ShaderBackground';
import { TopbarActions } from './TopbarActions';
import { cutoutMask, type CutoutRect } from './sidebarCutout';
import type { Accent } from './accentStyles';

interface SidebarItem {
  label: string;
  path: string;
  icon: ReactNode;
  color?: Accent;
}

/**
 * O item ativo NÃO recebe fundo: ele fica dentro do recorte, onde o shader aparece cru.
 * O destaque é a cor de acento no texto e no ícone — um pastel ali sumiria sobre o fundo vivo.
 *
 * A chave é `Accent`: uma cor só existe no menu se tiver estado definido aqui, então uma
 * cor nova na paleta obriga a aparecer nesta tabela (e uma cor fora da paleta não compila).
 */
const colorMap: Record<Accent, { active: string; hover: string }> = {
  violet: { active: 'text-violet-700', hover: 'hover:bg-violet-50 hover:text-violet-700' },
  blue:   { active: 'text-blue-700',   hover: 'hover:bg-blue-50 hover:text-blue-700' },
  teal:   { active: 'text-teal-700',   hover: 'hover:bg-teal-50 hover:text-teal-700' },
  green:  { active: 'text-green-700',  hover: 'hover:bg-green-50 hover:text-green-700' },
  amber:  { active: 'text-amber-700',  hover: 'hover:bg-amber-50 hover:text-amber-700' },
  red:    { active: 'text-red-700',    hover: 'hover:bg-red-50 hover:text-red-700' },
};

interface SidebarProps {
  items: SidebarItem[];
  children: ReactNode;
  /** Rota de notificações do perfil: o sino da topbar leva a ela. */
  notificationsTo: string;
}

export function Sidebar({ items, children, notificationsTo }: SidebarProps) {
  const [mobileOpen, setMobileOpen] = useState(false);
  const [desktopCollapsed, setDesktopCollapsed] = useState(true);
  const location = useLocation();
  const navigate = useNavigate();
  const { user, logout } = useAuth();
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
            <span className="truncate">{item.label}</span>
          </Link>
          )
        })}
      </nav>
      <div className="p-4 border-t border-stone-200 shrink-0">
        <div className="flex items-center gap-3 mb-3">
          <div className="w-8 h-8 rounded-full bg-stone-300 flex items-center justify-center text-stone-700 font-semibold text-sm shrink-0">
            {user?.name?.[0]?.toUpperCase()}
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-sm font-medium text-stone-800 truncate">{user?.name}</p>
            <p className="text-xs text-stone-500">{user?.role}</p>
          </div>
        </div>
        <Button variant="ghost" size="sm" className="w-full justify-center bg-white text-stone-700 shadow-sm hover:bg-red-100 hover:text-red-700 font-medium transition-all" onClick={handleLogout}>
          Sair
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
            aria-label={menuOpen ? 'Fechar menu' : 'Abrir menu'}
            aria-expanded={menuOpen}
            className="-ml-2 rounded-xl p-1 hover:bg-white/40 active:scale-95 transition-all"
          >
            <img src="/Ciclo_favicon.png" alt="" className="w-12 h-12 shrink-0" />
          </button>

          <TopbarActions notificationsTo={notificationsTo} />
        </div>

        {/* ===== MAIN CONTENT ===== */}
        <main className="no-scrollbar flex-1 overflow-auto">
          <div className="p-4 md:p-6">
            {children}
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
    </div>
  );
}
