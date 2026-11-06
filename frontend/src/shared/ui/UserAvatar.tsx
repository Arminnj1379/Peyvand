import { useAvatarUrl } from '../../features/auth/useAvatar';

function initials(name: string): string {
  const parts = name.split(' ').filter(Boolean);
  const first = parts[0]?.[0] ?? '?';
  const second = parts.length > 1 ? (parts[1]?.[0] ?? '') : '';
  return (first + second).toUpperCase();
}

/**
 * Avatar that shows the user's photo when available, initials otherwise.
 * Photos load through the authorized API (object URL); plain initials need
 * no network. `bust` forces a refetch after an avatar change.
 */
export function UserAvatar({
  avatarUrl,
  name,
  color,
  size = 48,
  online = false,
  bust = 0,
}: {
  avatarUrl: string | null | undefined;
  name: string;
  color: string;
  size?: number;
  online?: boolean;
  bust?: number;
}) {
  const photo = useAvatarUrl(avatarUrl ?? null, bust);
  return (
    <span
      className="avatar"
      style={{ background: color, width: size, height: size, fontSize: size * 0.36 }}
      aria-hidden="true"
    >
      {photo ? (
        <img className="avatar-photo" src={photo} alt="" draggable={false} />
      ) : (
        initials(name)
      )}
      {online ? <span className="presence-dot" /> : null}
    </span>
  );
}
