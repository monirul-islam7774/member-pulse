import { useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import { getMember, getMemberSegments, listTransactions, openAccount, postTransaction } from '../api/members';
import type { Account, AccountType, MemberDetail } from '../api/types';
import { DataTable } from '../components/DataTable';
import { Pager } from '../components/Pager';
import { EmptyState, ErrorState, FormError, Loading } from '../components/States';
import { formatCurrency, formatDate, formatDateTime } from '../format';
import { accountTypeLabels, accountTypes } from '../labels';
import { useAsync } from '../useAsync';

const TX_PAGE_SIZE = 10;

function PostTransactionForm({ accountId, onPosted }: { accountId: string; onPosted: () => void }) {
  const [amount, setAmount] = useState('');
  const [description, setDescription] = useState('');
  const [category, setCategory] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const value = Number(amount);
    if (!Number.isFinite(value) || value === 0) {
      setError('Amount cannot be zero. Use a negative amount for a purchase or withdrawal, positive for a deposit.');
      return;
    }

    setBusy(true);
    setError(null);
    try {
      await postTransaction(accountId, {
        amount: value,
        description: description.trim(),
        merchantCategory: category.trim() || undefined,
      });
      setAmount('');
      setDescription('');
      setCategory('');
      onPosted();
    } catch (e) {
      setError(e); // e.g. 422 "Insufficient funds."
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="stack" aria-label="Post transaction" onSubmit={onSubmit} noValidate>
      <h3>Post a transaction</h3>
      <div className="grid-2">
        <label className="field">
          <span>
            Amount <span className="hint">(negative for a purchase, positive for a deposit)</span>
          </span>
          <input type="number" step="0.01" required value={amount} onChange={(e) => setAmount(e.target.value)} />
        </label>
        <label className="field">
          <span>Description</span>
          <input required maxLength={200} value={description} onChange={(e) => setDescription(e.target.value)} />
        </label>
        <label className="field">
          <span>
            Merchant category <span className="hint">(optional)</span>
          </span>
          <input maxLength={50} value={category} onChange={(e) => setCategory(e.target.value)} />
        </label>
      </div>
      {error != null && <FormError error={error} />}
      <div>
        <button type="submit" className="btn primary" disabled={busy}>
          {busy ? 'Posting…' : 'Post transaction'}
        </button>
      </div>
    </form>
  );
}

function AccountTransactions({ account, onPosted }: { account: Account; onPosted: () => void }) {
  const [page, setPage] = useState(1);
  const transactions = useAsync(() => listTransactions(account.id, page, TX_PAGE_SIZE), [account.id, page]);

  function posted() {
    setPage(1);
    transactions.reload();
    onPosted();
  }

  return (
    <section className="panel stack">
      <h2>{accountTypeLabels[account.type]} transactions</h2>
      {transactions.error ? (
        <ErrorState error={transactions.error} onRetry={transactions.reload} />
      ) : !transactions.data ? (
        <Loading label="Loading transactions" />
      ) : (
        <div aria-busy={transactions.loading} data-stale={transactions.loading || undefined}>
          <DataTable
            label="Transactions"
            rows={transactions.data.items}
            rowKey={(t) => t.id}
            empty={<EmptyState title="No transactions on this account yet." />}
            columns={[
              { header: 'Date', cell: (t) => formatDateTime(t.occurredAt) },
              { header: 'Description', cell: (t) => t.description },
              { header: 'Category', cell: (t) => t.merchantCategory ?? '' },
              { header: 'Amount', numeric: true, cell: (t) => formatCurrency(t.amount) },
            ]}
          />
          {transactions.data.totalCount > TX_PAGE_SIZE && (
            <Pager
              page={page}
              pageSize={TX_PAGE_SIZE}
              totalCount={transactions.data.totalCount}
              onChange={setPage}
              noun="transactions"
            />
          )}
        </div>
      )}
      <PostTransactionForm accountId={account.id} onPosted={posted} />
    </section>
  );
}

function OpenAccountForm({ memberId, onOpened }: { memberId: string; onOpened: () => void }) {
  const [type, setType] = useState<AccountType>('Checking');
  const [deposit, setDeposit] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await openAccount(memberId, { type, initialDeposit: deposit === '' ? 0 : Number(deposit) });
      setDeposit('');
      onOpened();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="stack" aria-label="Open account" onSubmit={onSubmit}>
      <h3>Open an account</h3>
      <div className="grid-2">
        <label className="field">
          <span>Account type</span>
          <select value={type} onChange={(e) => setType(e.target.value as AccountType)}>
            {accountTypes.map((t) => (
              <option key={t} value={t}>
                {accountTypeLabels[t]}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>Initial deposit</span>
          <input type="number" min="0" step="0.01" value={deposit} onChange={(e) => setDeposit(e.target.value)} />
        </label>
      </div>
      {error != null && <FormError error={error} />}
      <div>
        <button type="submit" className="btn primary" disabled={busy}>
          {busy ? 'Opening…' : 'Open account'}
        </button>
      </div>
    </form>
  );
}

function MemberView({ member, onChanged }: { member: MemberDetail; onChanged: () => void }) {
  const segments = useAsync(() => getMemberSegments(member.id), [member.id]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = member.accounts.find((a) => a.id === selectedId) ?? member.accounts[0];

  return (
    <>
      <p className="muted">
        <Link to="/members">Members</Link>
      </p>
      <h1 className="page-title">
        {member.firstName} {member.lastName}
      </h1>
      <p className="page-lede">
        <span>{member.email}</span>. Age {member.age}, a member since {formatDate(member.joinedAt)}.
      </p>

      <section className="panel">
        <h2>Segments</h2>
        {segments.error ? (
          <ErrorState error={segments.error} onRetry={segments.reload} />
        ) : !segments.data ? (
          <Loading label="Loading segments" />
        ) : segments.data.length === 0 ? (
          <p className="muted">Not in any segment yet.</p>
        ) : (
          <div>
            {segments.data.map((s) => (
              <span key={s.id} className="chip">
                {s.name}
              </span>
            ))}
          </div>
        )}
      </section>

      <section className="panel stack">
        <h2>Accounts</h2>
        <DataTable
          label="Accounts"
          rows={member.accounts}
          rowKey={(a) => a.id}
          empty={<EmptyState title="No accounts yet.">Open one below to start recording transactions.</EmptyState>}
          columns={[
            { header: 'Type', cell: (a) => accountTypeLabels[a.type] },
            { header: 'Balance', numeric: true, cell: (a) => formatCurrency(a.balance) },
            { header: 'Opened', cell: (a) => formatDate(a.openedAt) },
            {
              header: 'Transactions',
              cell: (a) => (
                <button
                  type="button"
                  className="btn small"
                  aria-label={`View ${accountTypeLabels[a.type]} transactions`}
                  aria-pressed={a.id === selected?.id}
                  onClick={() => setSelectedId(a.id)}
                >
                  {a.id === selected?.id ? 'Showing' : 'View'}
                </button>
              ),
            },
          ]}
        />
        <OpenAccountForm memberId={member.id} onOpened={onChanged} />
      </section>

      {selected && <AccountTransactions key={selected.id} account={selected} onPosted={onChanged} />}
    </>
  );
}

/** Keyed by id: a different member is a fresh page, so the previous member's data and forms can never linger under the new URL. */
export function MemberDetailPage() {
  const { id = '' } = useParams();
  return <MemberDetailLoader key={id} id={id} />;
}

function MemberDetailLoader({ id }: { id: string }) {
  const member = useAsync(() => getMember(id), [id]);

  if (member.error instanceof ApiError && member.error.status === 404) {
    return (
      <>
        <h1 className="page-title">Member not found</h1>
        <p className="muted">
          There is no member with this ID. <Link to="/members">Back to members</Link>.
        </p>
      </>
    );
  }
  if (member.error) return <ErrorState error={member.error} onRetry={member.reload} />;
  if (!member.data) return <Loading label="Loading member" />;

  return <MemberView member={member.data} onChanged={member.reload} />;
}
