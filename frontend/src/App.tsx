import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { Toaster } from 'react-hot-toast';
import { AuthProvider } from './context/AuthContext';
import { ProtectedRoute } from './components/ProtectedRoute';
import { SplashGate } from './components/SplashGate';
import { Sidebar } from './components/Sidebar';
import DocumentTitle from './components/DocumentTitle';
import { LayoutDashboard, Users, FileText, Bell, GraduationCap } from 'lucide-react';
import LoginPage from './pages/LoginPage';
import ForgotPasswordPage from './pages/ForgotPasswordPage';
import RegisterPage from './pages/RegisterPage';
import AdminDashboard from './pages/admin/AdminDashboard';
import StudentList from './pages/admin/StudentList';
import StudentForm from './pages/admin/StudentForm';
import StudentDetail from './pages/admin/StudentDetail';
import EnrollmentList from './pages/admin/EnrollmentList';
import EnrollmentDetail from './pages/admin/EnrollmentDetail';
import DocumentVerification from './pages/admin/DocumentVerification';
import NotificationList from './pages/admin/NotificationList';
import NotificationCreate from './pages/admin/NotificationCreate';
import ParentDashboard from './pages/parent/ParentDashboard';
import ChildDetail from './pages/parent/ChildDetail';
import ChildDocuments from './pages/parent/ChildDocuments';
import NotificationInbox from './pages/parent/NotificationInbox';

const adminItems = [
  { label: 'Dashboard', path: '/admin', icon: <LayoutDashboard size={20} />, color: 'violet' as const },
  { label: 'Alunos', path: '/admin/students', icon: <Users size={20} />, color: 'blue' as const },
  { label: 'Matrículas', path: '/admin/enrollments', icon: <GraduationCap size={20} />, color: 'green' as const },
  { label: 'Documentos', path: '/admin/documents', icon: <FileText size={20} />, color: 'amber' as const },
  { label: 'Notificações', path: '/admin/notifications', icon: <Bell size={20} />, color: 'red' as const },
];

const parentItems = [
  { label: 'Dashboard', path: '/parent', icon: <LayoutDashboard size={20} />, color: 'violet' as const },
  { label: 'Notificações', path: '/parent/notifications', icon: <Bell size={20} />, color: 'red' as const },
];

function AdminLayout() {
  return (
    <Sidebar items={adminItems} title="Admin" showLogo>
      <Routes>
          <Route index element={<AdminDashboard />} />
          <Route path="students" element={<StudentList />} />
          <Route path="students/new" element={<StudentForm />} />
          <Route path="students/:id" element={<StudentDetail />} />
          <Route path="students/:id/edit" element={<StudentForm />} />
          <Route path="enrollments" element={<EnrollmentList />} />
          <Route path="enrollments/:id" element={<EnrollmentDetail />} />
          <Route path="documents" element={<DocumentVerification />} />
          <Route path="notifications" element={<NotificationList />} />
          <Route path="notifications/create" element={<NotificationCreate />} />
        </Routes>
    </Sidebar>
  );
}

function ParentLayout() {
  return (
    <Sidebar items={parentItems} title="Ciclo">
        <Routes>
          <Route index element={<ParentDashboard />} />
          <Route path="children/:id" element={<ChildDetail />} />
          <Route path="children/:id/documents" element={<ChildDocuments />} />
          <Route path="notifications" element={<NotificationInbox />} />
        </Routes>
    </Sidebar>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <DocumentTitle />
      <AuthProvider>
        <Toaster position="top-right" />
        <Routes>
          <Route path="/login" element={<SplashGate><LoginPage /></SplashGate>} />
          <Route path="/forgot-password" element={<ForgotPasswordPage />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route path="/auth/google/callback" element={<LoginPage />} />
          <Route path="/admin/*" element={<ProtectedRoute roles={['Admin', 'Staff']}><AdminLayout /></ProtectedRoute>} />
          <Route path="/parent/*" element={<ProtectedRoute roles={['Parent']}><ParentLayout /></ProtectedRoute>} />
          <Route path="*" element={<Navigate to="/login" replace />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}
