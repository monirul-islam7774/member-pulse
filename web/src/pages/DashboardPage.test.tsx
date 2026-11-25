import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { CampaignReport, DailyOverview, Kpi } from '../api/types';
import { json, mockApi, problem } from '../test/http';
import { DashboardPage } from './DashboardPage';

const zero: Kpi = { sent: 0, failed: 0, pending: 0, converted: 0, conversionRate: 0, membersReached: 0, convertedAmount: 0 };

const report: CampaignReport = {
  from: '2026-10-26T00:00:00Z',
  to: '2026-11-25T00:00:00Z',
  totals: { sent: 100, failed: 2, pending: 3, converted: 25, conversionRate: 0.25, membersReached: 80, convertedAmount: 12345.5 },
  campaigns: [
    {
      campaignId: 'c1',
      name: 'Travel push',
      kpis: { sent: 60, failed: 0, pending: 1, converted: 20, conversionRate: 20 / 60, membersReached: 50, convertedAmount: 9000 },
    },
  ],
};

const daily: DailyOverview = {
  from: report.from,
  to: report.to,
  daily: [
    { date: '2026-11-23', sent: 10, converted: 2 },
    { date: '2026-11-24', sent: 30, converted: 9 },
  ],
};

function renderPage() {
  render(
    <MemoryRouter>
      <DashboardPage />
    </MemoryRouter>,
  );
}

const ok = { 'GET /api/reports/campaigns': json(report), 'GET /api/reports/campaigns/daily': json(daily) };

describe('DashboardPage', () => {
  it('renders_totals_and_campaign_rows', async () => {
    mockApi(ok);
    renderPage();

    expect(await screen.findByText('25.0%')).toBeInTheDocument(); // the headline conversion rate
    const figures = screen.getByRole('list', { name: /totals/i });
    expect(within(figures).getByText('Messages sent').nextElementSibling).toHaveTextContent('100');
    expect(within(figures).getByText('Converted').nextElementSibling).toHaveTextContent('25');
    expect(within(figures).getByText('Members reached').nextElementSibling).toHaveTextContent('80');
    expect(within(figures).getByText('Converted amount').nextElementSibling).toHaveTextContent('$12,345.50');
    expect(within(figures).getByText('Failed').nextElementSibling).toHaveTextContent('2');
    expect(within(figures).getByText('Pending').nextElementSibling).toHaveTextContent('3');

    const row = screen.getByRole('link', { name: 'Travel push' }).closest('tr')!;
    expect(screen.getByRole('link', { name: 'Travel push' })).toHaveAttribute('href', '/campaigns/c1');
    expect(row).toHaveTextContent('33.3%');
    expect(row).toHaveTextContent('$9,000.00');
  });

  it('shows_zero_rate_not_NaN_when_nothing_was_sent', async () => {
    mockApi({
      'GET /api/reports/campaigns': json({
        ...report,
        totals: zero,
        campaigns: [{ campaignId: 'c2', name: 'Quiet one', kpis: zero }],
      }),
      'GET /api/reports/campaigns/daily': json({ ...daily, daily: [] }),
    });
    renderPage();

    const row = (await screen.findByRole('link', { name: 'Quiet one' })).closest('tr')!;
    expect(row).toHaveTextContent('0.0%');
    expect(document.body).not.toHaveTextContent(/NaN|Infinity/);
    const figures = screen.getByRole('list', { name: /totals/i });
    expect(within(figures).queryByText('Failed')).not.toBeInTheDocument(); // only shown when there is something to see
    expect(within(figures).queryByText('Pending')).not.toBeInTheDocument();
  });

  it('shows_an_empty_state_with_no_campaigns', async () => {
    mockApi({
      'GET /api/reports/campaigns': json({ ...report, totals: zero, campaigns: [] }),
      'GET /api/reports/campaigns/daily': json({ ...daily, daily: [] }),
    });
    renderPage();

    expect(await screen.findByText(/no messages were sent in this period/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /campaigns/i })).toHaveAttribute('href', '/campaigns');
  });

  it('changing_the_range_refetches_with_from_and_to', async () => {
    const { calls } = mockApi(ok);
    renderPage();
    await screen.findByText('25.0%');

    await userEvent.setup().click(screen.getByRole('button', { name: '7 days' }));

    const overview = calls.filter((c) => c.path.startsWith('/api/reports/campaigns?')).at(-1)!;
    const query = new URL(overview.path, 'http://x').searchParams;
    const days = (Date.parse(query.get('to')!) - Date.parse(query.get('from')!)) / 86_400_000;
    expect(days).toBeCloseTo(7, 1);
    expect(calls.filter((c) => c.path.startsWith('/api/reports/campaigns/daily?'))).toHaveLength(2); // default range, then 7 days
    expect(screen.getByRole('button', { name: '7 days' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('shows_an_error_state_with_retry_when_the_api_fails', async () => {
    let attempt = 0;
    mockApi({
      'GET /api/reports/campaigns': () => (++attempt === 1 ? problem(500, 'Boom', 'Database is down.') : json(report)),
      'GET /api/reports/campaigns/daily': json(daily),
    });
    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent('Database is down.');
    await userEvent.setup().click(screen.getByRole('button', { name: /try again/i }));

    expect(await screen.findByText('25.0%')).toBeInTheDocument();
  });

  it('offers_the_daily_figures_as_a_table', async () => {
    mockApi(ok);
    renderPage();
    await screen.findByText('25.0%');

    await userEvent.setup().click(screen.getByRole('button', { name: /view as table/i }));

    const table = screen.getByRole('table', { name: /messages per day/i });
    const rows = within(table).getAllByRole('row');
    expect(rows.length).toBeGreaterThan(2);
    expect(within(table).getByText('30')).toBeInTheDocument(); // the 24 Nov sent count
  });
});
