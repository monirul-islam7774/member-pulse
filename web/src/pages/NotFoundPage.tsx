import { Link } from 'react-router-dom';

export function NotFoundPage() {
  return (
    <>
      <h1 className="page-title">Page not found</h1>
      <p className="muted">
        There is nothing at this address. <Link to="/">Go to the dashboard</Link>.
      </p>
    </>
  );
}
