import { vi } from 'vitest';

/** A JSON response like the API sends. */
export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

export function problem(status: number, title: string, detail?: string): Response {
  return new Response(JSON.stringify({ title, detail, status }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

type Handler = (url: URL, init: RequestInit) => Response | Promise<Response>;

/**
 * Replaces global fetch with routes keyed "METHOD /path". Anything unrouted fails loudly, so a test never
 * silently talks to the network or an endpoint it did not mean to call.
 */
export function mockApi(routes: Record<string, Handler | Response>) {
  const calls: { method: string; path: string; body: unknown; headers: Headers }[] = [];
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init: RequestInit = {}) => {
    const url = new URL(String(input), 'http://localhost');
    const method = (init.method ?? 'GET').toUpperCase();
    calls.push({
      method,
      path: url.pathname + url.search,
      body: init.body ? JSON.parse(String(init.body)) : undefined,
      headers: new Headers(init.headers),
    });
    const route = routes[`${method} ${url.pathname}`];
    if (!route) throw new Error(`Unexpected request: ${method} ${url.pathname}${url.search}`);
    return typeof route === 'function' ? route(url, init) : route.clone();
  });
  vi.stubGlobal('fetch', fetchMock);
  return { calls, fetchMock };
}
