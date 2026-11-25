/**
 * The page a signed-out visitor asked for, to return to after signing in. Only a path on this site is ever
 * returned: anything else (another origin, the sign-in page itself, garbage) means "the dashboard".
 */
export function returnPathFrom(state: unknown): string {
  const from = (state as { from?: { pathname?: unknown; search?: unknown; hash?: unknown } } | null | undefined)?.from;
  const pathname = from?.pathname;
  if (typeof pathname !== 'string' || !pathname.startsWith('/') || pathname.startsWith('//') || pathname === '/login') return '/';

  const search = typeof from?.search === 'string' ? from.search : '';
  const hash = typeof from?.hash === 'string' ? from.hash : '';
  return `${pathname}${search}${hash}`;
}
