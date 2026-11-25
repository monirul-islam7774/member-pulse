import { NavLink, Navigate, Outlet } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

const links = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/members', label: 'Members' },
  { to: '/segments', label: 'Segments' },
  { to: '/campaigns', label: 'Campaigns' },
];

/** The signed-in frame: navigation on the left, who you are and sign-out on top. Signed-out visitors are sent to sign-in. */
export function Layout() {
  const { user, signOut } = useAuth();
  if (!user) return <Navigate to="/login" replace />;

  return (
    <div className="app">
      <nav className="nav" aria-label="Main">
        <p className="brand-mark">MemberPulse</p>
        <ul>
          {[...links, ...(user.role === 'Admin' ? [{ to: '/admin', label: 'Admin', end: false }] : [])].map((link) => (
            <li key={link.to}>
              <NavLink to={link.to} end={link.end}>
                {link.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>

      <div className="main">
        <header className="topbar">
          <span className="who">{user.email}</span>
          <button type="button" className="btn quiet" onClick={signOut}>
            Sign out
          </button>
        </header>
        <main className="page">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
