import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { callApi } from '../features/calls/callApi';
import { formatCallDuration } from '../features/calls/callTypes';
import { chatApi } from '../features/chats/chatApi';
import { chatDisplayName, isChatAdmin } from '../features/chats/chatTypes';
import type { Chat } from '../features/chats/chatTypes';
import { ApiError, apiClient } from '../shared/api/client';
import { UserAvatar } from '../shared/ui/UserAvatar';
import { useUserQuery } from '../features/auth/useUser';
import type { CallInfo } from '../features/calls/callTypes';
import { Icon } from '../shared/ui/Icon';
import { useT } from '../shared/i18n';
import type { TFunction } from '../shared/i18n';

interface FoundUser {
  id: string;
  username: string;
  displayName: string;
}

const ROLE_OPTIONS = ['Member', 'Admin', 'Owner'];
const CHANNEL_ROLE_OPTIONS = ['Subscriber', 'Member', 'Admin', 'Owner'];

function roleLabel(t: TFunction, role: string): string {
  switch (role) {
    case 'Member':
      return t('details.roleMember');
    case 'Admin':
      return t('details.roleAdmin');
    case 'Owner':
      return t('details.roleOwner');
    case 'Subscriber':
      return t('details.roleSubscriber');
    default:
      return role;
  }
}

/** Localized equivalent of callHistoryLabel (lives here to keep callTypes locale-free). */
function historyLabel(t: TFunction, call: CallInfo, isOutgoing: boolean): string {
  const video = call.mediaType === 'Video';
  if (call.status !== 'Ended') return video ? t('call.historyVideo') : t('call.historyVoice');
  if (call.endReason === 'Declined')
    return video
      ? isOutgoing
        ? t('call.historyDeclinedVideo')
        : t('call.historyMissedVideo')
      : isOutgoing
        ? t('call.historyDeclinedVoice')
        : t('call.historyMissedVoice');
  if (call.endReason === 'Cancelled')
    return t(video ? 'call.historyCancelledVideo' : 'call.historyCancelledVoice');
  return t('call.historyDone', {
    kind: video ? t('call.historyVideo') : t('call.historyVoice'),
    duration: formatCallDuration(call.durationSeconds),
  });
}

function callStatusLabel(t: TFunction, status: CallInfo['status']): string {
  return status === 'Ringing' ? t('call.statusRinging') : t('call.statusActive');
}

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
  const t = useT();
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
            aria-label={t('details.roleFor', { name: member.displayName })}
            value={member.role}
            onChange={(e) => onRole(member.userId, e.target.value)}
          >
            {(chat.type === 'Channel' ? CHANNEL_ROLE_OPTIONS : ROLE_OPTIONS).map((r) => (
              <option key={r} value={r}>
                {roleLabel(t, r)}
              </option>
            ))}
          </select>
          <button
            className="member-remove"
            onClick={() => onRemove(member.userId)}
            aria-label={t('details.removeMember', { name: member.displayName })}
            title={t('common.remove')}
          >
            <Icon name="trash" size={15} />
          </button>
        </span>
      ) : (
        <span className={member.role === 'Owner' ? 'chip owner' : 'chip'}>{roleLabel(t, member.role)}</span>
      )}
    </li>
  );
}

export function ChatDetails({
  chat,
  myUserId,
  onClose,
  onLeft,
  leaving,
}: {
  chat: Chat;
  myUserId: string | undefined;
  onClose: () => void;
  onLeft: () => void;
  leaving?: boolean;
}) {
  const queryClient = useQueryClient();
  const t = useT();
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
  // DM peer handle for the hero line (shared profile cache with list/header).
  const peer = chat.type === 'Dm' ? chat.members.find((m) => m.userId !== myUserId) : undefined;
  const peerId = peer?.userId;

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
      fail(e, t('details.errAdd'));
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
      fail(e, t('details.errSave'));
    }
  }

  async function saveVisibility(isPublic: boolean): Promise<void> {
    setError(null);
    try {
      await chatApi.setVisibility(chat.id, isPublic ? { isPublic, username: username.trim().toLowerCase() } : { isPublic });
      await refresh();
    } catch (e) {
      fail(e, t('details.errVisibility'));
    }
  }

  async function onAvatar(file: File): Promise<void> {
    setError(null);
    try {
      await chatApi.uploadAvatar(chat.id, file);
      await refresh();
    } catch (e) {
      fail(e, t('details.errAvatarUpload'));
    }
  }

  async function removeAvatar(): Promise<void> {
    setError(null);
    try {
      await chatApi.removeAvatar(chat.id);
      await refresh();
    } catch (e) {
      fail(e, t('details.errAvatarRemove'));
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
      fail(e, t('details.errDelete'));
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
      fail(e, t('details.errBlock'));
    }
  }

  const canInvite = admin || (chat.type === 'Group' && chat.membersCanInvite && chat.members.some((m) => m.userId === myUserId));

  return (
    <aside
      className={`details-panel${leaving ? ' leaving' : ''}`}
      aria-label={t('details.panelLabel')}
    >
      <div className="details-close">
        <button className="icon-btn" onClick={onClose} aria-label={t('details.closeDetails')}>
          <Icon name="close" />
        </button>
      </div>
      <div className="details-hero">
        <span className="hero-avatar">
          <UserAvatar
            avatarUrl={heroAvatarUrl}
            name={name}
            color={colorFor(chat.id)}
            size={80}
          />
        </span>
        <div className="details-name" dir="auto">{name}</div>
        <div className="details-sub">
          {chat.type !== 'Dm' ? (
            <span className="chip">{chat.type === 'Channel' ? t('details.typeChannel') : t('details.typeGroup')}</span>
          ) : null}
          {peer ? <span dir="auto">@{peer.username}</span> : null}
          {chat.type !== 'Dm' ? (
            <span>{chat.memberCount} {chat.type === 'Channel' ? t('details.subscribers') : t('details.members')}</span>
          ) : null}
          {chat.type === 'Channel' && chat.isPublic && chat.username ? <span dir="auto">@{chat.username}</span> : null}
          {chat.isMuted ? <span className="chip">{t('details.muted')}</span> : null}
        </div>
        {chat.description ? <p className="details-desc" dir="auto">{chat.description}</p> : null}
      </div>

      {error ? (
        <div className="form-error" role="alert">
          <Icon name="info" size={15} />
          <span>{error}</span>
        </div>
      ) : null}

      {admin && chat.type !== 'Dm' ? (
        <>
          <h3 className="details-h">{t('details.profile')}</h3>
          <div className="field">
            <label htmlFor="chat-title">{t('details.titleLabel')}</label>
            <input id="chat-title" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={64} />
          </div>
          <div className="field">
            <label htmlFor="chat-desc">{t('details.descLabel')}</label>
            <input id="chat-desc" value={description} onChange={(e) => setDescription(e.target.value)} maxLength={512} />
          </div>
          <div className="row-actions">
            <button className="primary-btn inline" onClick={() => void saveProfile()}>
              {t('details.saveProfile')}
            </button>
          </div>
          <div className="field">
            <span className="field-label" id="chat-avatar-label">{t('details.avatar')}</span>
            <div className="row-actions avatar-actions">
              <label className="ghost-btn inline file-btn" htmlFor="chat-avatar">
                <Icon name="image" size={15} />
                {t('details.changePhoto')}
              </label>
              {chat.avatarUrl ? (
                <button className="ghost-btn inline" onClick={() => void removeAvatar()}>
                  {t('common.remove')}
                </button>
              ) : null}
            </div>
            <input
              id="chat-avatar"
              className="visually-hidden"
              type="file"
              accept="image/jpeg,image/png,image/webp"
              aria-labelledby="chat-avatar-label"
              onChange={(e) => {
                const f = e.target.files?.[0];
                if (f) void onAvatar(f);
                e.target.value = '';
              }}
            />
          </div>
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
                .catch((err: unknown) => fail(err, t('details.errInvitePolicy')));
            }}
          />
          {t('details.membersCanInvite')}
        </label>
      ) : null}

      {admin && chat.type === 'Channel' ? (
        <>
          <h3 className="details-h">{t('details.visibility')}</h3>
          <p className="hint">{chat.isPublic ? t('details.visibilityPublic', { username: chat.username ?? '' }) : t('details.visibilityPrivate')}</p>
          <div className="field">
            <label htmlFor="chat-username">{t('details.usernameLabel')}</label>
            <input
              id="chat-username"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              maxLength={32}
              placeholder={t('details.usernamePh')}
            />
          </div>
          <div className="row-actions">
            {!chat.isPublic ? (
              <button className="ghost-btn inline" onClick={() => void saveVisibility(true)}>
                {t('details.makePublic')}
              </button>
            ) : (
              <button className="ghost-btn inline" onClick={() => void saveVisibility(false)}>
                {t('details.makePrivate')}
              </button>
            )}
          </div>
        </>
      ) : null}

      {chat.type !== 'Dm' ? (
        <>
          <h3 className="details-h">{t('details.inviteLink')}</h3>
          {invite ? (
            <div className="invite-box">
              <code>{invite}</code>
              <button
                className="ghost-btn small inline"
                onClick={() => void navigator.clipboard?.writeText(invite).catch(() => undefined)}
              >
                <Icon name="copy" size={14} />
                {t('details.copy')}
              </button>
            </div>
          ) : (
            <p className="hint">{t('details.noInvite')}</p>
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
                    .catch((e: unknown) => fail(e, t('details.errCreateInvite')));
                }}
              >
                {invite ? t('details.rotateLink') : t('details.createLink')}
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
                      .catch((e: unknown) => fail(e, t('details.errRevokeInvite')));
                  }}
                >
                  {t('common.revoke')}
                </button>
              ) : null}
            </div>
          ) : null}
        </>
      ) : null}

      {pins.length > 0 ? (
        <>
          <h3 className="details-h">{t('details.pinned', { count: pins.length })}</h3>
          <ul className="pin-list">
            {pins.map((p) => (
              <li key={p.id} className="pin-row">
                <span className="pin-text">{p.isDeleted ? t('details.messageDeleted') : p.body.slice(0, 120)}</span>
                {(admin || chat.type === 'Dm') ? (
                  <button
                    className="ghost-btn small inline"
                    onClick={() => {
                      setError(null);
                      void chatApi
                        .unpin(chat.id, p.id)
                        .then(() => refresh())
                        .catch((e: unknown) => fail(e, t('details.errUnpin')));
                    }}
                  >
                    {t('details.unpin')}
                  </button>
                ) : null}
              </li>
            ))}
          </ul>
        </>
      ) : null}

      {pinsQuery.isPending ? <p className="hint">{t('details.loadingPins')}</p> : null}
      {callsQuery.isPending ? <p className="hint">{t('details.loadingCalls')}</p> : null}
      {calls.length > 0 ? (
        <>
          <h3 className="details-h">{t('details.callsTitle', { count: calls.length })}</h3>
          <ul className="call-history">
            {calls.map((c) => (
              <li key={c.id} className="call-row">
                <Icon name={c.mediaType === 'Video' ? 'video' : 'phone'} size={16} />
                <span className="call-main">
                  <strong>{historyLabel(t, c, c.initiatorId === myUserId)}</strong>
                  <span className="hint">
                    {new Date(c.createdAt).toLocaleString()}
                    {c.status !== 'Ended' ? ` · ${callStatusLabel(t, c.status)}` : null}
                  </span>
                </span>
              </li>
            ))}
          </ul>
        </>
      ) : null}

      <h3 className="details-h">{t('details.membersTitle', { count: chat.memberCount })}</h3>
      <ul className="member-list">
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
                .catch((e: unknown) => fail(e, t('details.errRole')));
            }}
            onRemove={(userId) => {
              setError(null);
              void chatApi
                .removeMember(chat.id, userId)
                .then(() => refresh())
                .catch((e: unknown) => fail(e, t('details.errRemoveMember')));
            }}
          />
        ))}
      </ul>

      {canInvite ? (
        <div className="field">
          <label htmlFor="add-member">{t('details.addMember')}</label>
          <input
            id="add-member"
            value={search}
            onChange={(e) => void onSearch(e.target.value)}
            placeholder={t('details.searchPh')}
          />
          {results.map((u) => (
            <button key={u.id} className="picker-row" onClick={() => void add(u.id)}>
              <strong>{u.displayName}</strong>
              <span className="hint">@{u.username}</span>
            </button>
          ))}
        </div>
      ) : null}

      <h3 className="details-h">{t('details.preferences')}</h3>
      <div className="row-actions even">
        <button
          className="ghost-btn inline"
          onClick={() => {
            setError(null);
            void chatApi
              .setMuted(chat.id, !chat.isMuted)
              .then(() => refresh())
              .catch((e: unknown) => fail(e, t('details.errMute')));
          }}
        >
          <Icon name={chat.isMuted ? 'bell' : 'mute'} size={15} />
          {chat.isMuted ? t('details.unmute') : t('details.mute')}
        </button>
        <button
          className="ghost-btn inline"
          onClick={() => {
            setError(null);
            void chatApi
              .setArchived(chat.id, !chat.isArchived)
              .then(() => refresh())
              .catch((e: unknown) => fail(e, t('details.errArchive')));
          }}
        >
          <Icon name="archive" size={15} />
          {chat.isArchived ? t('details.unarchive') : t('details.archive')}
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
                .catch((e: unknown) => fail(e, t('details.errUnsubscribe')));
            }}
          >
            <Icon name="logout" size={16} />
            {t('details.unsubscribe')}
          </button>
        </div>
      ) : null}

      {chat.type !== 'Dm' && chat.type !== 'Channel' ? (
        <div className="details-actions">
          <button
            className="danger-text"
            onClick={() => {
              setError(null);
              void chatApi
                .leave(chat.id)
                .then(() => queryClient.invalidateQueries({ queryKey: ['chats'] }).then(() => onLeft()))
                .catch((e: unknown) => fail(e, t('details.errLeave')));
            }}
          >
            <Icon name="logout" size={16} />
            {t('details.leaveConversation')}
          </button>
        </div>
      ) : null}

      {isOwner && chat.type !== 'Dm' ? (
        <div className="details-actions">
          <button className="danger-text" onClick={() => setConfirmDelete(true)}>
            <Icon name="trash" size={16} />
            {t('details.deleteConversation')}
          </button>
        </div>
      ) : null}

      {chat.type === 'Dm' && peerId ? (
        <div className="details-actions">
          <button onClick={() => void toggleBlock()}>
            <Icon name={blocked ? 'bell' : 'mute'} size={16} />
            {blocked ? t('details.unblockUser') : t('details.blockUser')}
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
            aria-label={t('details.deleteConversation')}
            onClick={(e) => e.stopPropagation()}
          >
            <h2>{t('details.deleteTitle')}</h2>
            <p className="hint">
              {t('details.deleteHint1', { name })}<strong>{t('details.deleteHintStrong')}</strong>{t('details.deleteHint2')}
            </p>
            <div className="row-actions">
              <button
                className="ghost-btn inline"
                disabled={deleting}
                onClick={() => setConfirmDelete(false)}
              >
                {t('common.cancel')}
              </button>
              <button
                className="ghost-btn inline danger-text"
                disabled={deleting}
                onClick={() => void confirmDeleteChat()}
              >
                {deleting ? t('details.deleting') : t('details.deleteForEveryone')}
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </aside>
  );
}
