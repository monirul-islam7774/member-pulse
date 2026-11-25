import type { ProblemDetails } from './types';

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly problem?: ProblemDetails,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

interface AuthHandlers {
  getToken: () => string | null;
  /** Called when the API rejects the token (expired or invalid). Not called for a failed sign-in. */
  onUnauthorized: () => void;
}

let handlers: AuthHandlers = { getToken: () => null, onUnauthorized: () => {} };

export function setAuthHandlers(next: AuthHandlers): void {
  handlers = next;
}

const LOGIN_PATH = '/api/auth/login';

type Query = Record<string, string | number | boolean | undefined>;

async function request<T>(method: string, path: string, options: { query?: Query; body?: unknown } = {}): Promise<T> {
  const url = new URL(path, window.location.origin);
  for (const [key, value] of Object.entries(options.query ?? {})) {
    if (value !== undefined) url.searchParams.set(key, String(value));
  }

  const headers = new Headers({ Accept: 'application/json' });
  const token = handlers.getToken();
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (options.body !== undefined) headers.set('Content-Type', 'application/json');

  let response: Response;
  try {
    response = await fetch(url.pathname + url.search, {
      method,
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
    });
  } catch {
    throw new ApiError(0, 'Could not reach the server.');
  }

  if (response.ok) {
    if (response.status === 204) return undefined as T;
    return (await response.json()) as T;
  }

  // A rejected sign-in is a 401 too, but it is not an expired session.
  if (response.status === 401 && path !== LOGIN_PATH) handlers.onUnauthorized();

  throw await toApiError(response);
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails | undefined;
  if (response.headers.get('Content-Type')?.includes('json')) {
    try {
      problem = (await response.json()) as ProblemDetails;
    } catch {
      problem = undefined;
    }
  }

  const message = problem?.detail ?? problem?.title ?? `The server returned an error (${response.status}).`;
  return new ApiError(response.status, message, problem);
}

export const api = {
  get: <T>(path: string, query?: Query) => request<T>('GET', path, { query }),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, { body }),
};
