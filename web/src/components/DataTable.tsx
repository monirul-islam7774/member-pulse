import type { ReactNode } from 'react';

export interface Column<T> {
  header: string;
  cell: (row: T) => ReactNode;
  /** Right-aligned figures. */
  numeric?: boolean;
}

interface Props<T> {
  /** Names the table for assistive technology. */
  label: string;
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => string;
  /** Shown instead of the table when there are no rows. */
  empty: ReactNode;
}

/** Cell contents are React nodes, so strings from the API are always rendered as text, never as HTML. */
export function DataTable<T>({ label, columns, rows, rowKey, empty }: Props<T>) {
  if (rows.length === 0) return <>{empty}</>;

  return (
    <div className="table-wrap">
      <table className="table" aria-label={label}>
        <thead>
          <tr>
            {columns.map((c) => (
              <th key={c.header} className={c.numeric ? 'num' : undefined}>
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={rowKey(row)}>
              {columns.map((c) => (
                <td key={c.header} className={c.numeric ? 'num' : undefined}>
                  {c.cell(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
