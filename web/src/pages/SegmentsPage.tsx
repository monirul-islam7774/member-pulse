import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { createSegment, listSegmentMembers, listSegments } from '../api/segments';
import type { Segment } from '../api/types';
import { CriteriaBuilder, emptyCriterion, toCriteriaRequest, type CriterionDraft } from '../components/CriteriaBuilder';
import { DataTable } from '../components/DataTable';
import { Pager } from '../components/Pager';
import { EmptyState, ErrorState, FormError, Loading } from '../components/States';
import { describeCriterion } from '../labels';
import { useAsync } from '../useAsync';

const MEMBERS_PAGE_SIZE = 10;

function SegmentMembers({ id }: { id: string }) {
  const [page, setPage] = useState(1);
  const members = useAsync(() => listSegmentMembers(id, page, MEMBERS_PAGE_SIZE), [id, page]);

  if (members.error) return <ErrorState error={members.error} onRetry={members.reload} />;
  if (!members.data) return <Loading label="Loading members" />;

  return (
    <div aria-busy={members.loading} data-stale={members.loading || undefined}>
      <DataTable
        label="Members in this segment"
        rows={members.data.items}
        rowKey={(m) => m.id}
        empty={<EmptyState title="No member matches this segment right now." />}
        columns={[
          { header: 'Name', cell: (m) => <Link to={`/members/${m.id}`}>{m.fullName}</Link> },
          { header: 'Email', cell: (m) => m.email },
        ]}
      />
      {members.data.totalCount > MEMBERS_PAGE_SIZE && (
        <Pager page={page} pageSize={MEMBERS_PAGE_SIZE} totalCount={members.data.totalCount} onChange={setPage} noun="members" />
      )}
    </div>
  );
}

function SegmentCard({ segment }: { segment: Segment }) {
  const [open, setOpen] = useState(false);
  return (
    <section className="panel stack" aria-label={segment.name}>
      <div>
        <h2>{segment.name}</h2>
        {segment.description && <p className="muted">{segment.description}</p>}
      </div>
      <ul className="criteria">
        {segment.criteria.map((c, i) => (
          <li key={i}>{describeCriterion(c)}</li>
        ))}
      </ul>
      <div>
        <button type="button" className="btn small" aria-expanded={open} onClick={() => setOpen((v) => !v)}>
          {open ? 'Hide members' : 'Show members'}
        </button>
      </div>
      {open && <SegmentMembers id={segment.id} />}
    </section>
  );
}

function NewSegmentForm({ onCreated }: { onCreated: () => void }) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [criteria, setCriteria] = useState<CriterionDraft[]>([emptyCriterion()]);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await createSegment({
        name: name.trim(),
        description: description.trim() || undefined,
        criteria: toCriteriaRequest(criteria),
      });
      onCreated();
    } catch (e) {
      setError(e);
      setBusy(false);
    }
  }

  return (
    <form className="panel stack" aria-label="New segment" onSubmit={onSubmit}>
      <h2>New segment</h2>
      <p className="muted">A member belongs to the segment when all of these conditions are true.</p>
      <div className="grid-2">
        <label className="field">
          <span>Name</span>
          <input required maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />
        </label>
        <label className="field">
          <span>
            Description <span className="hint">(optional)</span>
          </span>
          <input maxLength={500} value={description} onChange={(e) => setDescription(e.target.value)} />
        </label>
      </div>
      <CriteriaBuilder value={criteria} onChange={setCriteria} />
      {error != null && <FormError error={error} />}
      <div>
        <button type="submit" className="btn primary" disabled={busy}>
          {busy ? 'Creating…' : 'Create segment'}
        </button>
      </div>
    </form>
  );
}

export function SegmentsPage() {
  const segments = useAsync(listSegments, []);
  const [showForm, setShowForm] = useState(false);

  return (
    <>
      <h1 className="page-title">Segments</h1>
      <p className="page-lede">Groups of members defined by what they hold and how they spend. Campaigns target a segment.</p>

      <div className="toolbar">
        <button type="button" className="btn primary" aria-expanded={showForm} onClick={() => setShowForm((v) => !v)}>
          New segment
        </button>
      </div>
      {showForm && (
        <NewSegmentForm
          onCreated={() => {
            setShowForm(false);
            segments.reload();
          }}
        />
      )}

      {segments.error ? (
        <ErrorState error={segments.error} onRetry={segments.reload} />
      ) : !segments.data ? (
        <Loading label="Loading segments" />
      ) : segments.data.length === 0 ? (
        <EmptyState title="No segments yet.">Create one with the New segment button, then target it from a campaign.</EmptyState>
      ) : (
        segments.data.map((s) => <SegmentCard key={s.id} segment={s} />)
      )}
    </>
  );
}
