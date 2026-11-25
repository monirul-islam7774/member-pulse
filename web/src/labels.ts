import type { AccountType, ComparisonOperator, Criterion, MemberMetric } from './api/types';
import { formatCurrency, formatNumber } from './format';

/** Words people use for the API's enum values. */
export const accountTypeLabels: Record<AccountType, string> = {
  Savings: 'Savings',
  Checking: 'Checking',
  CreditCard: 'Credit card',
  AutoLoan: 'Auto loan',
  Mortgage: 'Mortgage',
  HomeEquityLoan: 'Home equity loan',
};

export const accountTypes = Object.keys(accountTypeLabels) as AccountType[];

export const metricLabels: Record<MemberMetric, string> = {
  Age: 'Age',
  TotalBalance: 'Total balance',
  AccountCount: 'Number of accounts',
  TransactionCount30d: 'Transactions in the last 30 days',
  Spend30d: 'Spending in the last 30 days',
  DaysSinceJoined: 'Days as a member',
};

export const metrics = Object.keys(metricLabels) as MemberMetric[];

/** The API only accepts an account-type scope on these metrics... */
export const accountScopedMetrics: readonly MemberMetric[] = ['TotalBalance', 'AccountCount'];
/** ...and a merchant-category scope on these. */
export const categoryScopedMetrics: readonly MemberMetric[] = ['Spend30d', 'TransactionCount30d'];
const moneyMetrics: readonly MemberMetric[] = ['TotalBalance', 'Spend30d'];

export const operatorSymbols: Record<ComparisonOperator, string> = {
  GreaterThan: '>',
  GreaterThanOrEqual: '≥',
  LessThan: '<',
  LessThanOrEqual: '≤',
  Equal: '=',
};

export const operatorWords: Record<ComparisonOperator, string> = {
  GreaterThan: 'greater than',
  GreaterThanOrEqual: 'at least',
  LessThan: 'less than',
  LessThanOrEqual: 'at most',
  Equal: 'equal to',
};

export const operators = Object.keys(operatorWords) as ComparisonOperator[];

/** "Total balance ≥ $500,000 in Savings" */
export function describeCriterion(c: Criterion): string {
  const value = moneyMetrics.includes(c.metric)
    ? Number.isInteger(c.value)
      ? `$${formatNumber(c.value)}`
      : formatCurrency(c.value)
    : formatNumber(c.value);
  const scope = c.accountType ? ` in ${accountTypeLabels[c.accountType]}` : c.merchantCategory ? ` in ${c.merchantCategory}` : '';
  return `${metricLabels[c.metric]} ${operatorSymbols[c.operator]} ${value}${scope}`;
}
