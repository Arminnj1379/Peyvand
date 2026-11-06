import { apiClient } from '../../shared/api/client';
import type { HistoryPage, Message } from './messageTypes';

export const messageApi = {
  send(
    chatId: string,
    input: { body: string; clientMessageId: string; replyToMessageId?: string; fileId?: string },
    signal?: AbortSignal,
  ) {
    return apiClient.post<Message>(`/api/v1/chats/${chatId}/messages`, input, signal);
  },

  history(chatId: string, beforeSeq?: number, limit = 30, signal?: AbortSignal) {
    const q = new URLSearchParams();
    if (beforeSeq !== undefined) q.set('beforeSeq', String(beforeSeq));
    q.set('limit', String(limit));
    return apiClient.get<HistoryPage>(`/api/v1/chats/${chatId}/messages?${q.toString()}`, signal);
  },

  sync(chatId: string, afterSeq: number, limit = 100, signal?: AbortSignal) {
    return apiClient.get<Message[]>(
      `/api/v1/chats/${chatId}/messages/sync?afterSeq=${afterSeq}&limit=${limit}`,
      signal,
    );
  },

  edit(id: string, body: string, signal?: AbortSignal) {
    return apiClient.patch<Message>(`/api/v1/messages/${id}`, { body }, signal);
  },

  remove(id: string, signal?: AbortSignal) {
    return apiClient.delete<Message>(`/api/v1/messages/${id}`, signal);
  },

  bulkDelete(ids: string[], signal?: AbortSignal) {
    return apiClient.post<Message[]>('/api/v1/messages/bulk-delete', { messageIds: ids }, signal);
  },

  toggleReaction(id: string, emoji: string, signal?: AbortSignal) {
    return apiClient.post<Message>(`/api/v1/messages/${id}/reactions`, { emoji }, signal);
  },

  replies(id: string, limit = 30, signal?: AbortSignal) {
    return apiClient.get<Message[]>(`/api/v1/messages/${id}/replies?limit=${limit}`, signal);
  },

  forward(
    input: { sourceChatId: string; sourceMessageId: string; targetChatId: string; clientMessageId: string },
    signal?: AbortSignal,
  ) {
    return apiClient.post<Message>('/api/v1/messages/forward', input, signal);
  },
};
