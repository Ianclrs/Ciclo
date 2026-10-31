import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';

interface PageTitleRule {
  prefix: string;
  title: string;
}

// Ordem importa: rotas mais específicas primeiro.
const PAGE_TITLE_RULES: PageTitleRule[] = [
  { prefix: '/login', title: 'Login' },
  { prefix: '/auth/google', title: 'Login' },
  // O casamento aceita prefixo com barra, então `/register/parent` também casaria com
  // `/register`. A rota mais específica vem antes — e os nomes são os mesmos que aparecem
  // ao lado da logo na tela de entrada (`PANEL_COPY` em `AuthPage`).
  { prefix: '/register/parent', title: 'Conta de responsável' },
  { prefix: '/register', title: 'Cadastro de colégio' },
  { prefix: '/admin/students', title: 'Alunos' },
  { prefix: '/admin/enrollments', title: 'Matrículas' },
  { prefix: '/admin/documents', title: 'Documentos' },
  { prefix: '/admin/notifications', title: 'Notificações' },
  { prefix: '/admin', title: 'Dashboard' },
  { prefix: '/parent/notifications', title: 'Notificações' },
  { prefix: '/parent/children', title: 'Filhos' },
  { prefix: '/parent', title: 'Dashboard' },
];

function resolvePageName(pathname: string): string {
  for (const rule of PAGE_TITLE_RULES) {
    if (pathname === rule.prefix || pathname.startsWith(`${rule.prefix}/`)) {
      return rule.title;
    }
  }
  return '';
}

export default function DocumentTitle() {
  const location = useLocation();

  useEffect(() => {
    const pageName = resolvePageName(location.pathname);
    document.title = pageName ? `Ciclo | ${pageName}` : 'Ciclo';
  }, [location.pathname]);

  return null;
}
