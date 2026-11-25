import { render, screen, waitFor } from '@testing-library/react';
import { useEffect, useState } from 'react';
import { describe, expect, it } from 'vitest';
import { ApiError, api, setAuthHandlers } from '../api/client';
import { json, mockApi, problem } from '../test/http';
import { AuthProvider, useAuth } from './AuthContext';

/** A screen that loads data as soon as it mounts, like every page in the app. */
function LoadsOnMount() {
  const [result, setResult] = useState('loading');
  const { user, sessionExpired } = useAuth();
  useEffect(() => {
    api.get('/api/members').then(
      () => setResult('loaded'),
      (e: unknown) => setResult(e instanceof ApiError ? `failed ${e.status}` : 'failed'),
    );
  }, []);
  return (
    <p>
      {result} | user:{user?.email ?? 'none'} | expired:{String(sessionExpired)}
    </p>
  );
}

/** What a freshly loaded browser page looks like: no handler registered yet. */
function freshPageLoad() {
  setAuthHandlers({ getToken: () => null, onUnauthorized: () => {} });
}

describe('AuthProvider on a fresh page load', () => {
  it('sends_the_stored_token_with_the_first_request_a_screen_makes', async () => {
    freshPageLoad();
    sessionStorage.setItem('mp.token', 'stored-token');
    sessionStorage.setItem('mp.user', JSON.stringify({ email: 'staff@example.com', role: 'Staff' }));
    const { calls } = mockApi({ 'GET /api/members': json({ items: [] }) });

    render(
      <AuthProvider>
        <LoadsOnMount />
      </AuthProvider>,
    );

    expect(await screen.findByText(/^loaded/)).toBeInTheDocument();
    expect(calls[0].headers.get('Authorization')).toBe('Bearer stored-token');
  });

  it('a_reload_does_not_end_the_session', async () => {
    freshPageLoad();
    sessionStorage.setItem('mp.token', 'stored-token');
    sessionStorage.setItem('mp.user', JSON.stringify({ email: 'staff@example.com', role: 'Staff' }));
    // The API answers 401 to a request without a token, like the real one.
    mockApi({
      'GET /api/members': (_url, init) =>
        new Headers(init.headers).has('Authorization') ? json({ items: [] }) : problem(401, 'Unauthorized'),
    });

    render(
      <AuthProvider>
        <LoadsOnMount />
      </AuthProvider>,
    );

    expect(await screen.findByText(/^loaded/)).toHaveTextContent('user:staff@example.com | expired:false');
    expect(sessionStorage.getItem('mp.token')).toBe('stored-token');
  });

  it('a_genuine_401_ends_the_session_and_says_it_expired', async () => {
    freshPageLoad();
    sessionStorage.setItem('mp.token', 'old-token');
    sessionStorage.setItem('mp.user', JSON.stringify({ email: 'staff@example.com', role: 'Staff' }));
    mockApi({ 'GET /api/members': problem(401, 'Unauthorized') });

    render(
      <AuthProvider>
        <LoadsOnMount />
      </AuthProvider>,
    );

    await waitFor(() => expect(screen.getByText(/^failed 401/)).toHaveTextContent('user:none | expired:true'));
    expect(sessionStorage.getItem('mp.token')).toBeNull();
    expect(sessionStorage.getItem('mp.expired')).toBe('1');
  });

  it('a_401_when_nobody_is_signed_in_is_not_an_expiry', async () => {
    freshPageLoad();
    mockApi({ 'GET /api/members': problem(401, 'Unauthorized') });

    render(
      <AuthProvider>
        <LoadsOnMount />
      </AuthProvider>,
    );

    await waitFor(() => expect(screen.getByText(/^failed 401/)).toHaveTextContent('user:none | expired:false'));
    expect(sessionStorage.getItem('mp.expired')).toBeNull();
  });
});
