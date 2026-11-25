import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { MemberDetail, MemberSummary, Paged } from '../api/types';
import { json, mockApi, problem } from '../test/http';
import { MembersPage } from './MembersPage';

const members: MemberSummary[] = [
  { id: 'm1', fullName: 'Aisha Rahman', email: 'aisha@example.com', accountCount: 2 },
  { id: 'm2', fullName: 'Omar Faruk', email: 'omar@example.com', accountCount: 1 },
];

function pageOf(page: number, items = members, totalCount = 45): Paged<MemberSummary> {
  return { items, page, pageSize: 20, totalCount };
}

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/members']}>
      <Routes>
        <Route path="/members" element={<MembersPage />} />
        <Route path="/members/:id" element={<p>member detail page</p>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('MembersPage', () => {
  it('lists_members_and_pages', async () => {
    const { calls } = mockApi({
      'GET /api/members': (url) => json(pageOf(Number(url.searchParams.get('page')))),
    });
    renderPage();

    const link = await screen.findByRole('link', { name: 'Aisha Rahman' });
    expect(link).toHaveAttribute('href', '/members/m1');
    expect(link.closest('tr')).toHaveTextContent('aisha@example.com');
    expect(screen.getByText(/page 1 of 3/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /previous/i })).toBeDisabled();

    await userEvent.setup().click(screen.getByRole('button', { name: /next/i }));

    expect(await screen.findByText(/page 2 of 3/i)).toBeInTheDocument();
    expect(calls.at(-1)?.path).toBe('/api/members?page=2&pageSize=20');
  });

  it('renders_html_in_names_as_text', async () => {
    mockApi({
      'GET /api/members': json(
        pageOf(1, [{ id: 'x', fullName: '<img src=x onerror=alert(1)>', email: 'x@example.com', accountCount: 0 }], 1),
      ),
    });
    renderPage();

    expect(await screen.findByText('<img src=x onerror=alert(1)>')).toBeInTheDocument();
    expect(document.querySelector('img')).toBeNull();
  });

  it('shows_an_empty_state_when_there_are_no_members', async () => {
    mockApi({ 'GET /api/members': json(pageOf(1, [], 0)) });
    renderPage();

    expect(await screen.findByText(/no members yet/i)).toBeInTheDocument();
  });

  it('shows_an_error_state_with_retry_when_the_list_fails_to_load', async () => {
    mockApi({ 'GET /api/members': problem(500, 'Boom', 'Database is down.') });
    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent('Database is down.');
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument();
  });

  it('creating_a_member_opens_the_new_member', async () => {
    const created: MemberDetail = {
      id: 'new1', firstName: 'Nadia', lastName: 'Khan', email: 'nadia@example.com',
      dateOfBirth: '1992-02-03', age: 34, joinedAt: '2026-11-25T00:00:00Z', accounts: [],
    };
    const { calls } = mockApi({ 'GET /api/members': json(pageOf(1)), 'POST /api/members': json(created, 201) });
    renderPage();
    const user = userEvent.setup();
    await screen.findByRole('link', { name: 'Aisha Rahman' });

    await user.click(screen.getByRole('button', { name: /new member/i }));
    await user.type(screen.getByLabelText(/first name/i), 'Nadia');
    await user.type(screen.getByLabelText(/last name/i), 'Khan');
    await user.type(screen.getByLabelText(/email/i), 'nadia@example.com');
    fireEvent.change(screen.getByLabelText(/date of birth/i), { target: { value: '1992-02-03' } });
    await user.click(screen.getByRole('button', { name: /create member/i }));

    expect(await screen.findByText('member detail page')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({
      firstName: 'Nadia', lastName: 'Khan', email: 'nadia@example.com', dateOfBirth: '1992-02-03',
    });
  });

  it('shows_the_duplicate_email_conflict_and_validation_messages_inline', async () => {
    let attempt = 0;
    mockApi({
      'GET /api/members': json(pageOf(1)),
      'POST /api/members': () =>
        ++attempt === 1
          ? problem(409, 'Email already registered', "A member with email 'dup@example.com' already exists.")
          : new Response(
              JSON.stringify({ title: 'One or more validation errors occurred.', errors: { DateOfBirth: ['Date of birth cannot be in the future.'] } }),
              { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
            ),
    });
    renderPage();
    const user = userEvent.setup();
    await screen.findByRole('link', { name: 'Aisha Rahman' });
    await user.click(screen.getByRole('button', { name: /new member/i }));
    await user.type(screen.getByLabelText(/first name/i), 'Dup');
    await user.type(screen.getByLabelText(/last name/i), 'User');
    await user.type(screen.getByLabelText(/email/i), 'dup@example.com');
    fireEvent.change(screen.getByLabelText(/date of birth/i), { target: { value: '1990-01-01' } });

    await user.click(screen.getByRole('button', { name: /create member/i }));
    const form = screen.getByRole('form', { name: /new member/i });
    expect(await within(form).findByRole('alert')).toHaveTextContent("A member with email 'dup@example.com' already exists.");

    await user.click(screen.getByRole('button', { name: /create member/i }));
    expect(await within(form).findByText(/date of birth cannot be in the future/i)).toBeInTheDocument();
  });
});
