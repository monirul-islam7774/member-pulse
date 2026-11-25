import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { Campaign, Segment } from '../api/types';
import { json, mockApi, problem } from '../test/http';
import { CampaignsPage } from './CampaignsPage';

const whales: Segment = { id: 's1', name: 'Whales', description: null, createdAt: '2026-11-01T00:00:00Z', criteria: [] };

const travel: Campaign = {
  id: 'c1',
  name: 'Travel push',
  segmentId: 's1',
  channel: 'Email',
  messageTemplate: 'Planning a trip, {FirstName}?',
  direction: 'Debit',
  minAmount: null,
  merchantCategory: 'Dining',
  cooldownDays: 30,
  conversion: { windowDays: 7, direction: 'Debit', minAmount: null, merchantCategory: 'Travel' },
  status: 'Active',
  createdAt: '2026-11-02T00:00:00Z',
};

function renderPage() {
  render(
    <MemoryRouter>
      <CampaignsPage />
    </MemoryRouter>,
  );
}

describe('CampaignsPage', () => {
  it('lists_campaigns_with_status_and_toggles_pause_and_resume', async () => {
    let status: Campaign['status'] = 'Active';
    const { calls } = mockApi({
      'GET /api/campaigns': () => json([{ ...travel, status }]),
      'GET /api/segments': json([whales]),
      'POST /api/campaigns/c1/pause': () => {
        status = 'Paused';
        return json({ ...travel, status });
      },
      'POST /api/campaigns/c1/resume': () => {
        status = 'Active';
        return json({ ...travel, status });
      },
    });
    renderPage();
    const user = userEvent.setup();

    const link = await screen.findByRole('link', { name: 'Travel push' });
    const row = link.closest('tr')!;
    expect(link).toHaveAttribute('href', '/campaigns/c1');
    expect(row).toHaveTextContent('Active');
    await waitFor(() => expect(row).toHaveTextContent('Whales'));
    expect(row).toHaveTextContent('Email');

    await user.click(screen.getByRole('button', { name: 'Pause Travel push' }));
    expect(await screen.findByRole('button', { name: 'Resume Travel push' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Travel push' }).closest('tr')).toHaveTextContent('Paused');

    await user.click(screen.getByRole('button', { name: 'Resume Travel push' }));
    expect(await screen.findByRole('button', { name: 'Pause Travel push' })).toBeInTheDocument();
    expect(calls.filter((c) => c.method === 'POST').map((c) => c.path)).toEqual(['/api/campaigns/c1/pause', '/api/campaigns/c1/resume']);
  });

  it('shows_an_empty_state_when_there_are_no_campaigns', async () => {
    mockApi({ 'GET /api/campaigns': json([]), 'GET /api/segments': json([whales]) });
    renderPage();

    expect(await screen.findByText(/no campaigns yet/i)).toBeInTheDocument();
  });

  it('shows_why_a_pause_failed', async () => {
    mockApi({
      'GET /api/campaigns': json([travel]),
      'GET /api/segments': json([whales]),
      'POST /api/campaigns/c1/pause': problem(500, 'Boom', 'Database is down.'),
    });
    renderPage();

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Pause Travel push' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Database is down.');
  });

  it('create_form_defaults_conversion_to_7_days_debit', async () => {
    mockApi({ 'GET /api/campaigns': json([]), 'GET /api/segments': json([whales]) });
    renderPage();
    await screen.findByText(/no campaigns yet/i);

    await userEvent.setup().click(screen.getByRole('button', { name: /new campaign/i }));

    const form = screen.getByRole('form', { name: /new campaign/i });
    expect(within(form).getByLabelText(/^conversion window/i)).toHaveValue(7);
    expect(within(form).getByLabelText(/^conversion type/i)).toHaveValue('Debit');
    expect(within(form).getByText(/\{FirstName\}/)).toBeInTheDocument(); // template placeholders are explained
  });

  it('create_sends_exactly_the_api_field_names', async () => {
    let created = false;
    const { calls } = mockApi({
      'GET /api/campaigns': () => json(created ? [travel] : []),
      'GET /api/segments': json([whales]),
      'POST /api/campaigns': () => {
        created = true;
        return json(travel, 201);
      },
    });
    renderPage();
    const user = userEvent.setup();
    await screen.findByText(/no campaigns yet/i);
    await user.click(screen.getByRole('button', { name: /new campaign/i }));
    const form = screen.getByRole('form', { name: /new campaign/i });

    await user.type(within(form).getByLabelText('Name'), 'Travel push');
    fireEvent.change(within(form).getByLabelText('Segment'), { target: { value: 's1' } });
    fireEvent.change(within(form).getByLabelText('Channel'), { target: { value: 'Sms' } });
    fireEvent.change(within(form).getByLabelText('Message'), { target: { value: 'Planning a trip, {FirstName}?' } });
    await user.type(within(form).getByLabelText(/^minimum amount/i), '100');
    await user.type(within(form).getByLabelText(/^merchant category/i), 'Dining');
    await user.clear(within(form).getByLabelText(/^days between messages/i));
    await user.type(within(form).getByLabelText(/^days between messages/i), '30');
    await user.clear(within(form).getByLabelText(/^conversion window/i));
    await user.type(within(form).getByLabelText(/^conversion window/i), '14');
    await user.type(within(form).getByLabelText(/^conversion minimum amount/i), '250');
    await user.type(within(form).getByLabelText(/^conversion merchant category/i), 'Travel');
    await user.click(within(form).getByRole('button', { name: /create campaign/i }));

    expect(await screen.findByRole('link', { name: 'Travel push' })).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({
      name: 'Travel push',
      segmentId: 's1',
      channel: 'Sms',
      messageTemplate: 'Planning a trip, {FirstName}?',
      direction: 'Debit',
      minAmount: 100,
      merchantCategory: 'Dining',
      cooldownDays: 30,
      conversionWindowDays: 14,
      conversionDirection: 'Debit',
      conversionMinAmount: 250,
      conversionMerchantCategory: 'Travel',
    });
    expect(screen.queryByRole('form', { name: /new campaign/i })).not.toBeInTheDocument();
  });

  it('omits_optional_fields_that_were_left_empty', async () => {
    const { calls } = mockApi({
      'GET /api/campaigns': json([]),
      'GET /api/segments': json([whales]),
      'POST /api/campaigns': json(travel, 201),
    });
    renderPage();
    const user = userEvent.setup();
    await screen.findByText(/no campaigns yet/i);
    await user.click(screen.getByRole('button', { name: /new campaign/i }));
    const form = screen.getByRole('form', { name: /new campaign/i });
    await user.type(within(form).getByLabelText('Name'), 'Plain');
    fireEvent.change(within(form).getByLabelText('Segment'), { target: { value: 's1' } });
    fireEvent.change(within(form).getByLabelText('Message'), { target: { value: 'Hello' } });

    await user.click(within(form).getByRole('button', { name: /create campaign/i }));

    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true));
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({
      name: 'Plain',
      segmentId: 's1',
      channel: 'Email',
      messageTemplate: 'Hello',
      direction: 'Debit',
      cooldownDays: 7,
      conversionWindowDays: 7,
      conversionDirection: 'Debit',
    });
  });

  it('asks_for_a_segment_first_when_there_are_none', async () => {
    mockApi({ 'GET /api/campaigns': json([]), 'GET /api/segments': json([]) });
    renderPage();
    await screen.findByText(/no campaigns yet/i);

    await userEvent.setup().click(screen.getByRole('button', { name: /new campaign/i }));

    const form = screen.getByRole('form', { name: /new campaign/i });
    expect(within(form).getByRole('link', { name: /create a segment/i })).toHaveAttribute('href', '/segments');
    expect(within(form).getByRole('button', { name: /create campaign/i })).toBeDisabled();
  });

  it('duplicate_name_409_shows_inline_and_keeps_the_form', async () => {
    mockApi({
      'GET /api/campaigns': json([]),
      'GET /api/segments': json([whales]),
      'POST /api/campaigns': problem(409, 'Campaign name already used', "A campaign named 'Dup' already exists."),
    });
    renderPage();
    const user = userEvent.setup();
    await screen.findByText(/no campaigns yet/i);
    await user.click(screen.getByRole('button', { name: /new campaign/i }));
    const form = screen.getByRole('form', { name: /new campaign/i });
    await user.type(within(form).getByLabelText('Name'), 'Dup');
    fireEvent.change(within(form).getByLabelText('Segment'), { target: { value: 's1' } });
    fireEvent.change(within(form).getByLabelText('Message'), { target: { value: 'Hello' } });

    await user.click(within(form).getByRole('button', { name: /create campaign/i }));

    expect(await within(form).findByRole('alert')).toHaveTextContent("A campaign named 'Dup' already exists.");
    expect(within(form).getByLabelText('Name')).toHaveValue('Dup');
  });

  it('renders_html_in_campaign_names_as_text', async () => {
    mockApi({
      'GET /api/campaigns': json([{ ...travel, name: '<img src=x onerror=alert(1)>' }]),
      'GET /api/segments': json([whales]),
    });
    renderPage();

    expect(await screen.findByText('<img src=x onerror=alert(1)>')).toBeInTheDocument();
    expect(document.querySelector('img')).toBeNull();
  });

  it('shows_a_retry_in_the_form_when_the_segments_fail_to_load_instead_of_asking_for_a_segment', async () => {
    let attempt = 0;
    mockApi({
      'GET /api/campaigns': json([travel]),
      'GET /api/segments': () => (++attempt === 1 ? problem(500, 'Boom', 'Segments are unavailable.') : json([whales])),
    });
    renderPage();
    const user = userEvent.setup();
    const row = (await screen.findByRole('link', { name: 'Travel push' })).closest('tr')!;
    await waitFor(() => expect(row).toHaveTextContent('Unavailable')); // not a blank cell

    await user.click(screen.getByRole('button', { name: /new campaign/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Segments are unavailable.');
    expect(screen.queryByText(/create a segment/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('form', { name: /new campaign/i })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /try again/i }));

    expect(await screen.findByRole('form', { name: /new campaign/i })).toBeInTheDocument();
    expect(row).toHaveTextContent('Whales');
  });

  it('does_not_claim_there_are_no_segments_while_they_are_still_loading', async () => {
    let release: (r: Response) => void = () => {};
    mockApi({
      'GET /api/campaigns': json([]),
      'GET /api/segments': () => new Promise<Response>((resolve) => { release = resolve; }),
    });
    renderPage();
    const user = userEvent.setup();
    await screen.findByText(/no campaigns yet/i);

    await user.click(screen.getByRole('button', { name: /new campaign/i }));

    expect(screen.getByRole('status')).toHaveTextContent(/loading segments/i);
    expect(screen.queryByText(/create a segment/i)).not.toBeInTheDocument();

    release(json([whales]));
    expect(await screen.findByRole('form', { name: /new campaign/i })).toBeInTheDocument();
    expect(screen.queryByText(/create a segment/i)).not.toBeInTheDocument();
  });
});
