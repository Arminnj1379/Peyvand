import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { chatApi } from '../features/chats/chatApi';
import { chatDisplayName } from '../features/chats/chatTypes';
import type { Chat } from '../features/chats/chatTypes';
import { isAdmin } from '../features/auth/authTypes';
import { useAuthStore } from '../features/auth/authStore';
import { formatConversationTime } from '../features/messages/messageTypes';
import { UserAvatar } from '../shared/ui/UserAvatar';
import { useUserQuery } from '../features/auth/useUser';
import { Icon } from '../shared/ui/Icon';
import { SearchInput } from '../shared/ui/SearchInput';
import { NotificationBell } from './NotificationBell';

export type FolderFilter =
  | 'all'
  | 'personal'
  | 'contacts'
  | 'noncontacts'
  | 'group'
  | 'channel'
  | 'archived';

const TABS: { key: Exclude<FolderFilter, 'archived'>; label: string }[] = [
  { key: 'all', label: 'All' },
  { key: 'personal', label: 'Personal' },
  { key: 'contacts', label: 'Contacts' },
  { key: 'noncontacts', label: 'Non-Contacts' },
  { key: 'group', label: 'Groups' },
  { key: 'channel', label: 'Channels' },
];

const AVATAR_COLORS = ['#2aabee', '#7b61ff', '#31b545', '#e8a13d', '#df3f40', '#009688'];

function colorFor(id: string): string {
  let h = 0;
  for (const c of id) h = (h * 31 + c.charCodeAt(0)) % 997;
  return AVATAR_COLORS[h % AVATAR_COLORS.length];
}

const PROMO_KEY = 'messenger:promo-birthday';

function promoVisibleInitial(): boolean {
  try {
    return localStorage.getItem(PROMO_KEY) !== 'dismissed';
  } catch {
    return true;
  }
}

function ConversationRow({
  chat: c,
  active,
  onSelect,
  onItemMenu,
  myUserId,
  isOnline,
}: {
  chat: Chat;
  active: boolean;
  onSelect: (id: string) => void;
  onItemMenu: (e: React.MouseEvent, chat: Chat) => void;
  myUserId: string | undefined;
  isOnline: (userId: string) => boolean;
}) {
  const name = chatDisplayName(c, myUserId);
  const peerId = c.type === 'Dm' ? (c.members.find((m) => m.userId !== myUserId)?.userId ?? null) : null;
  // DM peer photos come from the profile endpoint (shared cache key); group
  // and channel photos ride on the chat payload itself.
  const peer = useUserQuery(c.type === 'Dm' ? peerId : null);
  const avatarUrl = c.type === 'Dm' ? (peer.data?.avatarUrl ?? null) : c.avatarUrl;
  // Public channels carry a username; treat them as verified (no dedicated flag in the API).
  const verified = c.type === 'Channel' && c.isPublic && c.username !== null;
  return (
    <button
      className={`conversation-item${active ? ' active' : ''}`}
      onClick={() => onSelect(c.id)}
      onContextMenu={(e) => onItemMenu(e, c)}
      aria-current={active}
    >
      <UserAvatar avatarUrl={avatarUrl} name={name} color={colorFor(c.id)} size={54} online={peerId !== null && isOnline(peerId)} />
      <span className="conversation-main">
        <span className="conversation-top">
          <span className="conversation-name" dir="auto">
            {name}
            {verified ? (
              <span className="verified-ic" title="Verified">
                <Icon name="verified" size={15} />
              </span>
            ) : null}
            {c.isMuted ? (
              <span className="mute-ic" title="Muted">
                <Icon name="mute" size={13} />
              </span>
            ) : null}
          </span>
          <span className="conversation-time">
            {c.lastMessageAt ? formatConversationTime(c.lastMessageAt) : ''}
          </span>
        </span>
        <span className="conversation-bottom">
          <span className="conversation-preview" dir="auto">{c.lastMessagePreview ?? 'No messages yet'}</span>
          <span className="conversation-meta">
            {c.unreadCount > 0 ? (
              <span className={`unread-badge${c.isMuted ? ' muted' : ''}`}>{c.unreadCount}</span>
            ) : null}
          </span>
        </span>
      </span>
    </button>
  );
}

export function ChatList({
  chats,
  activeId,
  onSelect,
  myUserId,
  isOnline,
  query,
  onQueryChange,
  filter,
  onFilterChange,
  onNewChat,
  onJoinChannel,
  onOpenSearch,
  isLoading,
}: {
  chats: Chat[];
  activeId: string | null;
  onSelect: (id: string) => void;
  myUserId: string | undefined;
  isOnline: (userId: string) => boolean;
  query: string;
  onQueryChange: (value: string) => void;
  filter: FolderFilter;
  onFilterChange: (filter: FolderFilter) => void;
  onNewChat: () => void;
  onJoinChannel: () => void;
  onOpenSearch: () => void;
  isLoading: boolean;
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [promoVisible, setPromoVisible] = useState(promoVisibleInitial);
  const [itemMenu, setItemMenu] = useState<{ chat: Chat; x: number; y: number } | null>(null);
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useAuthStore((s) => s.logout);
  const queryClient = useQueryClient();
  const label = user?.displayName ?? '…';
  // Own photo for the header account button (shared profile cache with
  // conversation rows; the store copy covers first paint).
  const selfProfile = useUserQuery(myUserId);
  const selfAvatarUrl = selfProfile.data?.avatarUrl ?? user?.avatarUrl ?? null;

  const visible = chats.filter((c) => {
    const name = chatDisplayName(c, myUserId);
    const matchesQuery =
      query.trim() === '' || name.toLowerCase().includes(query.trim().toLowerCase());
    if (filter === 'archived') return c.isArchived && matchesQuery;
    if (c.isArchived) return false;
    switch (filter) {
      case 'all':
        return matchesQuery;
      case 'group':
        return c.type === 'Group' && matchesQuery;
      case 'channel':
        return c.type === 'Channel' && matchesQuery;
      case 'personal':
      case 'contacts':
      case 'noncontacts':
        // The Contacts / Non-Contacts split needs an address-book flag from the
        // API; until then they show direct chats, same as Personal.
        return c.type === 'Dm' && matchesQuery;
      default:
        return matchesQuery;
    }
  });

  const nonArchived = chats.filter((c) => !c.isArchived);
  const counts = {
    all: nonArchived.length,
    personal: nonArchived.filter((c) => c.type === 'Dm').length,
    contacts: nonArchived.filter((c) => c.type === 'Dm').length,
    noncontacts: nonArchived.filter((c) => c.type === 'Dm').length,
    group: nonArchived.filter((c) => c.type === 'Group').length,
    channel: nonArchived.filter((c) => c.type === 'Channel').length,
    archived: chats.filter((c) => c.isArchived).length,
  };
  const totalUnread = nonArchived.reduce((sum, c) => sum + c.unreadCount, 0);

  async function refreshChats(): Promise<void> {
    await queryClient.invalidateQueries({ queryKey: ['chats'] });
  }

  function openItemMenu(e: React.MouseEvent, chat: Chat): void {
    e.preventDefault();
    e.stopPropagation();
    setItemMenu({ chat, x: e.clientX, y: e.clientY });
  }

  function dismissPromo(): void {
    try {
      localStorage.setItem(PROMO_KEY, 'dismissed');
    } catch {
      // Private mode: just hide for this session.
    }
    setPromoVisible(false);
  }

  const emptyHint =
    filter === 'archived'
      ? 'Nothing archived.'
      : filter === 'personal'
        ? 'No personal chats yet.'
        : filter === 'contacts'
          ? 'No contacts yet.'
          : filter === 'noncontacts'
            ? 'No chats from non-contacts.'
            : filter === 'group'
              ? 'No groups yet.'
              : filter === 'channel'
                ? 'No channels yet.'
                : 'No conversations yet.';

  return (
    <aside className="sidebar" aria-label="Conversations">
      <div className="sidebar-header">
        <button
          className="icon-btn"
          aria-label="Menu"
          title="Menu"
          onClick={() => setMenuOpen((open) => !open)}
          aria-expanded={menuOpen}
        >
          <Icon name="menu" />
        </button>
        <SearchInput value={query} onChange={onQueryChange} />
        <button
          className="icon-btn"
          aria-label="Search messages, chats, and people"
          title="Search (Ctrl+K)"
          onClick={onOpenSearch}
        >
          <Icon name="search" />
        </button>
        <NotificationBell
          totalUnread={totalUnread}
          onOpenChat={onSelect}
        />
        <button
          className="header-avatar-btn"
          onClick={() => setMenuOpen((open) => !open)}
          aria-haspopup="menu"
          aria-expanded={menuOpen}
          aria-label="Account menu"
          title={label}
        >
          <UserAvatar avatarUrl={selfAvatarUrl} name={label} color="#8774e1" size={38} />
        </button>
        {menuOpen ? (
          <div className="profile-menu top" role="menu">
            <button
              onClick={() => {
                setMenuOpen(false);
                void navigate('/profile');
              }}
            >
              <Icon name="settings" size={16} />
              My profile
            </button>
            <button
              onClick={() => {
                setMenuOpen(false);
                onFilterChange('archived');
              }}
            >
              <Icon name="archive" size={16} />
              Archived chats
              {counts.archived > 0 ? (
                <span className="folder-count dim">{counts.archived}</span>
              ) : null}
            </button>
            {isAdmin(user) ? (
              <button
                onClick={() => {
                  setMenuOpen(false);
                  void navigate('/admin');
                }}
              >
                <Icon name="users" size={16} />
                User administration
              </button>
            ) : null}
            <button
              onClick={() => {
                setMenuOpen(false);
                void navigate('/support');
              }}
            >
              <Icon name="info" size={16} />
              Support
            </button>
            <div className="menu-sep" aria-hidden="true" />
            <button
              onClick={() => {
                setMenuOpen(false);
                void logout().then(() => navigate('/login'));
              }}
            >
              <Icon name="logout" size={16} />
              Log out
            </button>
          </div>
        ) : null}
      </div>

      <nav className="folder-tabs" aria-label="Folders">
        {TABS.map((tab) => (
          <button
            key={tab.key}
            className={`folder-tab${filter === tab.key ? ' active' : ''}`}
            onClick={() => onFilterChange(tab.key)}
            aria-pressed={filter === tab.key}
          >
            {tab.label}
            {tab.key === 'all' && totalUnread > 0 ? (
              <span className="folder-count accent">{totalUnread}</span>
            ) : null}
            {tab.key === 'group' ? (
              <span className="folder-count dim">{counts.group}</span>
            ) : null}
            {tab.key === 'channel' ? (
              <span className="folder-count dim">{counts.channel}</span>
            ) : null}
          </button>
        ))}
      </nav>

      {promoVisible ? (
        <div className="promo-banner" role="note" aria-label="Add your birthday">
          <span className="promo-ic" aria-hidden="true">
            <Icon name="gift" size={18} />
          </span>
          <span className="promo-main">
            <span className="promo-title">Add your birthday!</span>
            <span className="promo-sub">Let friends celebrate with you 🎂</span>
          </span>
          <button
            className="icon-btn"
            aria-label="Dismiss"
            title="Dismiss"
            onClick={dismissPromo}
            style={{ width: 30, height: 30 }}
          >
            <Icon name="close" size={14} />
          </button>
        </div>
      ) : null}

      {filter === 'archived' ? (
        <div className="archived-bar">
          <Icon name="archive" size={15} />
          <span>
            Archived · {counts.archived}
          </span>
          <button className="ghost-btn inline small" onClick={() => onFilterChange('all')}>
            Show all
          </button>
        </div>
      ) : null}

      <div className="conversation-list" role="listbox" aria-label="Conversation list">
        {isLoading ? (
          <p className="loading-line">Loading…</p>
        ) : null}
        {!isLoading && visible.length === 0 ? (
          <div className="empty-chats">
            <span className="empty-icon" aria-hidden="true">
              <Icon name={filter === 'archived' ? 'archive' : 'users'} size={26} />
            </span>
            <p>{emptyHint}</p>
            <div className="row-actions">
              <button className="ghost-btn inline" onClick={onNewChat}>
                Start a conversation
              </button>
              <button className="ghost-btn inline" onClick={onJoinChannel}>
                Join a channel
              </button>
            </div>
          </div>
        ) : null}
        {visible.map((c) => (
          <ConversationRow
            key={c.id}
            chat={c}
            active={c.id === activeId}
            onSelect={onSelect}
            onItemMenu={openItemMenu}
            myUserId={myUserId}
            isOnline={isOnline}
          />
        ))}
      </div>
      <button
        className="new-chat-fab"
        aria-label="New conversation"
        title="New conversation"
        onClick={onNewChat}
      >
        <Icon name="edit" size={20} />
      </button>

      {itemMenu ? (
        <div className="menu-backdrop" onClick={() => setItemMenu(null)}>
          <div
            className="context-menu"
            role="menu"
            style={{ left: Math.min(itemMenu.x, window.innerWidth - 200), top: Math.min(itemMenu.y, window.innerHeight - 200) }}
            onClick={(e) => e.stopPropagation()}
          >
            <button
              onClick={() => {
                const chat = itemMenu.chat;
                setItemMenu(null);
                void chatApi.setArchived(chat.id, !chat.isArchived).then(() => refreshChats());
              }}
            >
              <Icon name="archive" size={16} />
              {itemMenu.chat.isArchived ? 'Unarchive' : 'Archive'}
            </button>
            <button
              onClick={() => {
                const chat = itemMenu.chat;
                setItemMenu(null);
                void chatApi.setMuted(chat.id, !chat.isMuted).then(() => refreshChats());
              }}
            >
              <Icon name={itemMenu.chat.isMuted ? 'bell' : 'mute'} size={16} />
              {itemMenu.chat.isMuted ? 'Unmute' : 'Mute'}
            </button>
            {itemMenu.chat.seqCounter > 0 ? (
              <button
                onClick={() => {
                  const chat = itemMenu.chat;
                  setItemMenu(null);
                  void chatApi.markUnread(chat.id, chat.seqCounter).then(() => refreshChats());
                }}
              >
                <Icon name="checks" size={16} />
                Mark as unread
              </button>
            ) : null}
          </div>
        </div>
      ) : null}
    </aside>
  );
}
