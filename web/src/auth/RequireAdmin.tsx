import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from './AuthContext';

/** Renders its children only for admins. For anyone else the admin screen is never even mounted, so it makes no admin API calls. */
export function RequireAdmin({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  if (user?.role !== 'Admin') {
    return (
      <>
        <h1 className="page-title">Admins only</h1>
        <p className="muted">
          Your account can't open this page. <Link to="/">Back to the dashboard</Link>.
        </p>
      </>
    );
  }
  return <>{children}</>;
}
