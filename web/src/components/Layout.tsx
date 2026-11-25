import { NavLink, Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

const links = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/members', label: 'Members' },
  { to: '/segments', label: 'Segments' },
  { to: '/campaigns', label: 'Campaigns' },
];

/** The signed-in frame: navigation on the left, who you are and sign-out on top. Signed-out visitors are sent to sign-in. */
export function Layout() {
  const { user, signOut, signedOutByUser } = useAuth();
  const location = useLocation();
  // Remember where the visitor was going, so signing in (also after an expired session) brings them back to it.
  // Someone who just chose to sign out was going nowhere.
  if (!user) return <Navigate to="/login" replace state={signedOutByUser ? undefined : { from: location }} />;

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
