import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider } from '../auth/AuthContext';
import { json, mockApi, problem } from '../test/http';
import { LoginPage } from './LoginPage';

function renderLogin() {
  render(
    <MemoryRouter initialEntries={['/login']}>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/" element={<p>dashboard</p>} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

async function submit(email = 'staff@example.com', password = 'whatever-password') {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText(/email/i), email);
  await user.type(screen.getByLabelText(/password/i), password);
  await user.click(screen.getByRole('button', { name: /sign in/i }));
}

describe('LoginPage', () => {
  it('signs_in_loads_the_user_and_opens_the_dashboard', async () => {
    const { calls } = mockApi({
      'POST /api/auth/login': json({ accessToken: 'tok', tokenType: 'Bearer', expiresAt: '2030-01-01T00:00:00Z' }),
      'GET /api/auth/me': json({ id: '1', email: 'staff@example.com', role: 'Admin', createdAt: '2026-01-01T00:00:00Z' }),
    });
    renderLogin();

    await submit();

    expect(await screen.findByText('dashboard')).toBeInTheDocument();
    expect(sessionStorage.getItem('mp.token')).toBe('tok');
    expect(calls[1].headers.get('Authorization')).toBe('Bearer tok'); // /me is called with the new token
  });

  it('shows_one_generic_message_for_a_401', async () => {
    mockApi({ 'POST /api/auth/login': problem(401, 'Invalid credentials') });
    renderLogin();

    await submit();

    expect(await screen.findByRole('alert')).toHaveTextContent(/incorrect email or password/i);
    expect(sessionStorage.getItem('mp.token')).toBeNull();
  });

  it('shows_a_wait_message_for_a_429', async () => {
    mockApi({ 'POST /api/auth/login': problem(429, 'Too Many Requests') });
    renderLogin();

    await submit();

    expect(await screen.findByRole('alert')).toHaveTextContent(/too many .*attempts.*wait a minute/i);
  });

  it('shows_a_connection_error_when_the_network_fails', async () => {
    mockApi({ 'POST /api/auth/login': () => { throw new TypeError('Failed to fetch'); } });
    renderLogin();

    await submit();

    expect(await screen.findByRole('alert')).toHaveTextContent(/can.t reach the server/i);
  });

  it('shows_session_expired_when_redirected_after_expiry', () => {
    sessionStorage.setItem('mp.expired', '1');
    mockApi({});

    renderLogin();

    expect(screen.getByRole('status')).toHaveTextContent(/session (has )?expired/i);
  });

  it('disables_the_button_while_signing_in', async () => {
    let release: (r: Response) => void = () => {};
    mockApi({ 'POST /api/auth/login': () => new Promise<Response>((resolve) => { release = resolve; }) });
    renderLogin();

    await submit();

    expect(screen.getByRole('button', { name: /signing in/i })).toBeDisabled();
    release(problem(401, 'Invalid credentials'));
    await waitFor(() => expect(screen.getByRole('button', { name: /sign in/i })).toBeEnabled());
  });

  it('does_not_call_the_api_with_empty_fields', async () => {
    const { fetchMock } = mockApi({});
    renderLogin();

    await userEvent.setup().click(screen.getByRole('button', { name: /sign in/i }));

    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/email/i)).toBeInvalid();
  });
});
