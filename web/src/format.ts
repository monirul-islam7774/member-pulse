// The one place that decides how numbers, money and dates look.
// Money is US dollars and numbers use US grouping so the figures read the same for every viewer;
// dates follow the viewer's own locale and time zone.

const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });
const count = new Intl.NumberFormat('en-US');

export function formatCurrency(amount: number): string {
  return money.format(amount);
}

export function formatNumber(n: number): string {
  return count.format(n);
}

/** A 0..1 rate as "12.5%". Anything that is not a finite number (0 sent -> NaN) reads as "0.0%". */
export function formatPercent(rate: number): string {
  return `${(Number.isFinite(rate) ? rate * 100 : 0).toFixed(1)}%`;
}

export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
}

export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { dateStyle: 'medium' });
}
