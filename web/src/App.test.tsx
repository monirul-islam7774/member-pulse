import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AppRoutes } from './App';
import { AuthProvider } from './auth/AuthContext';
import { json, mockApi, problem } from './test/http';

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

  it('keeps_staff_out_of_the_admin_page', () => {
    signedInAs('Staff');

    renderAt('/admin');

    expect(screen.getByRole('heading', { name: /admins only/i })).toBeInTheDocument();
  });

  it('shows_a_not_found_page_for_unknown_routes', () => {
    signedInAs('Staff');

    renderAt('/nowhere/at/all');

    expect(screen.getByRole('heading', { name: /page not found/i })).toBeInTheDocument();
  });
});

const signInRoutes = {
  'POST /api/auth/login': json({ accessToken: 'tok', tokenType: 'Bearer', expiresAt: '2030-01-01T00:00:00Z' }),
  'GET /api/auth/me': json({ id: '1', email: 'staff@example.com', role: 'Staff', createdAt: '2026-01-01T00:00:00Z' }),
};

function renderWithoutDefaultMocks(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </MemoryRouter>,
  );
}

async function signInThroughTheForm() {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText(/email/i), 'staff@example.com');
  await user.type(screen.getByLabelText(/password/i), 'whatever-password');
  await user.click(screen.getByRole('button', { name: /sign in/i }));
}

describe('returning to where the user was', () => {
  it('returns_to_the_page_a_signed_out_visitor_asked_for', async () => {
    mockApi({ ...signInRoutes, 'GET /api/segments': json([]) });
    renderWithoutDefaultMocks('/segments');
    expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument();

    await signInThroughTheForm();

    expect(await screen.findByRole('heading', { name: 'Segments' })).toBeInTheDocument();
  });

  it('returns_to_the_page_after_a_session_expires_and_says_unsaved_work_was_not_saved', async () => {
    signedInAs('Staff');
    let segmentCalls = 0;
    mockApi({
      ...signInRoutes,
      'GET /api/segments': () => (++segmentCalls === 1 ? problem(401, 'Unauthorized') : json([])),
    });
    renderWithoutDefaultMocks('/segments');

    const notice = await screen.findByText(/your session expired/i);
    expect(notice).toHaveTextContent(/weren.t saved/i);

    await signInThroughTheForm();

    expect(await screen.findByRole('heading', { name: 'Segments' })).toBeInTheDocument();
    expect(segmentCalls).toBe(2);
  });

  it('signing_out_does_not_carry_the_page_into_the_next_sign_in', async () => {
    signedInAs('Staff');
    const report = { from: '2026-10-26T00:00:00Z', to: '2026-11-25T00:00:00Z' };
    mockApi({
      ...signInRoutes,
      'GET /api/members': json({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
      'GET /api/reports/campaigns': json({ ...report, campaigns: [], totals: { sent: 0, failed: 0, pending: 0, converted: 0, conversionRate: 0, membersReached: 0, convertedAmount: 0 } }),
      'GET /api/reports/campaigns/daily': json({ ...report, daily: [] }),
    });
    renderWithoutDefaultMocks('/members');
    await screen.findByRole('heading', { name: 'Members' });

    await userEvent.setup().click(screen.getByRole('button', { name: /sign out/i }));
    await signInThroughTheForm();

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
  });
});
