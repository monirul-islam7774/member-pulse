import { api } from './client';
import type { CampaignDetailReport, CampaignReport, DailyOverview } from './types';

/** ISO 8601 instants. Omitted = the API's default (the last 30 days). */
export interface DateRange {
  from?: string;
  to?: string;
}

export const getOverview = (range: DateRange = {}) => api.get<CampaignReport>('/api/reports/campaigns', { ...range });

export const getDailyOverview = (range: DateRange = {}) =>
  api.get<DailyOverview>('/api/reports/campaigns/daily', { ...range });

export const getCampaignReport = (id: string, range: DateRange = {}) =>
  api.get<CampaignDetailReport>(`/api/reports/campaigns/${id}`, { ...range });
