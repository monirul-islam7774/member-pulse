import { api } from './client';
import type { Campaign, CampaignChannel, Delivery, Paged, TransactionDirection } from './types';

/** Field names exactly as the API's CreateCampaignRequest. Optional fields are left out when unset. */
export interface CreateCampaignBody {
  name: string;
  segmentId: string;
  channel: CampaignChannel;
  messageTemplate: string;
  direction: TransactionDirection;
  minAmount?: number;
  merchantCategory?: string;
  cooldownDays: number;
  conversionWindowDays: number;
  conversionDirection: TransactionDirection;
  conversionMinAmount?: number;
  conversionMerchantCategory?: string;
}

export const listCampaigns = () => api.get<Campaign[]>('/api/campaigns');

export const getCampaign = (id: string) => api.get<Campaign>(`/api/campaigns/${id}`);

export const createCampaign = (body: CreateCampaignBody) => api.post<Campaign>('/api/campaigns', body);

export const pauseCampaign = (id: string) => api.post<Campaign>(`/api/campaigns/${id}/pause`);

export const resumeCampaign = (id: string) => api.post<Campaign>(`/api/campaigns/${id}/resume`);

export const listDeliveries = (id: string, page: number, pageSize: number) =>
  api.get<Paged<Delivery>>(`/api/campaigns/${id}/deliveries`, { page, pageSize });
