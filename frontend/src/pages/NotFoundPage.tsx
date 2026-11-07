import { Link } from 'react-router-dom';
import { useT } from '../shared/i18n';

export function NotFoundPage() {
  const t = useT();
  return (
    <div className="center-page">
      <div className="card">
        <h1>{t('notfound.title')}</h1>
        <p>{t('notfound.body')}</p>
        <Link to="/">{t('notfound.back')}</Link>
      </div>
    </div>
  );
}
