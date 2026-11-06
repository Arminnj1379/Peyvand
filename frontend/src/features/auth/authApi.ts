import { apiClient } from '../../shared/api/client';
import type { AuthResult, Session, User } from './authTypes';

const DEVICE_LABEL = 'web';

export const authApi = {
  register(input: { username: string; displayName: string; password: string; phone?: string }, signal?: AbortSignal) {
    return apiClient.post<AuthResult>(
      '/api/v1/auth/register',
      { ...input, phone: input.phone || undefined, deviceLabel: DEVICE_LABEL },
      signal,
    );
  },

  login(login: string, password: string, signal?: AbortSignal) {
    return apiClient.post<AuthResult>(
      '/api/v1/auth/login',
      { login, password, deviceLabel: DEVICE_LABEL },
      signal,
    );
  },

  refresh(refreshToken: string, signal?: AbortSignal) {
    return apiClient.post<AuthResult>('/api/v1/auth/refresh', { refreshToken }, signal);
  },

  logout(signal?: AbortSignal) {
    return apiClient.post<void>('/api/v1/auth/logout', {}, signal);
  },

  logoutAll(signal?: AbortSignal) {
    return apiClient.post<void>('/api/v1/auth/logout-all', {}, signal);
  },

  sessions(signal?: AbortSignal) {
    return apiClient.get<Session[]>('/api/v1/auth/sessions', signal);
  },

  revokeSession(id: string, signal?: AbortSignal) {
    return apiClient.delete<void>(`/api/v1/auth/sessions/${id}`, signal);
  },

  changePassword(currentPassword: string, newPassword: string, signal?: AbortSignal) {
    return apiClient.post<void>('/api/v1/auth/change-password', { currentPassword, newPassword }, signal);
  },

  me(signal?: AbortSignal) {
    return apiClient.get<User>('/api/v1/users/me', signal);
  },

  user(id: string, signal?: AbortSignal) {
    return apiClient.get<User>(`/api/v1/users/${id}`, signal);
  },

  updateMe(displayName: string, bio: string, signal?: AbortSignal) {
    return apiClient.patch<User>('/api/v1/users/me', { displayName, bio }, signal);
  },

  uploadAvatar(file: File, signal?: AbortSignal) {
    const form = new FormData();
    form.append('file', file, file.name);
    return apiClient.upload<User>('/api/v1/users/me/avatar', form, signal);
  },

  removeAvatar(signal?: AbortSignal) {
    return apiClient.delete<User>('/api/v1/users/me/avatar', signal);
  },

  avatarBlob(url: string, signal?: AbortSignal) {
    return apiClient.blob(url, signal);
  },
};

export const adminApi = {
  list(params: { userType?: string; role?: string; isActive?: boolean; limit?: number } = {}, signal?: AbortSignal) {
    const q = new URLSearchParams();
    if (params.userType) q.set('userType', params.userType);
    if (params.role) q.set('role', params.role);
    if (params.isActive !== undefined) q.set('isActive', String(params.isActive));
    q.set('limit', String(params.limit ?? 50));
    return apiClient.get<User[]>(`/api/v1/admin/users?${q.toString()}`, signal);
  },

  createEmployee(
    input: { username: string; displayName: string; password: string; roles: string[]; phone?: string },
    signal?: AbortSignal,
  ) {
    return apiClient.post<User>(
      '/api/v1/admin/users',
      { ...input, phone: input.phone || undefined },
      signal,
    );
  },

  setActive(id: string, isActive: boolean, signal?: AbortSignal) {
    return apiClient.patch<User>(`/api/v1/admin/users/${id}/active`, { isActive }, signal);
  },

  setRoles(id: string, roles: string[], signal?: AbortSignal) {
    return apiClient.put<User>(`/api/v1/admin/users/${id}/roles`, { roles }, signal);
  },
};
