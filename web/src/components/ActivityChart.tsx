import { useMemo, useState } from 'react';
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import type { DailyKpi } from '../api/types';
import { formatNumber } from '../format';

interface Props {
  daily: DailyKpi[];
  /** The range being shown (ISO instants). Days without messages are drawn as zero, not skipped. */
  from: string;
  to: string;
  /** Names the table for assistive tech, e.g. "Messages per day". */
  label?: string;
}

interface Point {
  date: string;
  label: string;
  sent: number;
  converted: number;
}

const SERIES = [
  { key: 'sent', name: 'Sent', color: 'var(--series-sent)' },
  { key: 'converted', name: 'Converted', color: 'var(--series-converted)' },
] as const;

const dayLabel = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' });

/** One point per UTC day from `from` to `to`, with zeros where nothing was sent. */
export function fillDays(daily: DailyKpi[], from: string, to: string): Point[] {
  const byDate = new Map(daily.map((d) => [d.date, d]));
  const points: Point[] = [];
  const end = Date.parse(to);
  for (let t = Date.parse(from.slice(0, 10)); t <= end; t += 86_400_000) {
    const date = new Date(t).toISOString().slice(0, 10);
    const found = byDate.get(date);
    points.push({ date, label: dayLabel.format(new Date(t)), sent: found?.sent ?? 0, converted: found?.converted ?? 0 });
  }
  return points;
}

function ChartTooltip({ active, payload, label }: { active?: boolean; payload?: { dataKey: string; value: number }[]; label?: string }) {
  if (!active || !payload?.length) return null;
  return (
    <div className="chart-tooltip">
      <p className="chart-tooltip-title">{label}</p>
      {SERIES.map((s) => (
        <p key={s.key} className="chart-tooltip-row">
          <i style={{ background: s.color }} aria-hidden="true" />
          <strong>{formatNumber(payload.find((p) => p.dataKey === s.key)?.value ?? 0)}</strong>
          <span>{s.name}</span>
        </p>
      ))}
    </div>
  );
}

export function ActivityChart({ daily, from, to, label = 'Messages per day' }: Props) {
  const [asTable, setAsTable] = useState(false);
  const points = useMemo(() => fillDays(daily, from, to), [daily, from, to]);

  return (
    <div>
      <div className="chart-head">
        <ul className="legend" aria-label="Legend">
          {SERIES.map((s) => (
            <li key={s.key}>
              <i style={{ background: s.color }} aria-hidden="true" />
              {s.name}
            </li>
          ))}
        </ul>
        <button type="button" className="btn quiet small" onClick={() => setAsTable((v) => !v)}>
          {asTable ? 'View as chart' : 'View as table'}
        </button>
      </div>

      {asTable ? (
        <div className="table-wrap chart-table">
          <table className="table" aria-label={label}>
            <thead>
              <tr>
                <th>Day</th>
                <th className="num">Sent</th>
                <th className="num">Converted</th>
              </tr>
            </thead>
            <tbody>
              {[...points].reverse().map((p) => (
                <tr key={p.date}>
                  <td>{p.label}</td>
                  <td className="num">{formatNumber(p.sent)}</td>
                  <td className="num">{formatNumber(p.converted)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="chart" role="img" aria-label={`${label}: sent and converted. Use "View as table" for the exact figures.`}>
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={points} margin={{ top: 8, right: 12, bottom: 0, left: 0 }}>
              <CartesianGrid stroke="var(--line)" vertical={false} />
              <XAxis dataKey="label" tick={{ fill: 'var(--muted)', fontSize: 12 }} tickLine={false} axisLine={{ stroke: 'var(--line)' }} minTickGap={24} />
              <YAxis allowDecimals={false} tick={{ fill: 'var(--muted)', fontSize: 12 }} tickLine={false} axisLine={false} width={40} />
              <Tooltip content={<ChartTooltip />} cursor={{ stroke: 'var(--muted)', strokeDasharray: '3 3' }} />
              {SERIES.map((s) => (
                <Line
                  key={s.key}
                  type="monotone"
                  dataKey={s.key}
                  name={s.name}
                  stroke={s.color}
                  strokeWidth={2}
                  dot={false}
                  activeDot={{ r: 5, stroke: 'var(--surface)', strokeWidth: 2 }}
                  isAnimationActive={false}
                />
              ))}
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}
    </div>
  );
}
