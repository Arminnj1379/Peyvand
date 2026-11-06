function initials(name: string): string {
  const parts = name.split(' ').filter(Boolean);
  const first = parts[0]?.[0] ?? '?';
  const second = parts.length > 1 ? (parts[1]?.[0] ?? '') : '';
  return (first + second).toUpperCase();
}

export function Avatar({
  name,
  color,
  size = 48,
  online = false,
}: {
  name: string;
  color: string;
  size?: number;
  online?: boolean;
}) {
  return (
    <span
      className="avatar"
      style={{ background: color, width: size, height: size, fontSize: size * 0.36 }}
      aria-hidden="true"
    >
      {initials(name)}
      {online ? <span className="presence-dot" /> : null}
    </span>
  );
}
