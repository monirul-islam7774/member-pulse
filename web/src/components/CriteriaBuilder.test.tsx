import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { CriteriaBuilder, emptyCriterion, toCriteriaRequest, type CriterionDraft } from './CriteriaBuilder';

let latest: CriterionDraft[] = [];

function Harness({ initial = [emptyCriterion()] }: { initial?: CriterionDraft[] }) {
  const [value, setValue] = useState(initial);
  latest = value;
  return <CriteriaBuilder value={value} onChange={setValue} />;
}

const row = (n = 1) => screen.getByRole('group', { name: `Criterion ${n}` });

describe('CriteriaBuilder', () => {
  it('builder_offers_scope_fields_only_for_the_metrics_that_accept_them', () => {
    render(<Harness />);
    const metric = within(row()).getByLabelText('Metric');

    // default metric: total balance -> account type only
    expect(within(row()).getByLabelText(/^account type/i)).toBeInTheDocument();
    expect(within(row()).queryByLabelText(/^merchant category/i)).not.toBeInTheDocument();

    fireEvent.change(metric, { target: { value: 'Spend30d' } });
    expect(within(row()).getByLabelText(/^merchant category/i)).toBeInTheDocument();
    expect(within(row()).queryByLabelText(/^account type/i)).not.toBeInTheDocument();

    fireEvent.change(metric, { target: { value: 'Age' } });
    expect(within(row()).queryByLabelText(/^account type/i)).not.toBeInTheDocument();
    expect(within(row()).queryByLabelText(/^merchant category/i)).not.toBeInTheDocument();
  });

  it('switching_the_metric_drops_a_scope_that_no_longer_applies', () => {
    render(<Harness />);
    fireEvent.change(within(row()).getByLabelText(/^account type/i), { target: { value: 'Savings' } });
    fireEvent.change(within(row()).getByLabelText('Value'), { target: { value: '500' } });

    fireEvent.change(within(row()).getByLabelText('Metric'), { target: { value: 'Age' } });

    expect(toCriteriaRequest(latest)).toEqual([{ metric: 'Age', operator: 'GreaterThanOrEqual', value: 500 }]);
  });

  it('builder_requires_at_least_one_criterion', async () => {
    render(<Harness />);
    expect(screen.getByRole('button', { name: /remove criterion 1/i })).toBeDisabled();

    await userEvent.setup().click(screen.getByRole('button', { name: /add criterion/i }));

    expect(screen.getAllByRole('group', { name: /criterion \d/i })).toHaveLength(2);
    expect(screen.getByRole('button', { name: /remove criterion 1/i })).toBeEnabled();
  });

  it('removes_the_chosen_row', async () => {
    render(<Harness initial={[{ ...emptyCriterion(), value: '1' }, { ...emptyCriterion(), value: '2' }]} />);

    await userEvent.setup().click(screen.getByRole('button', { name: /remove criterion 1/i }));

    expect(latest.map((c) => c.value)).toEqual(['2']);
  });

  it('builds_the_api_body_with_numbers_and_only_the_scopes_that_are_set', () => {
    const body = toCriteriaRequest([
      { metric: 'TotalBalance', operator: 'GreaterThanOrEqual', value: '500000', accountType: 'Savings', merchantCategory: '' },
      { metric: 'Spend30d', operator: 'GreaterThan', value: '500.5', accountType: '', merchantCategory: ' Travel ' },
    ]);

    expect(body).toEqual([
      { metric: 'TotalBalance', operator: 'GreaterThanOrEqual', value: 500000, accountType: 'Savings' },
      { metric: 'Spend30d', operator: 'GreaterThan', value: 500.5, merchantCategory: 'Travel' },
    ]);
  });
});
