import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { DeadLetter, StaffUserSummary } from '../api/types';
import { AuthProvider } from '../auth/AuthContext';
import { RequireAdmin } from '../auth/RequireAdmin';
import { json, mockApi, problem } from '../test/http';
import { AdminPage } from './AdminPage';

const user = (over: Partial<StaffUserSummary>): StaffUserSummary => ({
  id: 'u1', email: 'admin@example.com', role: 'Admin', createdAt: '2026-01-01T00:00:00Z', isLockedOut: false, lockedUntil: null, ...over,
});

const letter = (over: Partial<DeadLetter> = {}): DeadLetter => ({
  id: 'dl1', topic: 'memberpulse.transactions', key: 'acct-1', type: 'TransactionPosted', attempts: 20,
  lastError: 'broker unavailable', createdAt: '2026-11-24T10:00:00Z', deadLetteredAt: '2026-11-24T11:00:00Z', ...over,
});

function renderAs(role: 'Staff' | 'Admin') {
  sessionStorage.setItem('mp.token', 'tok');
  sessionStorage.setItem('mp.user', JSON.stringify({ email: `${role.toLowerCase()}@example.com`, role }));
  render(
    <MemoryRouter>
      <AuthProvider>
        <RequireAdmin>
          <AdminPage />
        </RequireAdmin>
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe('AdminPage', () => {
  it('non_admins_see_forbidden_and_no_admin_calls_are_made', () => {
    const { fetchMock } = mockApi({});

    renderAs('Staff');

    expect(screen.getByRole('heading', { name: /admins only/i })).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('locked_users_show_until_when_and_unlock_refreshes_the_list', async () => {
    let locked = true;
    const { calls } = mockApi({
      'GET /api/users': () =>
        json([user({}), user({ id: 'u2', email: 'locked@example.com', role: 'Staff', isLockedOut: locked, lockedUntil: locked ? '2030-01-01T12:00:00Z' : null })]),
      'GET /api/admin/outbox/dead': json([]),
      'POST /api/users/unlock': () => {
        locked = false;
        return new Response(null, { status: 204 });
      },
    });
    renderAs('Admin');

    const row = (await screen.findByText('locked@example.com')).closest('tr')!;
    expect(row).toHaveTextContent(/locked until/i);
    expect(within(screen.getByText('admin@example.com').closest('tr')!).queryByRole('button', { name: /unlock/i })).not.toBeInTheDocument();

    await userEvent.setup().click(within(row).getByRole('button', { name: /unlock locked@example.com/i }));

    await waitFor(() => expect(screen.getByText('locked@example.com').closest('tr')).not.toHaveTextContent(/locked until/i));
    expect(calls.find((c) => c.path === '/api/users/unlock')?.body).toEqual({ email: 'locked@example.com' });
  });

  it('create_user_enforces_a_12_character_password_before_calling_the_api', async () => {
    const { calls } = mockApi({
      'GET /api/users': json([user({})]),
      'GET /api/admin/outbox/dead': json([]),
      'POST /api/users': json(user({ id: 'u3', email: 'new@example.com', role: 'Staff' }), 201),
    });
    renderAs('Admin');
    const u = userEvent.setup();
    await screen.findByText('admin@example.com');
    const form = screen.getByRole('form', { name: /add staff user/i });

    await u.type(within(form).getByLabelText('Email'), 'new@example.com');
    await u.type(within(form).getByLabelText('Password'), 'short');
    await u.click(within(form).getByRole('button', { name: /add user/i }));

    expect(await within(form).findByRole('alert')).toHaveTextContent(/at least 12 characters/i);
    expect(calls.some((c) => c.method === 'POST')).toBe(false);

    await u.clear(within(form).getByLabelText('Password'));
    await u.type(within(form).getByLabelText('Password'), 'A-long-enough-password');
    await u.click(within(form).getByRole('button', { name: /add user/i }));

    await waitFor(() => expect(within(form).getByLabelText('Email')).toHaveValue(''));
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ email: 'new@example.com', password: 'A-long-enough-password', role: 'Staff' });
    expect(within(form).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows_a_duplicate_email_inline', async () => {
    mockApi({
      'GET /api/users': json([user({})]),
      'GET /api/admin/outbox/dead': json([]),
      'POST /api/users': problem(409, 'Email already registered', "A staff user with email 'a@b.c' already exists."),
    });
    renderAs('Admin');
    const u = userEvent.setup();
    await screen.findByText('admin@example.com');
    const form = screen.getByRole('form', { name: /add staff user/i });
    await u.type(within(form).getByLabelText('Email'), 'a@b.c');
    await u.type(within(form).getByLabelText('Password'), 'A-long-enough-password');

    await u.click(within(form).getByRole('button', { name: /add user/i }));

    expect(await within(form).findByRole('alert')).toHaveTextContent("A staff user with email 'a@b.c' already exists.");
  });

  it('dead_letters_list_and_retry_removes_the_row', async () => {
    let letters = [letter()];
    const { calls } = mockApi({
      'GET /api/users': json([user({})]),
      'GET /api/admin/outbox/dead': () => json(letters),
      'POST /api/admin/outbox/dl1/retry': () => {
        letters = [];
        return new Response(null, { status: 204 });
      },
    });
    renderAs('Admin');

    const row = (await screen.findByText('broker unavailable')).closest('tr')!;
    expect(row).toHaveTextContent('20');
    expect(row).toHaveTextContent('acct-1');

    await userEvent.setup().click(within(row).getByRole('button', { name: /retry/i }));

    expect(await screen.findByText(/no dead-lettered events/i)).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'POST' && c.path === '/api/admin/outbox/dl1/retry')).toBe(true);
  });

  it('shows_an_empty_state_when_there_are_no_dead_letters', async () => {
    mockApi({ 'GET /api/users': json([user({})]), 'GET /api/admin/outbox/dead': json([]) });
    renderAs('Admin');

    expect(await screen.findByText(/no dead-lettered events/i)).toBeInTheDocument();
  });

  it('renders_emails_and_errors_as_text', async () => {
    mockApi({
      'GET /api/users': json([user({ email: '<img src=x onerror=alert(1)>@example.com' })]),
      'GET /api/admin/outbox/dead': json([letter({ lastError: '<b>boom</b>' })]),
    });
    renderAs('Admin');

    expect(await screen.findByText('<b>boom</b>')).toBeInTheDocument();
    expect(screen.getByText('<img src=x onerror=alert(1)>@example.com')).toBeInTheDocument();
    expect(document.querySelector('img, b')).toBeNull();
  });
});
