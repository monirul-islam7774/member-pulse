import { api } from './client';
import type { Account, AccountType, MemberDetail, MemberSummary, Paged, Segment, Transaction } from './types';

export const listMembers = (page: number, pageSize: number) =>
  api.get<Paged<MemberSummary>>('/api/members', { page, pageSize });

export const getMember = (id: string) => api.get<MemberDetail>(`/api/members/${id}`);

export const getMemberSegments = (id: string) => api.get<Segment[]>(`/api/members/${id}/segments`);

export const createMember = (body: { firstName: string; lastName: string; email: string; dateOfBirth: string }) =>
  api.post<MemberDetail>('/api/members', body);

export const openAccount = (memberId: string, body: { type: AccountType; initialDeposit: number }) =>
  api.post<Account>(`/api/members/${memberId}/accounts`, body);

export const listTransactions = (accountId: string, page: number, pageSize: number) =>
  api.get<Paged<Transaction>>(`/api/accounts/${accountId}/transactions`, { page, pageSize });

/** Negative amount = purchase or withdrawal, positive = deposit. */
export const postTransaction = (accountId: string, body: { amount: number; description: string; merchantCategory?: string }) =>
  api.post<Transaction>(`/api/accounts/${accountId}/transactions`, body);
