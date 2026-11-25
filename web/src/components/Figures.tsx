import type { Kpi } from '../api/types';
import { formatCurrency, formatNumber } from '../format';

/** The headline numbers as a flat labelled row. Pending and failed only appear when there is something to look at. */
export function Figures({ totals }: { totals: Kpi }) {
  const items: [string, string][] = [
    ['Messages sent', formatNumber(totals.sent)],
    ['Converted', formatNumber(totals.converted)],
    ['Members reached', formatNumber(totals.membersReached)],
    ['Converted amount', formatCurrency(totals.convertedAmount)],
  ];
  if (totals.pending > 0) items.push(['Pending', formatNumber(totals.pending)]);
  if (totals.failed > 0) items.push(['Failed', formatNumber(totals.failed)]);

  return (
    <ul className="figures" aria-label="Totals">
      {items.map(([label, value]) => (
        <li key={label}>
          <span className="figure-label">{label}</span>
          <span className="figure-value">{value}</span>
        </li>
      ))}
    </ul>
  );
}
