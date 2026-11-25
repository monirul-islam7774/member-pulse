import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import { getCampaign, listDeliveries, pauseCampaign, resumeCampaign } from '../api/campaigns';
import { getCampaignReport } from '../api/reports';
import { listSegments } from '../api/segments';
import type { Campaign } from '../api/types';
import { ActivityChart } from '../components/ActivityChart';
import { DataTable } from '../components/DataTable';
import { Figures } from '../components/Figures';
import { Pager } from '../components/Pager';
import { EmptyState, ErrorState, FormError, Loading } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import { formatCurrency, formatDateTime, formatPercent } from '../format';
import { channelLabels, describeConversion, describeCooldown, describeTrigger } from '../labels';
import { useAsync } from '../useAsync';

const PAGE_SIZE = 10;

function Settings({ campaign, segmentName }: { campaign: Campaign; segmentName: string }) {
  const rows: [string, string][] = [
    ['Segment', segmentName],
    ['Channel', channelLabels[campaign.channel]],
    ['Sent when a member makes', describeTrigger(campaign)],
    ['Message', campaign.messageTemplate],
    ['Frequency', describeCooldown(campaign.cooldownDays)],
    ['Counts as a conversion', describeConversion(campaign.conversion)],
  ];
  return (
    <ul className="settings" aria-label="Settings">
      {rows.map(([label, value]) => (
        <li key={label}>
          <span className="figure-label">{label}</span>
          <span>{value}</span>
        </li>
      ))}
    </ul>
  );
}

function CampaignView({ campaign, onChanged }: { campaign: Campaign; onChanged: () => void }) {
  const segments = useAsync(listSegments, []);
  const report = useAsync(() => getCampaignReport(campaign.id), [campaign.id]);
  const [page, setPage] = useState(1);
  const deliveries = useAsync(() => listDeliveries(campaign.id, page, PAGE_SIZE), [campaign.id, page]);
  const [actionError, setActionError] = useState<unknown>(null);

  async function toggle() {
    setActionError(null);
    try {
      await (campaign.status === 'Active' ? pauseCampaign(campaign.id) : resumeCampaign(campaign.id));
      onChanged();
    } catch (e) {
      setActionError(e);
    }
  }

  const kpis = report.data?.campaign.kpis;

  return (
    <>
      <p className="muted">
        <Link to="/campaigns">Campaigns</Link>
      </p>
      <div className="toolbar">
        <h1 className="page-title">{campaign.name}</h1>
        <StatusBadge status={campaign.status} />
        <span className="spacer" />
        <button type="button" className="btn" onClick={toggle}>
          {campaign.status === 'Active' ? 'Pause' : 'Resume'}
        </button>
      </div>
      {actionError != null && <FormError error={actionError} />}

      <section className="panel">
        <h2>Settings</h2>
        <Settings
          campaign={campaign}
          segmentName={
            segments.error
              ? 'Unavailable'
              : segments.data
                ? (segments.data.find((s) => s.id === campaign.segmentId)?.name ?? 'Unknown segment')
                : ''
          }
        />
      </section>

      <section className="panel stack">
        <h2>Results, last 30 days</h2>
        {report.error ? (
          <ErrorState error={report.error} onRetry={report.reload} />
        ) : !report.data || !kpis ? (
          <Loading label="Loading results" />
        ) : (
          <>
            <div>
              <div className="hero-rate small">{formatPercent(kpis.conversionRate)}</div>
              <p className="hero-caption">of messages led to a conversion within the window.</p>
            </div>
            <Figures totals={kpis} />
            <ActivityChart daily={report.data.daily} from={report.data.from} to={report.data.to} />
          </>
        )}
      </section>

      <section className="panel stack">
        <h2>Messages</h2>
        {deliveries.error ? (
          <ErrorState error={deliveries.error} onRetry={deliveries.reload} />
        ) : !deliveries.data ? (
          <Loading label="Loading messages" />
        ) : (
          <div aria-busy={deliveries.loading} data-stale={deliveries.loading || undefined}>
            <DataTable
              label="Messages"
              rows={deliveries.data.items}
              rowKey={(d) => d.id}
              empty={<EmptyState title="No messages sent yet.">Messages appear here when a member's transaction matches this campaign.</EmptyState>}
              columns={[
                { header: 'Created', cell: (d) => formatDateTime(d.createdAt) },
                { header: 'Member', cell: (d) => <Link to={`/members/${d.memberId}`}>View member</Link> },
                { header: 'Status', cell: (d) => <StatusBadge status={d.status} /> },
                { header: 'Attempts', numeric: true, cell: (d) => d.attempts },
                {
                  header: 'Result',
                  cell: (d) =>
                    d.convertedAt
                      ? `Converted ${formatCurrency(d.conversionAmount ?? 0)} on ${formatDateTime(d.convertedAt)}`
                      : (d.error ?? ''),
                },
              ]}
            />
            {deliveries.data.totalCount > PAGE_SIZE && (
              <Pager page={page} pageSize={PAGE_SIZE} totalCount={deliveries.data.totalCount} onChange={setPage} noun="messages" />
            )}
          </div>
        )}
      </section>
    </>
  );
}

/** Keyed by id: a different campaign is a fresh page, so nothing of the previous one lingers under the new URL. */
export function CampaignDetailPage() {
  const { id = '' } = useParams();
  return <CampaignDetailLoader key={id} id={id} />;
}

function CampaignDetailLoader({ id }: { id: string }) {
  const campaign = useAsync(() => getCampaign(id), [id]);

  if (campaign.error instanceof ApiError && campaign.error.status === 404) {
    return (
      <>
        <h1 className="page-title">Campaign not found</h1>
        <p className="muted">
          There is no campaign with this ID. <Link to="/campaigns">Back to campaigns</Link>.
        </p>
      </>
    );
  }
  if (campaign.error) return <ErrorState error={campaign.error} onRetry={campaign.reload} />;
  if (!campaign.data) return <Loading label="Loading campaign" />;

  return <CampaignView campaign={campaign.data} onChanged={campaign.reload} />;
}
