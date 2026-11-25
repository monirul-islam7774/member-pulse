import { useCallback, useEffect, useState } from 'react';

interface AsyncState<T> {
  data?: T;
  error?: unknown;
  loading: boolean;
}

/**
 * Runs `load` now and whenever `deps` change. While a reload is in flight the previous data stays available
 * (so screens keep their frame instead of flashing a spinner), and a failed reload shows the error alone.
 * Out-of-date responses (the inputs changed again before they arrived) are ignored.
 */
export function useAsync<T>(load: () => Promise<T>, deps: readonly unknown[]) {
  const [state, setState] = useState<AsyncState<T>>({ loading: true });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let current = true;
    setState((s) => ({ data: s.data, loading: true }));
    load().then(
      (data) => current && setState({ data, loading: false }),
      (error: unknown) => current && setState({ error, loading: false }),
    );
    return () => {
      current = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, attempt]);

  const reload = useCallback(() => setAttempt((n) => n + 1), []);
  return { ...state, reload };
}
