import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { Toaster } from 'react-hot-toast';
import { AuthProvider } from './context/AuthContext';
import { LocaleProvider } from './i18n/LocaleContext';
import { ErrorBoundary } from './components/ErrorBoundary';
import { ProtectedRoute } from './components/ProtectedRoute';
import { SplashGate } from './components/SplashGate';
import { Sidebar, type SidebarItem } from './components/Sidebar';
import DocumentTitle from './components/DocumentTitle';
import { LayoutDashboard, Users, FileText, Bell, GraduationCap, UserCog, BookOpen, Settings, Shield } from 'lucide-react';
import { useAuth } from './hooks/useAuth';
import AuthPage from './pages/AuthPage';
import ForgotPasswordPage from './pages/ForgotPasswordPage';
import AdminDashboard from './pages/admin/AdminDashboard';
import StudentList from './pages/admin/StudentList';
import StudentForm from './pages/admin/StudentForm';
import StudentDetail from './pages/admin/StudentDetail';
import EnrollmentList from './pages/admin/EnrollmentList';
import EnrollmentDetail from './pages/admin/EnrollmentDetail';
import EnrollmentRenewal from './pages/admin/EnrollmentRenewal';
import DocumentVerification from './pages/admin/DocumentVerification';
import NotificationList from './pages/admin/NotificationList';
import NotificationCreate from './pages/admin/NotificationCreate';
import UserManagement from './pages/admin/UserManagement';
import AcademicStructure from './pages/admin/AcademicStructure';
import GradeEntry from './pages/admin/GradeEntry';
import TenantSettings from './pages/admin/TenantSettings';
import PrivacyRequests from './pages/admin/PrivacyRequests';
import ParentDashboard from './pages/parent/ParentDashboard';
import ChildDetail from './pages/parent/ChildDetail';
import ChildDocuments from './pages/parent/ChildDocuments';
import NotificationInbox from './pages/parent/NotificationInbox';
import PrivacyCenter from './pages/parent/PrivacyCenter';

/**
 * Menu por perfil. O rótulo é uma chave do catálogo (`i18n/messages.ts`), resolvida pela
 * `Sidebar` com o idioma ativo: o mesmo menu aparece em pt, en e es sem lista duplicada.
 */
const adminItems: SidebarItem[] = [
  { labelKey: 'nav.dashboard', path: '/admin', icon: <LayoutDashboard size={20} />, color: 'violet' },
  { labelKey: 'nav.students', path: '/admin/students', icon: <Users size={20} />, color: 'blue' },
  { labelKey: 'nav.enrollments', path: '/admin/enrollments', icon: <GraduationCap size={20} />, color: 'green' },
  { labelKey: 'nav.documents', path: '/admin/documents', icon: <FileText size={20} />, color: 'amber' },
  { labelKey: 'nav.academic', path: '/admin/academico', icon: <BookOpen size={20} />, color: 'cyan' },
  { labelKey: 'nav.notifications', path: '/admin/notifications', icon: <Bell size={20} />, color: 'red' },
];

const parentItems: SidebarItem[] = [
  { labelKey: 'nav.dashboard', path: '/parent', icon: <LayoutDashboard size={20} />, color: 'violet' },
  { labelKey: 'nav.notifications', path: '/parent/notifications', icon: <Bell size={20} />, color: 'red' },
  { labelKey: 'nav.privacy', path: '/parent/privacy', icon: <Shield size={20} />, color: 'indigo' },
];

function AdminLayout() {
  const { user } = useAuth();
  const items: SidebarItem[] = user?.role === 'Admin'
    ? [
        ...adminItems,
        { labelKey: 'nav.users', path: '/admin/users', icon: <UserCog size={20} />, color: 'teal' },
        { labelKey: 'nav.settings', path: '/admin/settings', icon: <Settings size={20} />, color: 'indigo' },
        { labelKey: 'nav.privacy', path: '/admin/privacy', icon: <Shield size={20} />, color: 'sky' },
      ]
    : adminItems;

  return (
    <Sidebar items={items} notificationsTo="/admin/notifications">
      <Routes>
          <Route index element={<AdminDashboard />} />
          <Route path="students" element={<StudentList />} />
          <Route path="students/new" element={<StudentForm />} />
          <Route path="students/:id" element={<StudentDetail />} />
          <Route path="students/:id/edit" element={<StudentForm />} />
          <Route path="enrollments" element={<EnrollmentList />} />
          <Route path="enrollments/:id" element={<EnrollmentDetail />} />
          <Route path="enrollment-renewal" element={<EnrollmentRenewal />} />
          <Route path="documents" element={<DocumentVerification />} />
          <Route path="academico" element={<AcademicStructure />} />
          <Route path="notas" element={<GradeEntry />} />
          <Route path="notifications" element={<NotificationList />} />
          <Route path="notifications/create" element={<NotificationCreate />} />
          <Route path="users" element={<UserManagement />} />
          <Route
            path="settings"
            element={
              <ProtectedRoute roles={['Admin']}>
                <TenantSettings />
              </ProtectedRoute>
            }
          />
          <Route
            path="privacy"
            element={
              <ProtectedRoute roles={['Admin']}>
                <PrivacyRequests />
              </ProtectedRoute>
            }
          />
        </Routes>
    </Sidebar>
  );
}

function ParentLayout() {
  return (
    <Sidebar items={parentItems} notificationsTo="/parent/notifications">
        <Routes>
          <Route index element={<ParentDashboard />} />
          <Route path="children/:id" element={<ChildDetail />} />
          <Route path="children/:id/documents" element={<ChildDocuments />} />
          <Route path="notifications" element={<NotificationInbox />} />
          <Route path="privacy" element={<PrivacyCenter />} />
        </Routes>
    </Sidebar>
  );
}

export default function App() {
  return (
    <ErrorBoundary>
    <LocaleProvider>
      <BrowserRouter>
        <DocumentTitle />
        <AuthProvider>
        <Toaster position="top-right" />
        <Routes>
          {/* Uma tela, três endereços: a rota define o que o painel mostra (login, cadastro
              de colégio, cadastro de responsável). Trocar de conteúdo muda a URL.

              As três passam pelo `SplashGate` porque ele é a porta de entrada do app: quem
              abre `/register` direto na barra de endereços veria a splash só depois, ao voltar
              para o login. Ele aparece uma vez por sessão (`sessionStorage`), então trocar de
              painel depois disso não mostra a abertura de novo.

              `/auth/google/callback` fica FORA: é retorno de redirecionamento com token na
              URL, e precisa processar a sessão sem uma tela para dispensar antes. */}
          <Route path="/login" element={<SplashGate><AuthPage /></SplashGate>} />
          <Route path="/forgot-password" element={<ForgotPasswordPage />} />
          <Route path="/register" element={<SplashGate><AuthPage /></SplashGate>} />
          <Route path="/register/parent" element={<SplashGate><AuthPage /></SplashGate>} />
          <Route path="/auth/google/callback" element={<AuthPage />} />
          <Route path="/admin/*" element={<ProtectedRoute roles={['Admin', 'Staff']}><AdminLayout /></ProtectedRoute>} />
          <Route path="/parent/*" element={<ProtectedRoute roles={['Parent']}><ParentLayout /></ProtectedRoute>} />
          <Route path="*" element={<Navigate to="/login" replace />} />
        </Routes>
        </AuthProvider>
      </BrowserRouter>
    </LocaleProvider>
    </ErrorBoundary>
  );
}
