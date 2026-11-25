import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { MemberDetail, Paged, Segment, Transaction } from '../api/types';
import { json, mockApi, problem } from '../test/http';
import { MemberDetailPage } from './MemberDetailPage';

const member: MemberDetail = {
  id: 'm1',
  firstName: 'Aisha',
  lastName: 'Rahman',
  email: 'aisha@example.com',
  dateOfBirth: '1997-06-21',
  age: 29,
  joinedAt: '2023-05-01T00:00:00Z',
  accounts: [
    { id: 'a1', type: 'Checking', balance: 4200.5, openedAt: '2023-05-01T00:00:00Z' },
    { id: 'a2', type: 'Savings', balance: 8500, openedAt: '2023-06-01T00:00:00Z' },
  ],
};

const segment: Segment = {
  id: 's1', name: 'High balance', description: null, createdAt: '2026-01-01T00:00:00Z',
  criteria: [{ metric: 'TotalBalance', operator: 'GreaterThanOrEqual', value: 10000, accountType: null, merchantCategory: null }],
};

const tx = (id: string, amount: number, description: string, category: string | null = null): Transaction => ({
  id, accountId: 'a1', amount, description, merchantCategory: category, occurredAt: '2026-11-20T10:00:00Z',
});

function txPage(items: Transaction[], page = 1, totalCount = items.length): Paged<Transaction> {
  return { items, page, pageSize: 10, totalCount };
}

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/members/m1']}>
      <Routes>
        <Route path="/members/:id" element={<MemberDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

const base = {
  'GET /api/members/m1': json(member),
  'GET /api/members/m1/segments': json([segment]),
  'GET /api/accounts/a1/transactions': json(txPage([tx('t1', -45.5, 'Dinner', 'Dining'), tx('t2', 900, 'Deposit')])),
};

describe('MemberDetailPage', () => {
  it('detail_shows_accounts_balances_and_segments', async () => {
    mockApi(base);
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Aisha Rahman' })).toBeInTheDocument();
    expect(screen.getByText('aisha@example.com')).toBeInTheDocument();
    const accounts = screen.getByRole('table', { name: /accounts/i });
    expect(within(accounts).getByText('Checking').closest('tr')).toHaveTextContent('$4,200.50');
    expect(within(accounts).getByText('Savings').closest('tr')).toHaveTextContent('$8,500.00');
    expect(await screen.findByText('High balance')).toBeInTheDocument();
    // the first account's transactions are shown, debits signed
    const transactions = await screen.findByRole('table', { name: /transactions/i });
    expect(within(transactions).getByText('Dinner').closest('tr')).toHaveTextContent('-$45.50');
    expect(within(transactions).getByText('Dinner').closest('tr')).toHaveTextContent('Dining');
  });

  it('selecting_another_account_shows_its_transactions', async () => {
    mockApi({
      ...base,
      'GET /api/accounts/a2/transactions': json(txPage([{ ...tx('t9', 8500, 'Initial deposit'), accountId: 'a2' }])),
    });
    renderPage();
    await screen.findByRole('table', { name: /transactions/i });

    await userEvent.setup().click(screen.getByRole('button', { name: /view savings transactions/i }));

    expect(await screen.findByRole('cell', { name: 'Initial deposit' })).toBeInTheDocument();
  });

  it('detail_handles_a_member_with_no_accounts', async () => {
    const { fetchMock } = mockApi({
      'GET /api/members/m1': json({ ...member, accounts: [] }),
      'GET /api/members/m1/segments': json([]),
    });
    renderPage();

    expect(await screen.findByText(/no accounts yet/i)).toBeInTheDocument();
    expect(await screen.findByText(/not in any segment/i)).toBeInTheDocument();
    expect(screen.getByRole('form', { name: /open account/i })).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([u]) => String(u).includes('/transactions'))).toBe(false);
  });

  it('shows_an_empty_state_for_an_account_with_no_transactions', async () => {
    mockApi({ ...base, 'GET /api/accounts/a1/transactions': json(txPage([])) });
    renderPage();

    expect(await screen.findByText(/no transactions on this account/i)).toBeInTheDocument();
  });

  it('posting_a_transaction_shows_the_api_error_inline_on_422_and_refreshes_on_success', async () => {
    let posts = 0;
    let balance = 4200.5;
    const { calls } = mockApi({
      'GET /api/members/m1': () => json({ ...member, accounts: [{ ...member.accounts[0], balance }, member.accounts[1]] }),
      'GET /api/members/m1/segments': json([]),
      'GET /api/accounts/a1/transactions': json(txPage([tx('t1', -45.5, 'Dinner')])),
      'POST /api/accounts/a1/transactions': () => {
        posts++;
        if (posts === 1) return problem(422, 'Transaction rejected', 'Insufficient funds.');
        balance = 4100.5;
        return json(tx('t3', -100, 'Rent'), 201);
      },
    });
    renderPage();
    const user = userEvent.setup();
    await screen.findByRole('table', { name: /transactions/i });
    const form = screen.getByRole('form', { name: /post transaction/i });

    await user.type(within(form).getByLabelText(/amount/i), '-99999');
    await user.type(within(form).getByLabelText(/description/i), 'Rent');
    await user.click(within(form).getByRole('button', { name: /post transaction/i }));
    expect(await within(form).findByRole('alert')).toHaveTextContent('Insufficient funds.');
    expect(screen.queryByText('Rent', { selector: 'td' })).not.toBeInTheDocument();

    await user.clear(within(form).getByLabelText(/amount/i));
    await user.type(within(form).getByLabelText(/amount/i), '-100');
    await user.click(within(form).getByRole('button', { name: /post transaction/i }));

    await waitFor(() => expect(screen.getByText('$4,100.50')).toBeInTheDocument()); // balance refreshed
    expect(within(form).queryByRole('alert')).not.toBeInTheDocument();
    expect(within(form).getByLabelText(/description/i)).toHaveValue('');
    const posted = calls.filter((c) => c.method === 'POST').at(-1)!;
    expect(posted.body).toEqual({ amount: -100, description: 'Rent', merchantCategory: undefined });
  });

  it('refuses_a_zero_or_empty_amount_without_calling_the_api', async () => {
    const { calls } = mockApi(base);
    renderPage();
    const user = userEvent.setup();
    await screen.findByRole('table', { name: /transactions/i });
    const form = screen.getByRole('form', { name: /post transaction/i });

    await user.type(within(form).getByLabelText(/amount/i), '0');
    await user.type(within(form).getByLabelText(/description/i), 'Nothing');
    await user.click(within(form).getByRole('button', { name: /post transaction/i }));

    expect(await within(form).findByRole('alert')).toHaveTextContent(/amount cannot be zero/i);
    expect(calls.some((c) => c.method === 'POST')).toBe(false);
  });

  it('opening_an_account_posts_the_type_and_deposit_and_refreshes', async () => {
    const { calls } = mockApi({
      ...base,
      'POST /api/members/m1/accounts': json({ id: 'a3', type: 'Savings', balance: 250, openedAt: '2026-11-25T00:00:00Z' }, 201),
    });
    renderPage();
    const user = userEvent.setup();
    await screen.findByRole('table', { name: /transactions/i });
    const form = screen.getByRole('form', { name: /open account/i });

    fireEvent.change(within(form).getByLabelText(/account type/i), { target: { value: 'Savings' } });
    await user.type(within(form).getByLabelText(/initial deposit/i), '250');
    await user.click(within(form).getByRole('button', { name: /open account/i }));

    await waitFor(() => expect(calls.filter((c) => c.path === '/api/members/m1').length).toBe(2));
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ type: 'Savings', initialDeposit: 250 });
  });

  it('renders_html_in_names_and_descriptions_as_text', async () => {
    mockApi({
      ...base,
      'GET /api/members/m1': json({ ...member, firstName: '<script>alert(1)</script>' }),
      'GET /api/accounts/a1/transactions': json(txPage([tx('t1', -5, '<b>bold</b>')])),
    });
    renderPage();

    expect(await screen.findByRole('heading', { name: /<script>alert\(1\)<\/script> Rahman/ })).toBeInTheDocument();
    expect(await screen.findByText('<b>bold</b>')).toBeInTheDocument();
    expect(document.querySelector('script, b')).toBeNull();
  });

  it('unknown_member_shows_not_found', async () => {
    mockApi({
      'GET /api/members/m1': problem(404, 'Not Found'),
      'GET /api/members/m1/segments': problem(404, 'Not Found'),
    });
    renderPage();

    expect(await screen.findByRole('heading', { name: /member not found/i })).toBeInTheDocument();
  });
});
