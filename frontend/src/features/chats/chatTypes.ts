export type ChatType = 'Dm' | 'Group' | 'Channel';

export interface ChatMember {
  userId: string;
  username: string;
  displayName: string;
  role: string;
  joinedAt: string;
  lastReadSeq: number;
  unreadCount: number;
  isMuted: boolean;
  lastDeliveredSeq: number;
}

export interface Chat {
  id: string;
  type: ChatType;
  title: string | null;
  description: string | null;
  ownerId: string;
  members: ChatMember[];
  memberCount: number;
  pinnedMessageIds: string[];
  lastMessageId: string | null;
  lastMessageAt: string | null;
  lastMessagePreview: string | null;
  seqCounter: number;
  unreadCount: number;
  lastReadSeq: number;
  createdAt: string;
  isPublic: boolean;
  username: string | null;
  /** Active invite code. Visible to members only (server-enforced). */
  inviteCode: string | null;
  avatarUrl: string | null;
  membersCanInvite: boolean;
  /** Caller-relative preferences. */
  isMuted: boolean;
  isArchived: boolean;
}

export interface PublicChannel {
  id: string;
  title: string;
  description: string | null;
  username: string;
  memberCount: number;
  avatarUrl: string | null;
}

/** Display name for a conversation: group/channel title, or the peer in a DM. */
export function chatDisplayName(chat: Chat, myUserId: string | undefined): string {
  if (chat.type !== 'Dm') return chat.title ?? 'Untitled';
  const peer = chat.members.find((m) => m.userId !== myUserId);
  return peer?.displayName ?? peer?.username ?? 'Direct message';
}

/** Peer user id for a DM (undefined for groups/channels). */
export function chatPeerId(chat: Chat, myUserId: string | undefined): string | undefined {
  if (chat.type !== 'Dm') return undefined;
  return chat.members.find((m) => m.userId !== myUserId)?.userId;
}

export function myRoleIn(chat: Chat, myUserId: string | undefined): string | undefined {
  return chat.members.find((m) => m.userId === myUserId)?.role;
}

export function isChatAdmin(chat: Chat, myUserId: string | undefined): boolean {
  const role = myRoleIn(chat, myUserId);
  return role === 'Owner' || role === 'Admin';
}
