import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { useLocale } from '../i18n/LocaleContext';
import type { MessageKey } from '../i18n/messages';

interface PageTitleRule {
  prefix: string;
  titleKey: MessageKey;
}

// Ordem importa: rotas mais específicas primeiro.
const PAGE_TITLE_RULES: PageTitleRule[] = [
  { prefix: '/login', titleKey: 'nav.login' },
  { prefix: '/auth/google', titleKey: 'nav.login' },
  // O casamento aceita prefixo com barra, então `/register/parent` também casaria com
  // `/register`. A rota mais específica vem antes — e os nomes são as MESMAS chaves que
  // aparecem ao lado da logo na tela de entrada (`PANEL_COPY` em `AuthPage`), então aba e
  // painel não podem divergir nem ao mudar de idioma.
  { prefix: '/register/parent', titleKey: 'auth.brand.parent' },
  { prefix: '/register', titleKey: 'auth.brand.register' },
  { prefix: '/admin/students', titleKey: 'nav.students' },
  { prefix: '/admin/enrollments', titleKey: 'nav.enrollments' },
  { prefix: '/admin/enrollment-renewal', titleKey: 'renewal.title' },
  { prefix: '/admin/documents', titleKey: 'nav.documents' },
  { prefix: '/admin/notifications', titleKey: 'nav.notifications' },
  { prefix: '/admin/academico', titleKey: 'nav.academic' },
  { prefix: '/admin/notas', titleKey: 'grades.title' },
  { prefix: '/admin/users', titleKey: 'nav.users' },
  { prefix: '/admin/settings', titleKey: 'nav.settings' },
  { prefix: '/admin/privacy', titleKey: 'nav.privacy' },
  { prefix: '/admin', titleKey: 'nav.dashboard' },
  { prefix: '/parent/notifications', titleKey: 'nav.notifications' },
  { prefix: '/parent/children', titleKey: 'parent.children' },
  { prefix: '/parent/privacy', titleKey: 'nav.privacy' },
  { prefix: '/parent', titleKey: 'nav.dashboard' },
];

function resolvePageKey(pathname: string): MessageKey | null {
  for (const rule of PAGE_TITLE_RULES) {
    if (pathname === rule.prefix || pathname.startsWith(`${rule.prefix}/`)) {
      return rule.titleKey;
    }
  }
  return null;
}

export default function DocumentTitle() {
  const location = useLocation();
  const { language, t } = useLocale();

  useEffect(() => {
    const pageKey = resolvePageKey(location.pathname);
    document.title = pageKey ? `Ciclo | ${t(pageKey)}` : 'Ciclo';
  }, [location.pathname, t, language]);

  return null;
}
