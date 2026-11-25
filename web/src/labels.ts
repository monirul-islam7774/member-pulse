import type {
  AccountType,
  CampaignChannel,
  ComparisonOperator,
  ConversionGoal,
  Criterion,
  MemberMetric,
  TransactionDirection,
} from './api/types';
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

export const channelLabels: Record<CampaignChannel, string> = { Email: 'Email', Sms: 'SMS', Push: 'Push notification' };
export const channels = Object.keys(channelLabels) as CampaignChannel[];

/** Option text for choosing which kind of transaction a rule looks at. */
export const directionLabels: Record<TransactionDirection, string> = {
  Debit: 'A purchase (money out)',
  Credit: 'A deposit (money in)',
  Any: 'Any transaction',
};
export const directions = Object.keys(directionLabels) as TransactionDirection[];

const directionNouns: Record<TransactionDirection, string> = { Debit: 'purchase', Credit: 'deposit', Any: 'transaction' };

function money(value: number): string {
  return Number.isInteger(value) ? `$${formatNumber(value)}` : formatCurrency(value);
}

/** "Any purchase", "A purchase of at least $100 in Dining" */
export function describeTrigger(f: { direction: TransactionDirection; minAmount: number | null; merchantCategory: string | null }): string {
  const noun = directionNouns[f.direction];
  if (f.minAmount === null && !f.merchantCategory) return `Any ${noun}`;
  return `A ${noun}${f.minAmount !== null ? ` of at least ${money(f.minAmount)}` : ''}${f.merchantCategory ? ` in ${f.merchantCategory}` : ''}`;
}

/** "A purchase in Travel within 14 days" */
export function describeConversion(goal: ConversionGoal): string {
  return `${describeTrigger(goal)} within ${goal.windowDays} ${goal.windowDays === 1 ? 'day' : 'days'}`;
}

export function describeCooldown(days: number): string {
  if (days <= 0) return 'No limit on how often a member is messaged';
  return days === 1 ? 'At most one message a day per member' : `At most one message every ${days} days per member`;
}

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
