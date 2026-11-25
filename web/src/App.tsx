import { Route, Routes } from 'react-router-dom';
import { RequireAdmin } from './auth/RequireAdmin';
import { Layout } from './components/Layout';
import { AdminPage } from './pages/AdminPage';
import { CampaignDetailPage } from './pages/CampaignDetailPage';
import { CampaignsPage } from './pages/CampaignsPage';
import { DashboardPage } from './pages/DashboardPage';
import { LoginPage } from './pages/LoginPage';
import { MemberDetailPage } from './pages/MemberDetailPage';
import { MembersPage } from './pages/MembersPage';
import { NotFoundPage } from './pages/NotFoundPage';
import { SegmentsPage } from './pages/SegmentsPage';

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<Layout />}>
        <Route index element={<DashboardPage />} />
        <Route path="members" element={<MembersPage />} />
        <Route path="members/:id" element={<MemberDetailPage />} />
        <Route path="segments" element={<SegmentsPage />} />
        <Route path="campaigns" element={<CampaignsPage />} />
        <Route path="campaigns/:id" element={<CampaignDetailPage />} />
        <Route
          path="admin"
          element={
            <RequireAdmin>
              <AdminPage />
            </RequireAdmin>
          }
        />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  );
}
