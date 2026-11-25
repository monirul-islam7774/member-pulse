import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { getDailyOverview, getOverview } from '../api/reports';
import type { Kpi } from '../api/types';
import { ActivityChart } from '../components/ActivityChart';
import { EmptyState, ErrorState, Loading } from '../components/States';
import { formatCurrency, formatNumber, formatPercent } from '../format';
import { useAsync } from '../useAsync';

const RANGES = [7, 30, 90];

function Figures({ totals }: { totals: Kpi }) {
  const items: [string, string][] = [
    ['Messages sent', formatNumber(totals.sent)],
    ['Converted', formatNumber(totals.converted)],
    ['Members reached', formatNumber(totals.membersReached)],
    ['Converted amount', formatCurrency(totals.convertedAmount)],
  ];
  // Problems only appear when there is something to look at.
  if (totals.pending > 0) items.push(['Pending', formatNumber(totals.pending)]);
  if (totals.failed > 0) items.push(['Failed', formatNumber(totals.failed)]);

  return (
    <ul className="figures" aria-label="Totals">
      {items.map(([label, value]) => (
        <li key={label}>
          <span className="figure-label">{label}</span>
          <span className="figure-value">{value}</span>
        </li>
      ))}
    </ul>
  );
}

export function DashboardPage() {
  const [days, setDays] = useState(30);
  const range = useMemo(() => {
    const to = new Date();
    return { from: new Date(to.getTime() - days * 86_400_000).toISOString(), to: to.toISOString() };
  }, [days]);

  const overview = useAsync(() => getOverview(range), [range]);
  const daily = useAsync(() => getDailyOverview(range), [range]);

  const error = overview.error ?? daily.error;
  const report = overview.data;
  const busy = overview.loading || daily.loading;
  const reload = () => {
    overview.reload();
    daily.reload();
  };

  return (
    <>
      <h1 className="page-title">Dashboard</h1>
      <p className="page-lede">How campaign messages are turning into member activity.</p>

      <div className="toolbar">
        <div className="range" role="group" aria-label="Date range">
          {RANGES.map((d) => (
            <button key={d} type="button" aria-pressed={d === days} onClick={() => setDays(d)}>
              {d} days
            </button>
          ))}
        </div>
      </div>

      {error ? (
        <ErrorState error={error} onRetry={reload} />
      ) : !report || !daily.data ? (
        <Loading label="Loading the dashboard" />
      ) : report.campaigns.length === 0 && report.totals.sent === 0 ? (
        <EmptyState title="No messages were sent in this period.">
          Messages are sent by campaigns. <Link to="/campaigns">Go to campaigns</Link> to set one up.
        </EmptyState>
      ) : (
        <div className="frame" aria-busy={busy} data-stale={busy || undefined}>
          <section className="hero" aria-label="Conversion">
            <div className="hero-rate">{formatPercent(report.totals.conversionRate)}</div>
            <p className="hero-caption">
              of {formatNumber(report.totals.sent)} messages led to a purchase within the campaign's window, over the last{' '}
              {days} days.
            </p>
            <div
              className="flow"
              role="img"
              aria-label={`${formatNumber(report.totals.converted)} of ${formatNumber(report.totals.sent)} messages converted`}
            >
              <i style={{ width: `${Math.min(100, Math.max(0, report.totals.conversionRate * 100))}%` }} />
            </div>
            <Figures totals={report.totals} />
          </section>

          <section className="panel">
            <h2>Messages per day</h2>
            <ActivityChart daily={daily.data.daily} from={range.from} to={range.to} />
          </section>

          <section className="panel">
            <h2>By campaign</h2>
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Campaign</th>
                    <th className="num">Sent</th>
                    <th className="num">Pending</th>
                    <th className="num">Failed</th>
                    <th className="num">Converted</th>
                    <th className="num">Rate</th>
                    <th className="num">Amount</th>
                  </tr>
                </thead>
                <tbody>
                  {report.campaigns.map((c) => (
                    <tr key={c.campaignId}>
                      <td>
                        <Link to={`/campaigns/${c.campaignId}`}>{c.name}</Link>
                      </td>
                      <td className="num">{formatNumber(c.kpis.sent)}</td>
                      <td className="num">{formatNumber(c.kpis.pending)}</td>
                      <td className="num">{formatNumber(c.kpis.failed)}</td>
                      <td className="num">{formatNumber(c.kpis.converted)}</td>
                      <td className="num">{formatPercent(c.kpis.conversionRate)}</td>
                      <td className="num">{formatCurrency(c.kpis.convertedAmount)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </div>
      )}
    </>
  );
}
