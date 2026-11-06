import { apiClient } from '../../shared/api/client';
import type { CallInfo, CallJoin } from './callTypes';

export const callApi = {
  start(chatId: string, mediaType: 'Audio' | 'Video', signal?: AbortSignal) {
    return apiClient.post<CallInfo>(`/api/v1/chats/${chatId}/calls`, { mediaType }, signal);
  },

  list(chatId: string, limit = 20, signal?: AbortSignal) {
    return apiClient.get<CallInfo[]>(`/api/v1/chats/${chatId}/calls?limit=${limit}`, signal);
  },

  get(id: string, signal?: AbortSignal) {
    return apiClient.get<CallInfo>(`/api/v1/calls/${id}`, signal);
  },

  accept(id: string, signal?: AbortSignal) {
    return apiClient.post<CallInfo>(`/api/v1/calls/${id}/accept`, {}, signal);
  },

  reject(id: string, signal?: AbortSignal) {
    return apiClient.post<CallInfo>(`/api/v1/calls/${id}/reject`, {}, signal);
  },

  join(id: string, signal?: AbortSignal) {
    return apiClient.post<CallJoin>(`/api/v1/calls/${id}/join`, {}, signal);
  },

  leave(id: string, signal?: AbortSignal) {
    return apiClient.post<CallInfo>(`/api/v1/calls/${id}/leave`, {}, signal);
  },

  end(id: string, signal?: AbortSignal) {
    return apiClient.post<CallInfo>(`/api/v1/calls/${id}/end`, {}, signal);
  },
};
