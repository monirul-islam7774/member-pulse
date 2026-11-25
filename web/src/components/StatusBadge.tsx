import type { CampaignStatus, DeliveryStatus } from '../api/types';

const tone: Record<CampaignStatus | DeliveryStatus, 'good' | 'warn' | 'bad'> = {
  Active: 'good',
  Paused: 'warn',
  Sent: 'good',
  Pending: 'warn',
  Failed: 'bad',
};

/** Status is always written out; colour only reinforces it. */
export function StatusBadge({ status }: { status: CampaignStatus | DeliveryStatus }) {
  return <span className={`badge ${tone[status]}`}>{status}</span>;
}
