import { apiClient } from '../../shared/api/client';

export interface NotificationItem {
  chatId: string;
  chatType: string;
  title: string;
  preview: string | null;
  unreadCount: number;
  isMuted: boolean;
  at: string | null;
}

export const notificationApi = {
  list(limit = 20, signal?: AbortSignal) {
    return apiClient.get<NotificationItem[]>(`/api/v1/notifications?limit=${limit}`, signal);
  },
};
