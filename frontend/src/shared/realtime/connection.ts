import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import type { HubConnection } from '@microsoft/signalr';
import { apiBaseUrl } from '../api/client';

export const HUB_URL = `${apiBaseUrl}/hubs/messenger`;

export function createHubConnection(getToken: () => string | null): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(HUB_URL, { accessTokenFactory: () => getToken() ?? '' })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}
