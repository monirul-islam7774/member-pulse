import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { createCampaign, listCampaigns, pauseCampaign, resumeCampaign, type CreateCampaignBody } from '../api/campaigns';
import { listSegments } from '../api/segments';
import type { Campaign, CampaignChannel, Segment, TransactionDirection } from '../api/types';
import { DataTable } from '../components/DataTable';
import { EmptyState, ErrorState, FormError, Loading } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import { channelLabels, channels, describeTrigger, directionLabels, directions } from '../labels';
import { useAsync } from '../useAsync';

interface Draft {
  name: string;
  segmentId: string;
  channel: CampaignChannel;
  messageTemplate: string;
  direction: TransactionDirection;
  minAmount: string;
  merchantCategory: string;
  cooldownDays: string;
  conversionWindowDays: string;
  conversionDirection: TransactionDirection;
  conversionMinAmount: string;
  conversionMerchantCategory: string;
}

// Defaults: message on purchases, at most once a week per member; a conversion is any purchase within 7 days.
const blank: Draft = {
  name: '',
  segmentId: '',
  channel: 'Email',
  messageTemplate: '',
  direction: 'Debit',
  minAmount: '',
  merchantCategory: '',
  cooldownDays: '7',
  conversionWindowDays: '7',
  conversionDirection: 'Debit',
  conversionMinAmount: '',
  conversionMerchantCategory: '',
};

function toBody(d: Draft): CreateCampaignBody {
  const body: CreateCampaignBody = {
    name: d.name.trim(),
    segmentId: d.segmentId,
    channel: d.channel,
    messageTemplate: d.messageTemplate.trim(),
    direction: d.direction,
    cooldownDays: Number(d.cooldownDays),
    conversionWindowDays: Number(d.conversionWindowDays),
    conversionDirection: d.conversionDirection,
  };
  if (d.minAmount !== '') body.minAmount = Number(d.minAmount);
  if (d.merchantCategory.trim()) body.merchantCategory = d.merchantCategory.trim();
  if (d.conversionMinAmount !== '') body.conversionMinAmount = Number(d.conversionMinAmount);
  if (d.conversionMerchantCategory.trim()) body.conversionMerchantCategory = d.conversionMerchantCategory.trim();
  return body;
}

function NewCampaignForm({ segments, onCreated }: { segments: Segment[]; onCreated: () => void }) {
  const [draft, setDraft] = useState<Draft>(blank);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const set = <K extends keyof Draft>(key: K) => (e: { target: { value: string } }) =>
    setDraft({ ...draft, [key]: e.target.value as Draft[K] });

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await createCampaign(toBody(draft));
      onCreated();
    } catch (e) {
      setError(e);
      setBusy(false);
    }
  }

  const noSegments = segments.length === 0;

  return (
    <form className="panel stack" aria-label="New campaign" onSubmit={onSubmit}>
      <h2>New campaign</h2>

      <div className="grid-2">
        <label className="field">
          <span>Name</span>
          <input required maxLength={100} value={draft.name} onChange={set('name')} />
        </label>
        <label className="field">
          <span>Segment</span>
          <select required value={draft.segmentId} onChange={set('segmentId')}>
            <option value="">Choose a segment</option>
            {segments.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>Channel</span>
          <select value={draft.channel} onChange={set('channel')}>
            {channels.map((c) => (
              <option key={c} value={c}>
                {channelLabels[c]}
              </option>
            ))}
          </select>
        </label>
      </div>
      {noSegments && (
        <p className="notice">
          A campaign needs a segment to target. <Link to="/segments">Create a segment</Link> first.
        </p>
      )}

      <label className="field">
        <span>Message</span>
        <textarea required maxLength={1000} value={draft.messageTemplate} onChange={set('messageTemplate')} />
      </label>
      <p className="muted">
        You can use {'{FirstName}'}, {'{Amount}'} (the purchase amount) and {'{MerchantCategory}'} in the message.
      </p>

      <fieldset className="stack">
        <legend>When to send</legend>
        <div className="grid-2">
          <label className="field">
            <span>Triggered by</span>
            <select value={draft.direction} onChange={set('direction')}>
              {directions.map((d) => (
                <option key={d} value={d}>
                  {directionLabels[d]}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>
              Minimum amount <span className="hint">(optional)</span>
            </span>
            <input type="number" min="0" step="0.01" value={draft.minAmount} onChange={set('minAmount')} />
          </label>
          <label className="field">
            <span>
              Merchant category <span className="hint">(optional)</span>
            </span>
            <input maxLength={50} value={draft.merchantCategory} onChange={set('merchantCategory')} />
          </label>
          <label className="field">
            <span>Days between messages to the same member</span>
            <input type="number" min="0" max="3650" required value={draft.cooldownDays} onChange={set('cooldownDays')} />
          </label>
        </div>
      </fieldset>

      <fieldset className="stack">
        <legend>What counts as a conversion</legend>
        <div className="grid-2">
          <label className="field">
            <span>Conversion type</span>
            <select value={draft.conversionDirection} onChange={set('conversionDirection')}>
              {directions.map((d) => (
                <option key={d} value={d}>
                  {directionLabels[d]}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>Conversion window (days)</span>
            <input type="number" min="1" max="90" required value={draft.conversionWindowDays} onChange={set('conversionWindowDays')} />
          </label>
          <label className="field">
            <span>
              Conversion minimum amount <span className="hint">(optional)</span>
            </span>
            <input type="number" min="0" step="0.01" value={draft.conversionMinAmount} onChange={set('conversionMinAmount')} />
          </label>
          <label className="field">
            <span>
              Conversion merchant category <span className="hint">(optional)</span>
            </span>
            <input maxLength={50} value={draft.conversionMerchantCategory} onChange={set('conversionMerchantCategory')} />
          </label>
        </div>
      </fieldset>

      {error != null && <FormError error={error} />}
      <div>
        <button type="submit" className="btn primary" disabled={busy || noSegments}>
          {busy ? 'Creating…' : 'Create campaign'}
        </button>
      </div>
    </form>
  );
}

export function CampaignsPage() {
  const campaigns = useAsync(listCampaigns, []);
  const segments = useAsync(listSegments, []);
  const [showForm, setShowForm] = useState(false);
  const [actionError, setActionError] = useState<unknown>(null);

  // Blank while the segments load, so a real segment never flashes as "Unknown".
  const segmentName = (id: string) => (segments.data ? (segments.data.find((s) => s.id === id)?.name ?? 'Unknown segment') : '');

  async function toggle(c: Campaign) {
    setActionError(null);
    try {
      await (c.status === 'Active' ? pauseCampaign(c.id) : resumeCampaign(c.id));
      campaigns.reload();
    } catch (e) {
      setActionError(e);
    }
  }

  return (
    <>
      <h1 className="page-title">Campaigns</h1>
      <p className="page-lede">Messages sent to a segment when a member's transaction matches. Pause a campaign to stop new messages.</p>

      <div className="toolbar">
        <button type="button" className="btn primary" aria-expanded={showForm} onClick={() => setShowForm((v) => !v)}>
          New campaign
        </button>
      </div>
      {showForm && (
        <NewCampaignForm
          segments={segments.data ?? []}
          onCreated={() => {
            setShowForm(false);
            campaigns.reload();
          }}
        />
      )}
      {actionError != null && <FormError error={actionError} />}

      {campaigns.error ? (
        <ErrorState error={campaigns.error} onRetry={campaigns.reload} />
      ) : !campaigns.data ? (
        <Loading label="Loading campaigns" />
      ) : (
        <div className="panel" aria-busy={campaigns.loading} data-stale={campaigns.loading || undefined}>
          <DataTable
            label="Campaigns"
            rows={campaigns.data}
            rowKey={(c) => c.id}
            empty={<EmptyState title="No campaigns yet.">Create one with the New campaign button to start messaging a segment.</EmptyState>}
            columns={[
              { header: 'Name', cell: (c) => <Link to={`/campaigns/${c.id}`}>{c.name}</Link> },
              { header: 'Status', cell: (c) => <StatusBadge status={c.status} /> },
              { header: 'Segment', cell: (c) => segmentName(c.segmentId) },
              { header: 'Channel', cell: (c) => channelLabels[c.channel] },
              { header: 'Sent when', cell: (c) => describeTrigger(c) },
              {
                header: 'Action',
                cell: (c) => (
                  <button
                    type="button"
                    className="btn small"
                    aria-label={`${c.status === 'Active' ? 'Pause' : 'Resume'} ${c.name}`}
                    onClick={() => toggle(c)}
                  >
                    {c.status === 'Active' ? 'Pause' : 'Resume'}
                  </button>
                ),
              },
            ]}
          />
        </div>
      )}
    </>
  );
}
