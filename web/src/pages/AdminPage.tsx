import { useState, type FormEvent } from 'react';
import { createUser, listDeadLetters, listUsers, retryDeadLetter, unlockUser } from '../api/admin';
import type { StaffRole } from '../api/types';
import { DataTable } from '../components/DataTable';
import { EmptyState, ErrorState, FormError, Loading } from '../components/States';
import { formatDateTime } from '../format';
import { useAsync } from '../useAsync';

const MIN_PASSWORD_LENGTH = 12;

function StaffSection() {
  const users = useAsync(listUsers, []);
  const [error, setError] = useState<unknown>(null);

  async function unlock(email: string) {
    setError(null);
    try {
      await unlockUser(email);
      users.reload();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <section className="panel stack">
      <h2>Staff</h2>
      {error != null && <FormError error={error} />}
      {users.error ? (
        <ErrorState error={users.error} onRetry={users.reload} />
      ) : !users.data ? (
        <Loading label="Loading staff" />
      ) : (
        <DataTable
          label="Staff users"
          rows={users.data}
          rowKey={(u) => u.id}
          empty={<EmptyState title="No staff users." />}
          columns={[
            { header: 'Email', cell: (u) => u.email },
            { header: 'Role', cell: (u) => u.role },
            {
              header: 'Status',
              cell: (u) => (u.isLockedOut && u.lockedUntil ? `Locked until ${formatDateTime(u.lockedUntil)}` : 'Active'),
            },
            {
              header: 'Action',
              cell: (u) =>
                u.isLockedOut ? (
                  <button type="button" className="btn small" aria-label={`Unlock ${u.email}`} onClick={() => unlock(u.email)}>
                    Unlock
                  </button>
                ) : null,
            },
          ]}
        />
      )}
      <AddUserForm onAdded={users.reload} />
    </section>
  );
}

function AddUserForm({ onAdded }: { onAdded: () => void }) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [role, setRole] = useState<StaffRole>('Staff');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (password.length < MIN_PASSWORD_LENGTH) {
      setError(`The password must be at least ${MIN_PASSWORD_LENGTH} characters.`);
      return;
    }

    setBusy(true);
    setError(null);
    try {
      await createUser({ email: email.trim(), password, role });
      setEmail('');
      setPassword('');
      setRole('Staff');
      onAdded();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="stack" aria-label="Add staff user" onSubmit={onSubmit} noValidate>
      <h3>Add a staff user</h3>
      <div className="grid-2">
        <label className="field">
          <span>Email</span>
          <input type="email" required maxLength={256} autoComplete="off" value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label className="field">
          <span>Password</span>
          <input type="password" required autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} />
        </label>
        <label className="field">
          <span>Role</span>
          <select value={role} onChange={(e) => setRole(e.target.value as StaffRole)}>
            <option value="Staff">Staff</option>
            <option value="Admin">Admin (can manage users)</option>
          </select>
        </label>
      </div>
      <p className="muted">Use at least {MIN_PASSWORD_LENGTH} characters.</p>
      {error != null && <FormError error={error} />}
      <div>
        <button type="submit" className="btn primary" disabled={busy}>
          {busy ? 'Adding…' : 'Add user'}
        </button>
      </div>
    </form>
  );
}

function DeadLetterSection() {
  const letters = useAsync(listDeadLetters, []);
  const [error, setError] = useState<unknown>(null);

  async function retry(id: string) {
    setError(null);
    try {
      await retryDeadLetter(id);
      letters.reload();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <section className="panel stack">
      <h2>Events that could not be published</h2>
      <p className="muted">
        Transaction events that still failed to reach Kafka after every retry. Fix the cause, then retry: a retried event arrives after
        newer ones for the same account.
      </p>
      {error != null && <FormError error={error} />}
      {letters.error ? (
        <ErrorState error={letters.error} onRetry={letters.reload} />
      ) : !letters.data ? (
        <Loading label="Loading events" />
      ) : (
        <DataTable
          label="Dead-lettered events"
          rows={letters.data}
          rowKey={(l) => l.id}
          empty={<EmptyState title="No dead-lettered events." />}
          columns={[
            { header: 'Account', cell: (l) => l.key },
            { header: 'Type', cell: (l) => l.type },
            { header: 'Attempts', numeric: true, cell: (l) => l.attempts },
            { header: 'Last error', cell: (l) => l.lastError ?? '' },
            { header: 'Gave up', cell: (l) => formatDateTime(l.deadLetteredAt) },
            {
              header: 'Action',
              cell: (l) => (
                <button type="button" className="btn small" onClick={() => retry(l.id)}>
                  Retry
                </button>
              ),
            },
          ]}
        />
      )}
    </section>
  );
}

export function AdminPage() {
  return (
    <>
      <h1 className="page-title">Admin</h1>
      <p className="page-lede">Manage who can sign in and keep an eye on events that need attention.</p>
      <StaffSection />
      <DeadLetterSection />
    </>
  );
}
