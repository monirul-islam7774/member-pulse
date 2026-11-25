import type { ReactNode } from 'react';
import { ApiError } from '../api/client';

export function Loading({ label = 'Loading' }: { label?: string }) {
  return (
    <p role="status" className="state loading">
      {label}…
    </p>
  );
}

export function EmptyState({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="state empty">
      <p className="state-title">{title}</p>
      {children && <p className="muted">{children}</p>}
    </div>
  );
}

/** What to tell the user: the API's own explanation, including per-field validation messages when it sends them. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 0) return "Can't reach the server. Check your connection.";
    const fieldMessages = Object.values(error.problem?.errors ?? {}).flat();
    return fieldMessages.length > 0 ? fieldMessages.join(' ') : error.message;
  }
  return 'Something went wrong.';
}

/** An inline failure message for a form. */
export function FormError({ error }: { error: unknown }) {
  return (
    <p role="alert" className="form-error">
      {typeof error === 'string' ? error : errorMessage(error)}
    </p>
  );
}

export function ErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  return (
    <div role="alert" className="state error">
      <p className="state-title">{errorMessage(error)}</p>
      {onRetry && (
        <button type="button" className="btn" onClick={onRetry}>
          Try again
        </button>
      )}
    </div>
  );
}
