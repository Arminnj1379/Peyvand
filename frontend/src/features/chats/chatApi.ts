import { apiClient } from '../../shared/api/client';
import type { Chat, PublicChannel } from './chatTypes';
import type { Message } from '../messages/messageTypes';

export const chatApi = {
  list(limit = 50, signal?: AbortSignal) {
    return apiClient.get<Chat[]>(`/api/v1/chats?limit=${limit}`, signal);
  },

  get(id: string, signal?: AbortSignal) {
    return apiClient.get<Chat>(`/api/v1/chats/${id}`, signal);
  },

  createDm(otherUserId: string, signal?: AbortSignal) {
    return apiClient.post<Chat>('/api/v1/chats/dm', { otherUserId }, signal);
  },

  createGroup(input: { title: string; memberIds: string[]; description?: string }, signal?: AbortSignal) {
    return apiClient.post<Chat>('/api/v1/chats/groups', input, signal);
  },

  createChannel(
    input: { title: string; description?: string; subscriberIds?: string[]; isPublic?: boolean; username?: string },
    signal?: AbortSignal,
  ) {
    return apiClient.post<Chat>('/api/v1/chats/channels', input, signal);
  },

  updateProfile(
    chatId: string,
    input: { title?: string; description?: string; membersCanInvite?: boolean },
    signal?: AbortSignal,
  ) {
    return apiClient.patch<Chat>(`/api/v1/chats/${chatId}`, input, signal);
  },

  setVisibility(chatId: string, input: { isPublic: boolean; username?: string }, signal?: AbortSignal) {
    return apiClient.put<Chat>(`/api/v1/chats/${chatId}/visibility`, input, signal);
  },

  addMember(chatId: string, userId: string, signal?: AbortSignal) {
    return apiClient.post<Chat>(`/api/v1/chats/${chatId}/members`, { userId }, signal);
  },

  removeMember(chatId: string, userId: string, signal?: AbortSignal) {
    return apiClient.delete<Chat>(`/api/v1/chats/${chatId}/members/${userId}`, signal);
  },

  leave(chatId: string, signal?: AbortSignal) {
    return apiClient.post<void>(`/api/v1/chats/${chatId}/leave`, {}, signal);
  },

  deleteChat(chatId: string, signal?: AbortSignal) {
    return apiClient.delete<Chat>(`/api/v1/chats/${chatId}`, signal);
  },

  setRole(chatId: string, userId: string, role: string, signal?: AbortSignal) {
    return apiClient.put<Chat>(`/api/v1/chats/${chatId}/members/${userId}/role`, { role }, signal);
  },

  invite(chatId: string, signal?: AbortSignal) {
    return apiClient.get<string | null>(`/api/v1/chats/${chatId}/invite`, signal);
  },

  rotateInvite(chatId: string, signal?: AbortSignal) {
    return apiClient.post<string>(`/api/v1/chats/${chatId}/invite/rotate`, {}, signal);
  },

  revokeInvite(chatId: string, signal?: AbortSignal) {
    return apiClient.delete<void>(`/api/v1/chats/${chatId}/invite`, signal);
  },

  join(code: string, signal?: AbortSignal) {
    return apiClient.post<Chat>('/api/v1/chats/join', { code }, signal);
  },

  subscribe(chatId: string, signal?: AbortSignal) {
    return apiClient.post<Chat>(`/api/v1/chats/${chatId}/subscribe`, {}, signal);
  },

  unsubscribe(chatId: string, signal?: AbortSignal) {
    return apiClient.post<void>(`/api/v1/chats/${chatId}/unsubscribe`, {}, signal);
  },

  publicChannel(username: string, signal?: AbortSignal) {
    return apiClient.get<PublicChannel>(
      `/api/v1/chats/public/${encodeURIComponent(username)}`,
      signal,
    );
  },

  setMuted(chatId: string, value: boolean, signal?: AbortSignal) {
    return apiClient.post<Chat>(`/api/v1/chats/${chatId}/mute`, { value }, signal);
  },

  setArchived(chatId: string, value: boolean, signal?: AbortSignal) {
    return apiClient.post<Chat>(`/api/v1/chats/${chatId}/archive`, { value }, signal);
  },

  markUnread(chatId: string, seq: number, signal?: AbortSignal) {
    return apiClient.post<Chat>(`/api/v1/chats/${chatId}/unread`, { seq }, signal);
  },

  pin(chatId: string, messageId: string, signal?: AbortSignal) {
    return apiClient.post<Chat>(`/api/v1/chats/${chatId}/pins`, { messageId }, signal);
  },

  unpin(chatId: string, messageId: string, signal?: AbortSignal) {
    return apiClient.delete<Chat>(`/api/v1/chats/${chatId}/pins/${messageId}`, signal);
  },

  pins(chatId: string, signal?: AbortSignal) {
    return apiClient.get<Message[]>(`/api/v1/chats/${chatId}/pins`, signal);
  },

  markRead(chatId: string, seq: number, signal?: AbortSignal) {
    return apiClient.post<Chat>(`/api/v1/chats/${chatId}/read`, { seq }, signal);
  },

  uploadAvatar(chatId: string, file: File, signal?: AbortSignal) {
    const form = new FormData();
    form.append('file', file);
    return apiClient.upload<Chat>(`/api/v1/chats/${chatId}/avatar`, form, signal);
  },

  removeAvatar(chatId: string, signal?: AbortSignal) {
    return apiClient.delete<Chat>(`/api/v1/chats/${chatId}/avatar`, signal);
  },
};
