import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { callApi } from '../features/calls/callApi';
import { callHistoryLabel } from '../features/calls/callTypes';
import { chatApi } from '../features/chats/chatApi';
import { chatDisplayName, isChatAdmin } from '../features/chats/chatTypes';
import type { Chat } from '../features/chats/chatTypes';
import { ApiError, apiClient } from '../shared/api/client';
import { UserAvatar } from '../shared/ui/UserAvatar';
import { useUserQuery } from '../features/auth/useUser';
import { Icon } from '../shared/ui/Icon';

interface FoundUser {
  id: string;
  username: string;
  displayName: string;
}

const ROLE_OPTIONS = ['Member', 'Admin', 'Owner'];
const CHANNEL_ROLE_OPTIONS = ['Subscriber', 'Member', 'Admin', 'Owner'];

function colorFor(id: string): string {
  const colors = ['#2aabee', '#7b61ff', '#31b545', '#e8a13d', '#df3f40', '#009688'];
  let h = 0;
  for (const c of id) h = (h * 31 + c.charCodeAt(0)) % 997;
  return colors[h % colors.length];
}

function MemberRow({
  chat,
  member,
  myUserId,
  admin,
  onRole,
  onRemove,
}: {
  chat: Chat;
  member: Chat['members'][number];
  myUserId: string | undefined;
  admin: boolean;
  onRole: (userId: string, role: string) => void;
  onRemove: (userId: string) => void;
}) {
  const profile = useUserQuery(member.userId);
  return (
    <li className="member-row">
      <UserAvatar
        avatarUrl={profile.data?.avatarUrl ?? null}
        name={member.displayName}
        color={colorFor(member.userId)}
        size={36}
      />
      <span className="member-main">
        <strong>{member.displayName}</strong>
        <span className="hint">@{member.username}</span>
      </span>
      {admin && member.userId !== myUserId && chat.type !== 'Dm' ? (
        <span className="member-actions">
          <select
            aria-label={`Role for ${member.displayName}`}
            value={member.role}
            onChange={(e) => onRole(member.userId, e.target.value)}
          >
            {(chat.type === 'Channel' ? CHANNEL_ROLE_OPTIONS : ROLE_OPTIONS).map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </select>
          <button
            className="ghost-btn small inline"
            onClick={() => onRemove(member.userId)}
          >
            Remove
          </button>
        </span>
      ) : (
        <span className="chip">{member.role}</span>
      )}
    </li>
  );
}

export function ChatDetails({
  chat,
  myUserId,
  onClose,
  onLeft,
}: {
  chat: Chat;
  myUserId: string | undefined;
  onClose: () => void;
  onLeft: () => void;
}) {
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [results, setResults] = useState<FoundUser[]>([]);
  const [title, setTitle] = useState(chat.title ?? '');
  const [description, setDescription] = useState(chat.description ?? '');
  const [username, setUsername] = useState(chat.username ?? '');
  const [invite, setInvite] = useState<string | null>(chat.inviteCode ?? null);
  const [blocked, setBlocked] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const admin = isChatAdmin(chat, myUserId);
  const isOwner = chat.ownerId === myUserId;
  const name = chatDisplayName(chat, myUserId);
  const peerId = chat.type === 'Dm' ? chat.members.find((m) => m.userId !== myUserId)?.userId : undefined;

  const pinsQuery = useQuery({
    queryKey: ['pins', chat.id],
    queryFn: ({ signal }) => chatApi.pins(chat.id, signal),
    staleTime: 30_000,
  });
  const pins = pinsQuery.data ?? [];

  const callsQuery = useQuery({
    queryKey: ['calls', chat.id],
    queryFn: ({ signal }) => callApi.list(chat.id, 10, signal),
    staleTime: 15_000,
  });
  const calls = callsQuery.data ?? [];

  // DM peer photo (shared profile cache with list/header).
  const peerProfile = useUserQuery(chat.type === 'Dm' ? peerId : undefined);
  const heroAvatarUrl = chat.type === 'Dm' ? (peerProfile.data?.avatarUrl ?? null) : chat.avatarUrl;
  async function refresh(): Promise<void> {
    await queryClient.invalidateQueries({ queryKey: ['chats'] });
    await pinsQuery.refetch();
    await callsQuery.refetch();
    try {
      setInvite(await chatApi.invite(chat.id));
    } catch {
      // Members who lost access simply stop seeing the link.
    }
  }

  function fail(e: unknown, fallback: string): void {
    setError(e instanceof ApiError ? e.message : fallback);
  }

  async function onSearch(value: string): Promise<void> {
    setSearch(value);
    setError(null);
    if (value.trim().length < 2) {
      setResults([]);
      return;
    }
    try {
      const users = await apiClient.get<FoundUser[]>(
        `/api/v1/users/search?q=${encodeURIComponent(value.trim())}&limit=8`,
      );
      setResults(users.filter((u) => !chat.members.some((m) => m.userId === u.id)));
    } catch {
      setResults([]);
    }
  }

  async function add(userId: string): Promise<void> {
    setError(null);
    try {
      await chatApi.addMember(chat.id, userId);
      setResults((r) => r.filter((u) => u.id !== userId));
      setSearch('');
      await refresh();
    } catch (e) {
      fail(e, 'Could not add member.');
    }
  }

  async function saveProfile(): Promise<void> {
    setError(null);
    try {
      if (chat.type === 'Group') {
        await chatApi.updateProfile(chat.id, { title: title.trim() || undefined, description: description.trim() || undefined });
      } else if (chat.type === 'Channel') {
        await chatApi.updateProfile(chat.id, { title: title.trim() || undefined, description: description.trim() || undefined });
      }
      await refresh();
    } catch (e) {
      fail(e, 'Could not save.');
    }
  }

  async function saveVisibility(isPublic: boolean): Promise<void> {
    setError(null);
    try {
      await chatApi.setVisibility(chat.id, isPublic ? { isPublic, username: username.trim().toLowerCase() } : { isPublic });
      await refresh();
    } catch (e) {
      fail(e, 'Could not change visibility.');
    }
  }

  async function onAvatar(file: File): Promise<void> {
    setError(null);
    try {
      await chatApi.uploadAvatar(chat.id, file);
      await refresh();
    } catch (e) {
      fail(e, 'Could not upload avatar.');
    }
  }

  async function removeAvatar(): Promise<void> {
    setError(null);
    try {
      await chatApi.removeAvatar(chat.id);
      await refresh();
    } catch (e) {
      fail(e, 'Could not remove avatar.');
    }
  }

  async function confirmDeleteChat(): Promise<void> {
    if (deleting) return;
    setDeleting(true);
    setError(null);
    try {
      await chatApi.deleteChat(chat.id);
      await queryClient.invalidateQueries({ queryKey: ['chats'] });
      onLeft();
    } catch (e) {
      fail(e, 'Could not delete the conversation.');
    } finally {
      setDeleting(false);
      setConfirmDelete(false);
    }
  }

  async function toggleBlock(): Promise<void> {
    if (!peerId) return;
    setError(null);
    try {
      if (!blocked) await apiClient.post(`/api/v1/users/${peerId}/block`, {});
      else await apiClient.delete(`/api/v1/users/${peerId}/block`);
      setBlocked(!blocked);
    } catch (e) {
      fail(e, 'Could not update block.');
    }
  }

  const canInvite = admin || (chat.type === 'Group' && chat.membersCanInvite && chat.members.some((m) => m.userId === myUserId));

  return (
    <aside className="details-panel" aria-label="Conversation details">
      <div className="details-close">
        <button className="icon-btn" onClick={onClose} aria-label="Close details">
          <Icon name="close" />
        </button>
      </div>
      <div className="details-hero">
        <UserAvatar
          avatarUrl={heroAvatarUrl}
          name={name}
          color={colorFor(chat.id)}
          size={72}
        />
        <div className="details-name">{name}</div>
        <div className="details-sub">
          {chat.type}
          {chat.type === 'Channel' && chat.isPublic && chat.username ? ` · @${chat.username}` : ''}
          {chat.type !== 'Dm' ? ` · ${chat.memberCount} members` : ''}
          {chat.isMuted ? ' · muted' : ''}
        </div>
        {chat.description ? <p className="hint">{chat.description}</p> : null}
      </div>

      {error ? <div className="form-error">{error}</div> : null}

      {admin && chat.type !== 'Dm' ? (
        <>
          <h3 className="details-h">Profile</h3>
          <div className="field">
            <label htmlFor="chat-title">Title</label>
            <input id="chat-title" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={64} />
          </div>
          <div className="field">
            <label htmlFor="chat-desc">Description</label>
            <input id="chat-desc" value={description} onChange={(e) => setDescription(e.target.value)} maxLength={512} />
          </div>
          <div className="row-actions">
            <button className="ghost-btn inline" onClick={() => void saveProfile()}>
              Save profile
            </button>
          </div>
          <div className="field">
            <label htmlFor="chat-avatar">Avatar</label>
            <input
              id="chat-avatar"
              type="file"
              accept="image/jpeg,image/png,image/webp"
              onChange={(e) => {
                const f = e.target.files?.[0];
                if (f) void onAvatar(f);
                e.target.value = '';
              }}
            />
          </div>
          {chat.avatarUrl ? (
            <div className="row-actions">
              <button className="ghost-btn inline" onClick={() => void removeAvatar()}>
                Remove avatar
              </button>
            </div>
          ) : null}
        </>
      ) : null}

      {admin && chat.type === 'Group' ? (
        <label className="check">
          <input
            type="checkbox"
            checked={chat.membersCanInvite}
            onChange={(e) => {
              setError(null);
              void chatApi
                .updateProfile(chat.id, { membersCanInvite: e.target.checked })
                .then(() => refresh())
                .catch((err: unknown) => fail(err, 'Could not change invite policy.'));
            }}
          />
          Members can invite others
        </label>
      ) : null}

      {admin && chat.type === 'Channel' ? (
        <>
          <h3 className="details-h">Visibility</h3>
          <p className="hint">{chat.isPublic ? `Public · @${chat.username ?? ''}` : 'Private · join by invite only'}</p>
          <div className="field">
            <label htmlFor="chat-username">Username (public channels)</label>
            <input
              id="chat-username"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              maxLength={32}
              placeholder="e.g. opennews"
            />
          </div>
          <div className="row-actions">
            {!chat.isPublic ? (
              <button className="ghost-btn inline" onClick={() => void saveVisibility(true)}>
                Make public
              </button>
            ) : (
              <button className="ghost-btn inline" onClick={() => void saveVisibility(false)}>
                Make private
              </button>
            )}
          </div>
        </>
      ) : null}

      {chat.type !== 'Dm' ? (
        <>
          <h3 className="details-h">Invite link</h3>
          {invite ? (
            <div className="invite-box">
              <code>{invite}</code>
              <button
                className="ghost-btn small inline"
                onClick={() => void navigator.clipboard?.writeText(invite).catch(() => undefined)}
              >
                Copy
              </button>
            </div>
          ) : (
            <p className="hint">No active invite link.</p>
          )}
          {admin ? (
            <div className="row-actions">
              <button
                className="ghost-btn inline"
                onClick={() => {
                  setError(null);
                  void chatApi
                    .rotateInvite(chat.id)
                    .then((code) => {
                      setInvite(code);
                      return refresh();
                    })
                    .catch((e: unknown) => fail(e, 'Could not create invite.'));
                }}
              >
                {invite ? 'Rotate link' : 'Create link'}
              </button>
              {invite ? (
                <button
                  className="ghost-btn inline"
                  onClick={() => {
                    setError(null);
                    void chatApi
                      .revokeInvite(chat.id)
                      .then(() => {
                        setInvite(null);
                        return refresh();
                      })
                      .catch((e: unknown) => fail(e, 'Could not revoke invite.'));
                  }}
                >
                  Revoke
                </button>
              ) : null}
            </div>
          ) : null}
        </>
      ) : null}

      {pins.length > 0 ? (
        <>
          <h3 className="details-h">Pinned ({pins.length})</h3>
          <ul className="pin-list">
            {pins.map((p) => (
              <li key={p.id} className="pin-row">
                <span className="pin-text">{p.isDeleted ? 'Message deleted' : p.body.slice(0, 120)}</span>
                {(admin || chat.type === 'Dm') ? (
                  <button
                    className="ghost-btn small inline"
                    onClick={() => {
                      setError(null);
                      void chatApi
                        .unpin(chat.id, p.id)
                        .then(() => refresh())
                        .catch((e: unknown) => fail(e, 'Could not unpin.'));
                    }}
                  >
                    Unpin
                  </button>
                ) : null}
              </li>
            ))}
          </ul>
        </>
      ) : null}

      {pinsQuery.isPending ? <p className="hint">Loading pins…</p> : null}
      {callsQuery.isPending ? <p className="hint">Loading calls…</p> : null}
      {calls.length > 0 ? (
        <>
          <h3 className="details-h">Calls ({calls.length})</h3>
          <ul className="call-history">
            {calls.map((c) => (
              <li key={c.id} className="call-row">
                <Icon name={c.mediaType === 'Video' ? 'video' : 'phone'} size={16} />
                <span className="call-main">
                  <strong>{callHistoryLabel(c, c.initiatorId === myUserId)}</strong>
                  <span className="hint">
                    {new Date(c.createdAt).toLocaleString()}
                    {c.status !== 'Ended' ? ` · ${c.status.toLowerCase()}` : null}
                  </span>
                </span>
              </li>
            ))}
          </ul>
        </>
      ) : null}

      <h3 className="details-h">Members ({chat.memberCount})</h3>      <ul className="member-list">
        {chat.members.map((m) => (
          <MemberRow
            key={m.userId}
            chat={chat}
            member={m}
            myUserId={myUserId}
            admin={admin}
            onRole={(userId, role) => {
              setError(null);
              void chatApi
                .setRole(chat.id, userId, role)
                .then(() => refresh())
                .catch((e: unknown) => fail(e, 'Could not change role.'));
            }}
            onRemove={(userId) => {
              setError(null);
              void chatApi
                .removeMember(chat.id, userId)
                .then(() => refresh())
                .catch((e: unknown) => fail(e, 'Could not remove member.'));
            }}
          />
        ))}
      </ul>

      {canInvite ? (
        <div className="field">
          <label htmlFor="add-member">Add member</label>
          <input
            id="add-member"
            value={search}
            onChange={(e) => void onSearch(e.target.value)}
            placeholder="Search by name…"
          />
          {results.map((u) => (
            <button key={u.id} className="picker-row" onClick={() => void add(u.id)}>
              <strong>{u.displayName}</strong>
              <span className="hint">@{u.username}</span>
            </button>
          ))}
        </div>
      ) : null}

      <h3 className="details-h">Preferences</h3>
      <div className="row-actions">
        <button
          className="ghost-btn inline"
          onClick={() => {
            setError(null);
            void chatApi
              .setMuted(chat.id, !chat.isMuted)
              .then(() => refresh())
              .catch((e: unknown) => fail(e, 'Could not change mute.'));
          }}
        >
          {chat.isMuted ? 'Unmute' : 'Mute'}
        </button>
        <button
          className="ghost-btn inline"
          onClick={() => {
            setError(null);
            void chatApi
              .setArchived(chat.id, !chat.isArchived)
              .then(() => refresh())
              .catch((e: unknown) => fail(e, 'Could not change archive.'));
          }}
        >
          {chat.isArchived ? 'Unarchive' : 'Archive'}
        </button>
      </div>

      {chat.type === 'Channel' && !admin ? (
        <div className="details-actions">
          <button
            onClick={() => {
              setError(null);
              void chatApi
                .unsubscribe(chat.id)
                .then(() => queryClient.invalidateQueries({ queryKey: ['chats'] }).then(() => onLeft()))
                .catch((e: unknown) => fail(e, 'Could not unsubscribe.'));
            }}
          >
            <Icon name="logout" size={16} />
            Unsubscribe
          </button>
        </div>
      ) : null}

      {chat.type !== 'Dm' && chat.type !== 'Channel' ? (
        <div className="details-actions">
          <button
            onClick={() => {
              setError(null);
              void chatApi
                .leave(chat.id)
                .then(() => queryClient.invalidateQueries({ queryKey: ['chats'] }).then(() => onLeft()))
                .catch((e: unknown) => fail(e, 'Could not leave.'));
            }}
          >
            <Icon name="logout" size={16} />
            Leave conversation
          </button>
        </div>
      ) : null}

      {isOwner && chat.type !== 'Dm' ? (
        <div className="details-actions">
          <button className="danger-text" onClick={() => setConfirmDelete(true)}>
            <Icon name="trash" size={16} />
            Delete conversation
          </button>
        </div>
      ) : null}

      {chat.type === 'Dm' && peerId ? (
        <div className="details-actions">
          <button onClick={() => void toggleBlock()}>
            <Icon name={blocked ? 'bell' : 'mute'} size={16} />
            {blocked ? 'Unblock user' : 'Block user'}
          </button>
        </div>
      ) : null}

      {confirmDelete ? (
        <div
          className="dialog-backdrop"
          onClick={() => {
            if (!deleting) setConfirmDelete(false);
          }}
          role="presentation"
        >
          <div
            className="dialog"
            role="dialog"
            aria-modal="true"
            aria-label="Delete conversation"
            onClick={(e) => e.stopPropagation()}
          >
            <h2>Delete this conversation?</h2>
            <p className="hint">
              “{name}” will be removed for <strong>all members</strong>. Message history is
              preserved on the server but the conversation will disappear from everyone's list.
              This cannot be undone.
            </p>
            <div className="row-actions">
              <button
                className="ghost-btn inline"
                disabled={deleting}
                onClick={() => setConfirmDelete(false)}
              >
                Cancel
              </button>
              <button
                className="ghost-btn inline danger-text"
                disabled={deleting}
                onClick={() => void confirmDeleteChat()}
              >
                {deleting ? 'Deleting…' : 'Delete for everyone'}
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </aside>
  );
}
