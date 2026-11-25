import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AppRoutes } from './App';
import { AuthProvider } from './auth/AuthContext';
import { mockApi } from './test/http';

function signedInAs(role: 'Staff' | 'Admin') {
  sessionStorage.setItem('mp.token', 'tok');
  sessionStorage.setItem('mp.user', JSON.stringify({ email: `${role.toLowerCase()}@example.com`, role }));
}

function renderAt(path: string) {
  mockApi({});
  render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe('app shell', () => {
  it('sends_signed_out_visitors_to_the_sign_in_page', () => {
    renderAt('/campaigns');

    expect(screen.getByRole('heading', { name: 'MemberPulse' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument();
  });

  it('shows_the_navigation_and_the_signed_in_user', () => {
    signedInAs('Staff');

    renderAt('/');

    const nav = screen.getByRole('navigation', { name: /main/i });
    for (const name of ['Dashboard', 'Members', 'Segments', 'Campaigns']) {
      expect(nav).toHaveTextContent(name);
    }
    expect(screen.getByText('staff@example.com')).toBeInTheDocument();
  });

  it('shows_the_admin_link_only_to_admins', () => {
    signedInAs('Staff');
    renderAt('/');
    expect(screen.getByRole('navigation', { name: /main/i })).not.toHaveTextContent('Admin');
  });

  it('shows_the_admin_link_to_admins', () => {
    signedInAs('Admin');
    renderAt('/');
    expect(screen.getByRole('navigation', { name: /main/i })).toHaveTextContent('Admin');
  });

  it('signing_out_clears_the_session_and_returns_to_sign_in', async () => {
    signedInAs('Staff');
    renderAt('/');

    await userEvent.setup().click(screen.getByRole('button', { name: /sign out/i }));

    expect(await screen.findByRole('button', { name: /sign in/i })).toBeInTheDocument();
    expect(sessionStorage.getItem('mp.token')).toBeNull();
  });

  it('shows_a_not_found_page_for_unknown_routes', () => {
    signedInAs('Staff');

    renderAt('/nowhere/at/all');

    expect(screen.getByRole('heading', { name: /page not found/i })).toBeInTheDocument();
  });
});
