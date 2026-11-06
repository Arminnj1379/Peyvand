import type { Attachment } from '../files/fileTypes';

export interface ReplyPreview {
  messageId: string;
  senderName: string | null;
  preview: string;
  isDeleted: boolean;
}

export interface ForwardInfo {
  chatId: string;
  messageId: string;
  senderId: string;
  sentAt: string;
}

export interface Reaction {
  emoji: string;
  count: number;
  mine: boolean;
}

export interface Message {
  id: string;
  chatId: string;
  seq: number;
  clientMessageId: string;
  senderId: string | null;
  senderUsername: string | null;
  senderName: string | null;
  kind: string;
  body: string;
  replyTo: ReplyPreview | null;
  forwardFrom: ForwardInfo | null;
  isEdited: boolean;
  editedAt: string | null;
  isDeleted: boolean;
  createdAt: string;
  reactions: Reaction[];
  replyCount: number;
  attachment: Attachment | null;
  /** Client-only: optimistic send in flight. Never comes from the server. */
  sending?: boolean;
  /** Client-only: send failed, offers retry. Never comes from the server. */
  failed?: boolean;
}

export interface HistoryPage {
  items: Message[];
  nextCursor: number | null;
  hasMore: boolean;
}

export function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

export function dayKey(iso: string): string {
  const d = new Date(iso);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Compact timestamp for conversation list: time today, weekday/date otherwise. */
export function formatConversationTime(iso: string): string {
  const d = new Date(iso);
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const target = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const diffDays = Math.round((today.getTime() - target.getTime()) / 86_400_000);
  if (diffDays === 0) {
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }
  if (diffDays === 1) return 'Yesterday';
  if (diffDays > 1 && diffDays < 7) {
    return d.toLocaleDateString([], { weekday: 'short' });
  }
  if (d.getFullYear() === now.getFullYear()) {
    return d.toLocaleDateString([], { month: 'short', day: 'numeric' });
  }
  return d.toLocaleDateString([], { year: '2-digit', month: 'short', day: 'numeric' });
}

/** Human-friendly label for a day divider: Today / Yesterday / weekday / full date. */
export function formatDayLabel(iso: string): string {
  const d = new Date(iso);
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const target = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const diffDays = Math.round((today.getTime() - target.getTime()) / 86_400_000);
  if (diffDays === 0) return 'Today';
  if (diffDays === 1) return 'Yesterday';
  if (diffDays > 1 && diffDays < 7) {
    return d.toLocaleDateString([], { weekday: 'long' });
  }
  // Same year: drop the year to keep the pill compact.
  if (d.getFullYear() === now.getFullYear()) {
    return d.toLocaleDateString([], { month: 'long', day: 'numeric' });
  }
  return d.toLocaleDateString([], { year: 'numeric', month: 'short', day: 'numeric' });
}

/** Relative last-seen label ("online" handled by callers via presence). */
export function formatLastSeen(iso: string | null): string {
  if (!iso) return 'offline';
  const diff = Date.now() - new Date(iso).getTime();
  if (diff < 60_000) return 'last seen just now';
  if (diff < 3_600_000) return `last seen ${Math.floor(diff / 60_000)} min ago`;
  if (diff < 86_400_000) return `last seen ${Math.floor(diff / 3_600_000)} h ago`;
  return `last seen ${new Date(iso).toLocaleDateString([], { year: 'numeric', month: 'short', day: 'numeric' })}`;
}

/** First http(s) URL in a message body, for client-side link previews. No server fetch. */
export function firstUrl(body: string): string | null {
  const m = /\bhttps?:\/\/[^\s<>"']+/i.exec(body);
  return m ? m[0].replace(/[.,;:!?)]+$/, '') : null;
}

export function linkDomain(url: string): string {
  try {
    return new URL(url).hostname.replace(/^www\./, '');
  } catch {
    return url;
  }
}
