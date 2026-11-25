import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { Campaign, CampaignDetailReport, Delivery, Kpi, Paged, Segment } from '../api/types';
import { json, mockApi, problem } from '../test/http';
import { CampaignDetailPage } from './CampaignDetailPage';

const segment: Segment = { id: 's1', name: 'Whales', description: null, createdAt: '2026-11-01T00:00:00Z', criteria: [] };

const campaign: Campaign = {
  id: 'c1',
  name: 'Travel push',
  segmentId: 's1',
  channel: 'Email',
  messageTemplate: 'Planning a trip, {FirstName}?',
  direction: 'Debit',
  minAmount: 100,
  merchantCategory: 'Dining',
  cooldownDays: 30,
  conversion: { windowDays: 14, direction: 'Debit', minAmount: null, merchantCategory: 'Travel' },
  status: 'Active',
  createdAt: '2026-11-02T00:00:00Z',
};

const kpis: Kpi = { sent: 8, failed: 1, pending: 1, converted: 2, conversionRate: 0.25, membersReached: 7, convertedAmount: 940.25 };

function report(k: Kpi): CampaignDetailReport {
  return {
    from: '2026-10-26T00:00:00Z',
    to: '2026-11-25T00:00:00Z',
    campaign: { campaignId: 'c1', name: 'Travel push', kpis: k },
    daily: k.sent > 0 ? [{ date: '2026-11-24', sent: 8, converted: 2 }] : [],
  };
}

const delivery = (over: Partial<Delivery>): Delivery => ({
  id: 'd', campaignId: 'c1', memberId: 'm1', transactionId: 't1', channel: 'Email', message: 'Planning a trip, Aisha?',
  status: 'Sent', attempts: 1, error: null, createdAt: '2026-11-24T10:00:00Z', convertedAt: null,
  conversionTransactionId: null, conversionAmount: null, ...over,
});

const deliveries = (items: Delivery[]): Paged<Delivery> => ({ items, page: 1, pageSize: 10, totalCount: items.length });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/campaigns/c1']}>
      <Routes>
        <Route path="/campaigns/:id" element={<CampaignDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

const base = {
  'GET /api/campaigns/c1': json(campaign),
  'GET /api/segments': json([segment]),
  'GET /api/reports/campaigns/c1': json(report(kpis)),
  'GET /api/campaigns/c1/deliveries': json(
    deliveries([
      delivery({ id: 'd1', status: 'Sent', convertedAt: '2026-11-24T12:00:00Z', conversionAmount: 640.25, memberId: 'mA' }),
      delivery({ id: 'd2', status: 'Pending', attempts: 2, error: 'provider unavailable', memberId: 'mB' }),
      delivery({ id: 'd3', status: 'Failed', attempts: 5, error: 'mailbox full', memberId: 'mC' }),
    ]),
  ),
};

describe('CampaignDetailPage', () => {
  it('detail_shows_settings_kpis_deliveries_attempts_and_errors', async () => {
    mockApi(base);
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Travel push' })).toBeInTheDocument();
    const settings = screen.getByRole('list', { name: /settings/i });
    await waitFor(() => expect(settings).toHaveTextContent('Whales')); // the segment name arrives with its own request
    expect(settings).toHaveTextContent('A purchase of at least $100 in Dining');
    expect(settings).toHaveTextContent('Planning a trip, {FirstName}?');
    expect(settings).toHaveTextContent('At most one message every 30 days per member');
    expect(settings).toHaveTextContent('A purchase in Travel within 14 days');

    const figures = await screen.findByRole('list', { name: /totals/i });
    expect(within(figures).getByText('Messages sent').nextElementSibling).toHaveTextContent('8');
    expect(within(figures).getByText('Converted amount').nextElementSibling).toHaveTextContent('$940.25');

    const table = await screen.findByRole('table', { name: /messages/i });
    const rows = within(table).getAllByRole('row');
    expect(rows[1]).toHaveTextContent('Sent');
    expect(rows[1]).toHaveTextContent('$640.25');
    expect(rows[2]).toHaveTextContent('Pending');
    expect(rows[2]).toHaveTextContent('2'); // attempts
    expect(rows[2]).toHaveTextContent('provider unavailable');
    expect(rows[3]).toHaveTextContent('Failed');
    expect(rows[3]).toHaveTextContent('mailbox full');
    expect(within(rows[1]).getByRole('link', { name: /view member/i })).toHaveAttribute('href', '/members/mA');
  });

  it('a_campaign_with_no_deliveries_shows_zero_rate_and_an_empty_state', async () => {
    mockApi({
      ...base,
      'GET /api/reports/campaigns/c1': json(report({ sent: 0, failed: 0, pending: 0, converted: 0, conversionRate: 0, membersReached: 0, convertedAmount: 0 })),
      'GET /api/campaigns/c1/deliveries': json(deliveries([])),
    });
    renderPage();

    expect(await screen.findByText('0.0%')).toBeInTheDocument();
    expect(await screen.findByText(/no messages sent yet/i)).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(/NaN|Infinity/);
  });

  it('pausing_from_the_detail_page_updates_the_status', async () => {
    let status: Campaign['status'] = 'Active';
    mockApi({
      ...base,
      'GET /api/campaigns/c1': () => json({ ...campaign, status }),
      'POST /api/campaigns/c1/pause': () => {
        status = 'Paused';
        return json({ ...campaign, status });
      },
    });
    renderPage();

    await userEvent.setup().click(await screen.findByRole('button', { name: /^pause$/i }));

    expect(await screen.findByRole('button', { name: /^resume$/i })).toBeInTheDocument();
    expect(screen.getByText('Paused')).toBeInTheDocument();
  });

  it('unknown_campaign_shows_not_found', async () => {
    mockApi({
      'GET /api/campaigns/c1': problem(404, 'Not Found'),
      'GET /api/segments': json([segment]),
      'GET /api/reports/campaigns/c1': problem(404, 'Not Found'),
      'GET /api/campaigns/c1/deliveries': problem(404, 'Not Found'),
    });
    renderPage();

    expect(await screen.findByRole('heading', { name: /campaign not found/i })).toBeInTheDocument();
  });

  it('message_templates_and_error_text_render_as_text', async () => {
    mockApi({
      ...base,
      'GET /api/campaigns/c1': json({ ...campaign, messageTemplate: '<script>alert(1)</script>' }),
      'GET /api/campaigns/c1/deliveries': json(deliveries([delivery({ status: 'Failed', attempts: 5, error: '<b>smtp</b>' })])),
    });
    renderPage();

    expect(await screen.findByText(/<script>alert\(1\)<\/script>/)).toBeInTheDocument();
    expect(await screen.findByText('<b>smtp</b>')).toBeInTheDocument();
    expect(document.querySelector('script, b')).toBeNull();
  });
});
