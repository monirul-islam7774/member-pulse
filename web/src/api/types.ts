// TypeScript mirrors of the API's JSON contracts (src/MemberPulse.Api/Contracts/*.cs).
// The API sends camelCase, enums as strings, DateOnly as "YYYY-MM-DD" and DateTimeOffset as ISO 8601.

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

// --- auth ---------------------------------------------------------------------------------------------------------
export type StaffRole = 'Staff' | 'Admin';

export interface LoginResponse {
  accessToken: string;
  tokenType: string;
  expiresAt: string;
}

export interface StaffUser {
  id: string;
  email: string;
  role: StaffRole;
  createdAt: string;
}

export interface StaffUserSummary extends StaffUser {
  isLockedOut: boolean;
  lockedUntil: string | null;
}

// --- members ------------------------------------------------------------------------------------------------------
export type AccountType = 'Savings' | 'Checking' | 'CreditCard' | 'AutoLoan' | 'Mortgage' | 'HomeEquityLoan';

export interface Account {
  id: string;
  type: AccountType;
  balance: number;
  openedAt: string;
}

export interface MemberSummary {
  id: string;
  fullName: string;
  email: string;
  accountCount: number;
}

export interface MemberDetail {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  dateOfBirth: string;
  age: number;
  joinedAt: string;
  accounts: Account[];
}

export interface Transaction {
  id: string;
  accountId: string;
  amount: number;
  description: string;
  merchantCategory: string | null;
  occurredAt: string;
}

// --- segments -----------------------------------------------------------------------------------------------------
export type MemberMetric = 'Age' | 'TotalBalance' | 'AccountCount' | 'TransactionCount30d' | 'Spend30d' | 'DaysSinceJoined';
export type ComparisonOperator = 'GreaterThan' | 'GreaterThanOrEqual' | 'LessThan' | 'LessThanOrEqual' | 'Equal';

export interface Criterion {
  metric: MemberMetric;
  operator: ComparisonOperator;
  value: number;
  accountType: AccountType | null;
  merchantCategory: string | null;
}

export interface Segment {
  id: string;
  name: string;
  description: string | null;
  createdAt: string;
  criteria: Criterion[];
}

// --- campaigns ----------------------------------------------------------------------------------------------------
export type CampaignChannel = 'Email' | 'Sms' | 'Push';
export type TransactionDirection = 'Any' | 'Debit' | 'Credit';
export type CampaignStatus = 'Active' | 'Paused';
export type DeliveryStatus = 'Pending' | 'Sent' | 'Failed';

export interface ConversionGoal {
  windowDays: number;
  direction: TransactionDirection;
  minAmount: number | null;
  merchantCategory: string | null;
}

export interface Campaign {
  id: string;
  name: string;
  segmentId: string;
  channel: CampaignChannel;
  messageTemplate: string;
  direction: TransactionDirection;
  minAmount: number | null;
  merchantCategory: string | null;
  cooldownDays: number;
  conversion: ConversionGoal;
  status: CampaignStatus;
  createdAt: string;
}

export interface Delivery {
  id: string;
  campaignId: string;
  memberId: string;
  transactionId: string;
  channel: CampaignChannel;
  message: string;
  status: DeliveryStatus;
  attempts: number;
  error: string | null;
  createdAt: string;
  convertedAt: string | null;
  conversionTransactionId: string | null;
  conversionAmount: number | null;
}

// --- reports ------------------------------------------------------------------------------------------------------
export interface Kpi {
  sent: number;
  failed: number;
  pending: number;
  converted: number;
  /** converted / sent, 0..1 */
  conversionRate: number;
  membersReached: number;
  convertedAmount: number;
}

export interface CampaignKpi {
  campaignId: string;
  name: string;
  kpis: Kpi;
}

export interface CampaignReport {
  from: string;
  to: string;
  totals: Kpi;
  campaigns: CampaignKpi[];
}

export interface DailyKpi {
  date: string;
  sent: number;
  converted: number;
}

export interface CampaignDetailReport {
  from: string;
  to: string;
  campaign: CampaignKpi;
  daily: DailyKpi[];
}

export interface DailyOverview {
  from: string;
  to: string;
  daily: DailyKpi[];
}

// --- admin --------------------------------------------------------------------------------------------------------
export interface DeadLetter {
  id: string;
  topic: string;
  key: string;
  type: string;
  attempts: number;
  lastError: string | null;
  createdAt: string;
  deadLetteredAt: string;
}
