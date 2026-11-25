import { api } from './client';
import type { DeadLetter, StaffRole, StaffUser, StaffUserSummary } from './types';

export const listUsers = () => api.get<StaffUserSummary[]>('/api/users');

export const createUser = (body: { email: string; password: string; role: StaffRole }) => api.post<StaffUser>('/api/users', body);

export const unlockUser = (email: string) => api.post<void>('/api/users/unlock', { email });

export const listDeadLetters = () => api.get<DeadLetter[]>('/api/admin/outbox/dead');

export const retryDeadLetter = (id: string) => api.post<void>(`/api/admin/outbox/${id}/retry`);
