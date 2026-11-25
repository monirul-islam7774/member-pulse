import { api } from './client';
import type { AccountType, ComparisonOperator, MemberMetric, MemberSummary, Paged, Segment } from './types';

export interface CriterionRequest {
  metric: MemberMetric;
  operator: ComparisonOperator;
  value: number;
  accountType?: AccountType;
  merchantCategory?: string;
}

export const listSegments = () => api.get<Segment[]>('/api/segments');

export const createSegment = (body: { name: string; description?: string; criteria: CriterionRequest[] }) =>
  api.post<Segment>('/api/segments', body);

export const listSegmentMembers = (id: string, page: number, pageSize: number) =>
  api.get<Paged<MemberSummary>>(`/api/segments/${id}/members`, { page, pageSize });
