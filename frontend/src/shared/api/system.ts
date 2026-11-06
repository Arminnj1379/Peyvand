import { apiClient } from './client';

export interface SystemInfo {
  service: string;
  version: string;
  serverTimeUtc: string;
  locale: string | null;
}

/** Real backend call — backs the connectivity pill in the sidebar. */
export function fetchSystemInfo(signal?: AbortSignal): Promise<SystemInfo> {
  return apiClient.get<SystemInfo>('/api/v1/system/info', signal);
}
