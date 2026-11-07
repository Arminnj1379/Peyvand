import { useQuery } from '@tanstack/react-query';
import { fetchSystemInfo } from '../api/system';
import { useT } from '../i18n';

/** Backend connectivity indicator (API wiring proof on every screen). */
export function StatusPill() {
  const t = useT();
  const { data, isError, isPending } = useQuery({
    queryKey: ['system', 'info'],
    queryFn: ({ signal }) => fetchSystemInfo(signal),
    refetchInterval: 30_000,
  });

  if (isPending) {
    return (
      <span className="status-pill">
        <span className="status-dot" /> {t('status.connecting')}
      </span>
    );
  }
  if (isError || !data) {
    return (
      <span className="status-pill" title={t('status.offlineTitle')}>
        <span className="status-dot degraded" /> {t('status.offline')}
      </span>
    );
  }
  return (
    <span className="status-pill" title={t('status.versionTitle', { time: data.serverTimeUtc })}>
      <span className="status-dot online" /> v{data.version}
    </span>
  );
}
