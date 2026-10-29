import { useState, useEffect, type ReactNode } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';
import { Button } from './Button';
import { ShaderBackground } from './ShaderBackground';
import { TopbarActions } from './TopbarActions';

interface SidebarItem {
  label: string;
  path: string;
  icon: ReactNode;
  color?: 'blue' | 'green' | 'amber' | 'red' | 'violet';
}

const colorMap: Record<string, { active: string; hover: string }> = {
  blue:   { active: 'bg-blue-50 text-blue-700',     hover: 'hover:bg-blue-50 hover:text-blue-700' },
  green:  { active: 'bg-green-50 text-green-700',   hover: 'hover:bg-green-50 hover:text-green-700' },
  amber:  { active: 'bg-amber-50 text-amber-700',   hover: 'hover:bg-amber-50 hover:text-amber-700' },
  red:    { active: 'bg-red-50 text-red-700',       hover: 'hover:bg-red-50 hover:text-red-700' },
  violet: { active: 'bg-violet-50 text-violet-700', hover: 'hover:bg-violet-50 hover:text-violet-700' },
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
            className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors ${
              active ? colors.active : `text-stone-600 ${colors.hover}`
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
        className={`relative z-10 hidden md:flex md:flex-col h-full bg-stone-200/70 backdrop-blur-md shadow-lg transition-all duration-300 ease-out overflow-hidden ${
          desktopCollapsed ? 'md:w-0' : 'md:w-52'
        }`}
      >
        {/* Largura fixa: evita o texto reflowar durante a animação de abertura. */}
        <div className="w-52 flex flex-col h-full">{navContent}</div>
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
        className={`fixed inset-y-0 left-0 z-50 w-64 max-w-[85vw] bg-stone-200/70 backdrop-blur-md flex flex-col shadow-lg transform transition-transform duration-300 ease-out md:hidden ${
          mobileOpen ? 'translate-x-0' : '-translate-x-full'
        }`}
      >
        {navContent}
      </aside>
    </div>
  );
}
