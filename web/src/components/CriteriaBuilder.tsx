import type { CriterionRequest } from '../api/segments';
import type { AccountType, ComparisonOperator, MemberMetric } from '../api/types';
import {
  accountScopedMetrics,
  accountTypeLabels,
  accountTypes,
  categoryScopedMetrics,
  metricLabels,
  metrics,
  operatorWords,
  operators,
} from '../labels';

/** A row being edited: text fields as typed, empty string meaning "not set". */
export interface CriterionDraft {
  metric: MemberMetric;
  operator: ComparisonOperator;
  value: string;
  accountType: AccountType | '';
  merchantCategory: string;
}

export const emptyCriterion = (): CriterionDraft => ({
  metric: 'TotalBalance',
  operator: 'GreaterThanOrEqual',
  value: '',
  accountType: '',
  merchantCategory: '',
});

/** The API body: numbers as numbers, and a scope only where one is set (and the metric accepts it). */
export function toCriteriaRequest(drafts: CriterionDraft[]): CriterionRequest[] {
  return drafts.map((d) => {
    const request: CriterionRequest = { metric: d.metric, operator: d.operator, value: Number(d.value) };
    if (d.accountType && accountScopedMetrics.includes(d.metric)) request.accountType = d.accountType;
    const category = d.merchantCategory.trim();
    if (category && categoryScopedMetrics.includes(d.metric)) request.merchantCategory = category;
    return request;
  });
}

interface Props {
  value: CriterionDraft[];
  onChange: (next: CriterionDraft[]) => void;
}

export function CriteriaBuilder({ value, onChange }: Props) {
  const update = (index: number, patch: Partial<CriterionDraft>) =>
    onChange(value.map((row, i) => (i === index ? { ...row, ...patch } : row)));

  // Changing the metric drops a scope the new metric would not accept (the API rejects it).
  const changeMetric = (index: number, metric: MemberMetric) =>
    update(index, {
      metric,
      accountType: accountScopedMetrics.includes(metric) ? value[index].accountType : '',
      merchantCategory: categoryScopedMetrics.includes(metric) ? value[index].merchantCategory : '',
    });

  return (
    <div className="stack">
      {value.map((row, i) => (
        <div key={i} role="group" aria-label={`Criterion ${i + 1}`} className="criterion">
          <label className="field">
            <span>Metric</span>
            <select value={row.metric} onChange={(e) => changeMetric(i, e.target.value as MemberMetric)}>
              {metrics.map((m) => (
                <option key={m} value={m}>
                  {metricLabels[m]}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>Operator</span>
            <select value={row.operator} onChange={(e) => update(i, { operator: e.target.value as ComparisonOperator })}>
              {operators.map((o) => (
                <option key={o} value={o}>
                  {operatorWords[o]}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>Value</span>
            <input type="number" step="any" required value={row.value} onChange={(e) => update(i, { value: e.target.value })} />
          </label>
          {accountScopedMetrics.includes(row.metric) && (
            <label className="field">
              <span>
                Account type <span className="hint">(optional)</span>
              </span>
              <select value={row.accountType} onChange={(e) => update(i, { accountType: e.target.value as AccountType | '' })}>
                <option value="">Any account</option>
                {accountTypes.map((t) => (
                  <option key={t} value={t}>
                    {accountTypeLabels[t]}
                  </option>
                ))}
              </select>
            </label>
          )}
          {categoryScopedMetrics.includes(row.metric) && (
            <label className="field">
              <span>
                Merchant category <span className="hint">(optional)</span>
              </span>
              <input maxLength={50} value={row.merchantCategory} onChange={(e) => update(i, { merchantCategory: e.target.value })} />
            </label>
          )}
          <button
            type="button"
            className="btn quiet small"
            aria-label={`Remove criterion ${i + 1}`}
            disabled={value.length === 1}
            onClick={() => onChange(value.filter((_, j) => j !== i))}
          >
            Remove
          </button>
        </div>
      ))}
      <div>
        <button type="button" className="btn small" onClick={() => onChange([...value, emptyCriterion()])}>
          Add criterion
        </button>
      </div>
    </div>
  );
}
