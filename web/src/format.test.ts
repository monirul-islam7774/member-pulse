import { describe, expect, it } from 'vitest';
import { formatCurrency, formatNumber, formatPercent } from './format';

describe('format', () => {
  it('formats_money_as_usd_with_cents', () => {
    expect(formatCurrency(640.25)).toBe('$640.25');
    expect(formatCurrency(-45.5)).toBe('-$45.50');
    expect(formatCurrency(1234567.5)).toBe('$1,234,567.50');
  });

  it('formats_a_rate_as_a_percentage', () => {
    expect(formatPercent(0.125)).toBe('12.5%');
    expect(formatPercent(1)).toBe('100.0%');
  });

  it('never_shows_NaN_or_Infinity_for_a_rate', () => {
    expect(formatPercent(0)).toBe('0.0%');
    expect(formatPercent(Number.NaN)).toBe('0.0%');
    expect(formatPercent(Number.POSITIVE_INFINITY)).toBe('0.0%');
  });

  it('groups_thousands_in_counts', () => {
    expect(formatNumber(12345)).toBe('12,345');
    expect(formatNumber(0)).toBe('0');
  });
});
