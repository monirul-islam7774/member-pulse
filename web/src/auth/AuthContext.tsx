import { createContext, useCallback, useContext, useLayoutEffect, useMemo, useState, type ReactNode } from 'react';
import { api, setAuthHandlers } from '../api/client';
import type { LoginResponse, StaffRole, StaffUser } from '../api/types';

const TOKEN_KEY = 'mp.token';
const USER_KEY = 'mp.user';
const EXPIRED_KEY = 'mp.expired';

export interface SessionUser {
  email: string;
  role: StaffRole;
}

interface AuthValue {
  user: SessionUser | null;
  /** True after the API rejected our token (it lasts 60 minutes), until the next successful sign-in. */
  sessionExpired: boolean;
  /** True after the user chose to sign out (until they sign in again): there is no page to bring them back to. */
  signedOutByUser: boolean;
  signIn: (email: string, password: string) => Promise<void>;
  signOut: () => void;
}

const AuthContext = createContext<AuthValue | null>(null);

function readUser(): SessionUser | null {
  try {
    const raw = sessionStorage.getItem(USER_KEY);
    return raw && sessionStorage.getItem(TOKEN_KEY) ? (JSON.parse(raw) as SessionUser) : null;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(readUser);
  const [sessionExpired, setSessionExpired] = useState(() => sessionStorage.getItem(EXPIRED_KEY) === '1');
  const [signedOutByUser, setSignedOutByUser] = useState(false);

  const clearSession = useCallback(() => {
    sessionStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(USER_KEY);
    setUser(null);
  }, []);

  // A layout effect on purpose: React runs children's ordinary effects (where every screen starts loading its data)
  // before this component's own ordinary effects, so a plain useEffect would let the first request of a freshly
  // loaded page leave without the token, come back 401, and be mistaken for an expired session.
  useLayoutEffect(() => {
    setAuthHandlers({
      getToken: () => sessionStorage.getItem(TOKEN_KEY),
      onUnauthorized: () => {
        // Only a signed-in session can "expire"; a stray 401 after sign-out changes nothing.
        if (!sessionStorage.getItem(TOKEN_KEY)) return;
        sessionStorage.setItem(EXPIRED_KEY, '1');
        setSessionExpired(true);
        clearSession();
      },
    });
  }, [clearSession]);

  const signIn = useCallback(
    async (email: string, password: string) => {
      const login = await api.post<LoginResponse>('/api/auth/login', { email, password });
      sessionStorage.setItem(TOKEN_KEY, login.accessToken);
      try {
        const me = await api.get<StaffUser>('/api/auth/me');
        const next: SessionUser = { email: me.email, role: me.role };
        sessionStorage.setItem(USER_KEY, JSON.stringify(next));
        sessionStorage.removeItem(EXPIRED_KEY);
        setSessionExpired(false);
        setSignedOutByUser(false);
        setUser(next);
      } catch (error) {
        clearSession();
        throw error;
      }
    },
    [clearSession],
  );

  const signOut = useCallback(() => {
    sessionStorage.removeItem(EXPIRED_KEY);
    setSessionExpired(false);
    setSignedOutByUser(true);
    clearSession();
  }, [clearSession]);

  const value = useMemo(
    () => ({ user, sessionExpired, signedOutByUser, signIn, signOut }),
    [user, sessionExpired, signedOutByUser, signIn, signOut],
  );
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error('useAuth must be used inside <AuthProvider>.');
  return value;
}
