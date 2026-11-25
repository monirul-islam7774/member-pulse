import { describe, expect, it } from 'vitest';
import type { Criterion } from './api/types';
import type { ConversionGoal } from './api/types';
import { describeConversion, describeCooldown, describeCriterion, describeTrigger } from './labels';

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

describe('campaign wording', () => {
  it('describes_what_triggers_a_message', () => {
    expect(describeTrigger({ direction: 'Any', minAmount: null, merchantCategory: null })).toBe('Any transaction');
    expect(describeTrigger({ direction: 'Debit', minAmount: null, merchantCategory: null })).toBe('Any purchase');
    expect(describeTrigger({ direction: 'Debit', minAmount: 100, merchantCategory: 'Dining' })).toBe('A purchase of at least $100 in Dining');
    expect(describeTrigger({ direction: 'Credit', minAmount: 2500.5, merchantCategory: null })).toBe('A deposit of at least $2,500.50');
  });

  it('describes_the_conversion_goal_with_its_window', () => {
    const goal = (over: Partial<ConversionGoal>): ConversionGoal => ({ windowDays: 7, direction: 'Debit', minAmount: null, merchantCategory: null, ...over });

    expect(describeConversion(goal({}))).toBe('Any purchase within 7 days');
    expect(describeConversion(goal({ windowDays: 1 }))).toBe('Any purchase within 1 day');
    expect(describeConversion(goal({ windowDays: 14, minAmount: 250, merchantCategory: 'Travel' })))
      .toBe('A purchase of at least $250 in Travel within 14 days');
  });

  it('describes_the_cooldown', () => {
    expect(describeCooldown(0)).toBe('No limit on how often a member is messaged');
    expect(describeCooldown(1)).toBe('At most one message a day per member');
    expect(describeCooldown(30)).toBe('At most one message every 30 days per member');
  });
});
