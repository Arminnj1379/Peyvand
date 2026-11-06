import { apiClient } from '../../shared/api/client';

export interface Ticket {
  id: string;
  customerId: string;
  customerName: string | null;
  subject: string;
  description: string;
  status: 'Open' | 'Assigned' | 'InProgress' | 'Resolved' | 'Closed';
  assigneeId: string | null;
  assigneeName: string | null;
  chatId: string | null;
  createdAt: string;
  updatedAt: string;
  closedAt: string | null;
}

export const supportApi = {
  open(subject: string, description: string, signal?: AbortSignal) {
    return apiClient.post<Ticket>('/api/v1/support/tickets', { subject, description }, signal);
  },

  list(
    input: { status?: string; assigneeId?: string; mine?: boolean; limit?: number },
    signal?: AbortSignal,
  ) {
    const q = new URLSearchParams();
    if (input.status) q.set('status', input.status);
    if (input.assigneeId) q.set('assigneeId', input.assigneeId);
    if (input.mine) q.set('mine', 'true');
    q.set('limit', String(input.limit ?? 20));
    return apiClient.get<Ticket[]>(`/api/v1/support/tickets?${q.toString()}`, signal);
  },

  assign(id: string, agentId?: string, signal?: AbortSignal) {
    return apiClient.post<Ticket>(
      `/api/v1/support/tickets/${id}/assign`,
      agentId ? { agentId } : {},
      signal,
    );
  },

  setStatus(id: string, status: string, signal?: AbortSignal) {
    return apiClient.post<Ticket>(`/api/v1/support/tickets/${id}/status`, { status }, signal);
  },
};
