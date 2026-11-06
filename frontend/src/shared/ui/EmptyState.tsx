import { Icon } from './Icon';
import type { IconName } from './Icon';

export function EmptyState({ title, hint, icon }: { title: string; hint: string; icon?: IconName }) {
  return (
    <div className="empty-state">
      {icon ? (
        <span className="empty-icon" aria-hidden="true">
          <Icon name={icon} size={26} />
        </span>
      ) : null}
      <h2>{title}</h2>
      <p>{hint}</p>
    </div>
  );
}
