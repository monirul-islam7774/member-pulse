import { describe, expect, it } from 'vitest';
import type { Criterion } from './api/types';
import { describeCriterion } from './labels';

const c = (over: Partial<Criterion>): Criterion => ({
  metric: 'Age', operator: 'GreaterThan', value: 0, accountType: null, merchantCategory: null, ...over,
});

describe('describeCriterion', () => {
  it('writes_money_metrics_with_a_dollar_sign_and_the_account_scope', () => {
    expect(describeCriterion(c({ metric: 'TotalBalance', operator: 'GreaterThanOrEqual', value: 500000, accountType: 'Savings' })))
      .toBe('Total balance ≥ $500,000 in Savings');
  });

  it('writes_spending_with_its_merchant_category', () => {
    expect(describeCriterion(c({ metric: 'Spend30d', operator: 'GreaterThan', value: 500, merchantCategory: 'Travel' })))
      .toBe('Spending in the last 30 days > $500 in Travel');
  });

  it('writes_plain_counts_without_money_formatting', () => {
    expect(describeCriterion(c({ metric: 'Age', operator: 'LessThan', value: 30 }))).toBe('Age < 30');
    expect(describeCriterion(c({ metric: 'AccountCount', operator: 'Equal', value: 2 }))).toBe('Number of accounts = 2');
    expect(describeCriterion(c({ metric: 'DaysSinceJoined', operator: 'LessThanOrEqual', value: 90 }))).toBe('Days as a member ≤ 90');
    expect(describeCriterion(c({ metric: 'TransactionCount30d', operator: 'GreaterThanOrEqual', value: 5, merchantCategory: 'Dining' })))
      .toBe('Transactions in the last 30 days ≥ 5 in Dining');
  });

  it('keeps_cents_when_the_amount_has_them', () => {
    expect(describeCriterion(c({ metric: 'TotalBalance', operator: 'LessThan', value: 99.5 }))).toBe('Total balance < $99.50');
  });
});
