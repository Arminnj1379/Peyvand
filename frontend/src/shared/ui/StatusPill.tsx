import { useQuery } from '@tanstack/react-query';
import { fetchSystemInfo } from '../api/system';

/** Backend connectivity indicator (API wiring proof on every screen). */
export function StatusPill() {
  const { data, isError, isPending } = useQuery({
    queryKey: ['system', 'info'],
    queryFn: ({ signal }) => fetchSystemInfo(signal),
    refetchInterval: 30_000,
  });

  if (isPending) {
    return (
      <span className="status-pill">
        <span className="status-dot" /> connecting…
      </span>
    );
  }
  if (isError || !data) {
    return (
      <span className="status-pill" title="Backend unreachable. Start Messenger.Api (see README).">
        <span className="status-dot degraded" /> backend offline
      </span>
    );
  }
  return (
    <span className="status-pill" title={`Server time ${data.serverTimeUtc}`}>
      <span className="status-dot online" /> v{data.version}
    </span>
  );
}
