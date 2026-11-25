import { formatNumber } from '../format';

interface Props {
  page: number;
  pageSize: number;
  totalCount: number;
  onChange: (page: number) => void;
  /** Plural noun for the total, e.g. "members". */
  noun?: string;
}

export function Pager({ page, pageSize, totalCount, onChange, noun = 'items' }: Props) {
  const pages = Math.max(1, Math.ceil(totalCount / pageSize));
  return (
    <nav className="pager" aria-label="Pagination">
      <span>
        Page {page} of {pages} ({formatNumber(totalCount)} {noun})
      </span>
      <span>
        <button type="button" className="btn small" disabled={page <= 1} onClick={() => onChange(page - 1)}>
          Previous
        </button>{' '}
        <button type="button" className="btn small" disabled={page >= pages} onClick={() => onChange(page + 1)}>
          Next
        </button>
      </span>
    </nav>
  );
}
