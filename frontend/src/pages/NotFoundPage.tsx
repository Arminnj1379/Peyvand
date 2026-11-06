import { Link } from 'react-router-dom';

export function NotFoundPage() {
  return (
    <div className="center-page">
      <div className="card">
        <h1>Page not found</h1>
        <p>The page you asked for does not exist.</p>
        <Link to="/">Back to Messenger</Link>
      </div>
    </div>
  );
}
