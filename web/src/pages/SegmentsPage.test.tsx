import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { MemberSummary, Paged, Segment } from '../api/types';
import { json, mockApi, problem } from '../test/http';
import { SegmentsPage } from './SegmentsPage';

const whales: Segment = {
  id: 's1',
  name: 'Whales',
  description: 'Our biggest savers',
  createdAt: '2026-11-01T00:00:00Z',
  criteria: [
    { metric: 'TotalBalance', operator: 'GreaterThanOrEqual', value: 500000, accountType: 'Savings', merchantCategory: null },
    { metric: 'Age', operator: 'LessThan', value: 40, accountType: null, merchantCategory: null },
  ],
};

const memberPage = (items: MemberSummary[], totalCount = items.length): Paged<MemberSummary> => ({ items, page: 1, pageSize: 10, totalCount });

function renderPage() {
  render(
    <MemoryRouter>
      <SegmentsPage />
    </MemoryRouter>,
  );
}

describe('SegmentsPage', () => {
  it('lists_segments_with_readable_criteria', async () => {
    mockApi({ 'GET /api/segments': json([whales]) });
    renderPage();

    const segment = await screen.findByRole('region', { name: 'Whales' });
    expect(within(segment).getByText('Our biggest savers')).toBeInTheDocument();
    expect(within(segment).getByText('Total balance ≥ $500,000 in Savings')).toBeInTheDocument();
    expect(within(segment).getByText('Age < 40')).toBeInTheDocument();
  });

  it('shows_an_empty_state_when_there_are_no_segments', async () => {
    mockApi({ 'GET /api/segments': json([]) });
    renderPage();

    expect(await screen.findByText(/no segments yet/i)).toBeInTheDocument();
  });

  it('shows_matching_members', async () => {
    mockApi({
      'GET /api/segments': json([whales]),
      'GET /api/segments/s1/members': json(memberPage([{ id: 'm1', fullName: 'Aisha Rahman', email: 'aisha@example.com', accountCount: 2 }])),
    });
    renderPage();
    await screen.findByRole('region', { name: 'Whales' });

    await userEvent.setup().click(screen.getByRole('button', { name: /show members/i }));

    expect(await screen.findByRole('link', { name: 'Aisha Rahman' })).toHaveAttribute('href', '/members/m1');
    expect(screen.getByRole('button', { name: /hide members/i })).toBeInTheDocument();
  });

  it('shows_an_empty_state_when_no_member_matches', async () => {
    mockApi({ 'GET /api/segments': json([whales]), 'GET /api/segments/s1/members': json(memberPage([])) });
    renderPage();
    await screen.findByRole('region', { name: 'Whales' });

    await userEvent.setup().click(screen.getByRole('button', { name: /show members/i }));

    expect(await screen.findByText(/no member matches this segment right now/i)).toBeInTheDocument();
  });

  it('creating_a_segment_posts_the_expected_body_and_refreshes_the_list', async () => {
    let created = false;
    const { calls } = mockApi({
      'GET /api/segments': () => json(created ? [whales] : []),
      'POST /api/segments': () => {
        created = true;
        return json(whales, 201);
      },
    });
    renderPage();
    const user = userEvent.setup();
    await screen.findByText(/no segments yet/i);

    await user.click(screen.getByRole('button', { name: /new segment/i }));
    const form = screen.getByRole('form', { name: /new segment/i });
    await user.type(within(form).getByLabelText('Name'), 'Whales');
    await user.type(within(form).getByLabelText(/description/i), 'Our biggest savers');
    fireEvent.change(within(form).getByLabelText(/^account type/i), { target: { value: 'Savings' } });
    await user.clear(within(form).getByLabelText('Value'));
    await user.type(within(form).getByLabelText('Value'), '500000');
    await user.click(within(form).getByRole('button', { name: /create segment/i }));

    expect(await screen.findByRole('region', { name: 'Whales' })).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({
      name: 'Whales',
      description: 'Our biggest savers',
      criteria: [{ metric: 'TotalBalance', operator: 'GreaterThanOrEqual', value: 500000, accountType: 'Savings' }],
    });
    expect(screen.queryByRole('form', { name: /new segment/i })).not.toBeInTheDocument(); // closed after saving
  });

  it('duplicate_name_409_shows_inline_and_keeps_the_form', async () => {
    mockApi({
      'GET /api/segments': json([]),
      'POST /api/segments': problem(409, 'Segment name already used', "A segment named 'Whales' already exists."),
    });
    renderPage();
    const user = userEvent.setup();
    await screen.findByText(/no segments yet/i);
    await user.click(screen.getByRole('button', { name: /new segment/i }));
    const form = screen.getByRole('form', { name: /new segment/i });
    await user.type(within(form).getByLabelText('Name'), 'Whales');
    await user.clear(within(form).getByLabelText('Value'));
    await user.type(within(form).getByLabelText('Value'), '1');

    await user.click(within(form).getByRole('button', { name: /create segment/i }));

    expect(await within(form).findByRole('alert')).toHaveTextContent("A segment named 'Whales' already exists.");
    expect(within(form).getByLabelText('Name')).toHaveValue('Whales');
  });

  it('renders_html_in_names_as_text', async () => {
    mockApi({ 'GET /api/segments': json([{ ...whales, name: '<img src=x onerror=alert(1)>', description: '<b>x</b>' }]) });
    renderPage();

    expect(await screen.findByText('<img src=x onerror=alert(1)>')).toBeInTheDocument();
    expect(document.querySelector('img, b')).toBeNull();
  });
});
