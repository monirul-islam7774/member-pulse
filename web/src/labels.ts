import type { AccountType } from './api/types';

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
