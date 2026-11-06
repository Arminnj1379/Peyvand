import { apiClient } from '../../shared/api/client';
import type { Chat } from '../chats/chatTypes';
import type { Message } from '../messages/messageTypes';

export interface DirectoryUser {
  id: string;
  username: string;
  displayName: string;
}

export const searchApi = {
  messages(query: string, limit = 20, signal?: AbortSignal) {
    return apiClient.get<Message[]>(
      `/api/v1/messages/search?q=${encodeURIComponent(query)}&limit=${limit}`,
      signal,
    );
  },

  chats(query: string, limit = 20, signal?: AbortSignal) {
    return apiClient.get<Chat[]>(
      `/api/v1/chats/search?q=${encodeURIComponent(query)}&limit=${limit}`,
      signal,
    );
  },

  people(query: string, limit = 10, signal?: AbortSignal) {
    return apiClient.get<DirectoryUser[]>(
      `/api/v1/users/search?q=${encodeURIComponent(query)}&limit=${limit}`,
      signal,
    );
  },
};
