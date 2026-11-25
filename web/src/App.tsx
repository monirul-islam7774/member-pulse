import { Route, Routes } from 'react-router-dom';
import { Layout } from './components/Layout';
import { DashboardPage } from './pages/DashboardPage';
import { LoginPage } from './pages/LoginPage';
import { MemberDetailPage } from './pages/MemberDetailPage';
import { MembersPage } from './pages/MembersPage';
import { NotFoundPage } from './pages/NotFoundPage';

// Screens arrive task by task; until then their routes show a stand-in.
function Soon({ title }: { title: string }) {
  return <h1 className="page-title">{title}</h1>;
}

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<Layout />}>
        <Route index element={<DashboardPage />} />
        <Route path="members" element={<MembersPage />} />
        <Route path="members/:id" element={<MemberDetailPage />} />
        <Route path="segments" element={<Soon title="Segments" />} />
        <Route path="campaigns" element={<Soon title="Campaigns" />} />
        <Route path="campaigns/:id" element={<Soon title="Campaign" />} />
        <Route path="admin" element={<Soon title="Admin" />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  );
}
