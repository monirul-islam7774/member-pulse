import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { createMember, listMembers } from '../api/members';
import { DataTable } from '../components/DataTable';
import { Pager } from '../components/Pager';
import { EmptyState, ErrorState, FormError, Loading } from '../components/States';
import { useAsync } from '../useAsync';

const PAGE_SIZE = 20;

function NewMemberForm() {
  const navigate = useNavigate();
  const [form, setForm] = useState({ firstName: '', lastName: '', email: '', dateOfBirth: '' });
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [key]: e.target.value });

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const created = await createMember({
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        email: form.email.trim(),
        dateOfBirth: form.dateOfBirth,
      });
      navigate(`/members/${created.id}`);
    } catch (e) {
      setError(e);
      setBusy(false);
    }
  }

  return (
    <form className="panel stack" aria-label="New member" onSubmit={onSubmit}>
      <h2>New member</h2>
      <div className="grid-2">
        <label className="field">
          <span>First name</span>
          <input required maxLength={100} value={form.firstName} onChange={set('firstName')} />
        </label>
        <label className="field">
          <span>Last name</span>
          <input required maxLength={100} value={form.lastName} onChange={set('lastName')} />
        </label>
        <label className="field">
          <span>Email</span>
          <input type="email" required maxLength={256} value={form.email} onChange={set('email')} />
        </label>
        <label className="field">
          <span>Date of birth</span>
          <input type="date" required value={form.dateOfBirth} onChange={set('dateOfBirth')} />
        </label>
      </div>
      {error != null && <FormError error={error} />}
      <div>
        <button type="submit" className="btn primary" disabled={busy}>
          {busy ? 'Creating…' : 'Create member'}
        </button>
      </div>
    </form>
  );
}

export function MembersPage() {
  const [page, setPage] = useState(1);
  const [showForm, setShowForm] = useState(false);
  const members = useAsync(() => listMembers(page, PAGE_SIZE), [page]);

  return (
    <>
      <h1 className="page-title">Members</h1>
      <p className="page-lede">Everyone with an account. Open a member to see their accounts, transactions and segments.</p>

      <div className="toolbar">
        <button type="button" className="btn primary" onClick={() => setShowForm((v) => !v)} aria-expanded={showForm}>
          New member
        </button>
      </div>
      {showForm && <NewMemberForm />}

      {members.error ? (
        <ErrorState error={members.error} onRetry={members.reload} />
      ) : !members.data ? (
        <Loading label="Loading members" />
      ) : (
        <div className="panel" aria-busy={members.loading} data-stale={members.loading || undefined}>
          <DataTable
            label="Members"
            rows={members.data.items}
            rowKey={(m) => m.id}
            empty={<EmptyState title="No members yet.">Create the first member with the New member button.</EmptyState>}
            columns={[
              { header: 'Name', cell: (m) => <Link to={`/members/${m.id}`}>{m.fullName}</Link> },
              { header: 'Email', cell: (m) => m.email },
              { header: 'Accounts', numeric: true, cell: (m) => m.accountCount },
            ]}
          />
          {members.data.totalCount > 0 && (
            <Pager page={page} pageSize={PAGE_SIZE} totalCount={members.data.totalCount} onChange={setPage} noun="members" />
          )}
        </div>
      )}
    </>
  );
}
