import { useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { chatApi } from '../features/chats/chatApi';
import type { Chat } from '../features/chats/chatTypes';
import { ApiError, apiClient } from '../shared/api/client';
import { useEscape } from '../shared/ui/useEscape';

interface FoundUser {
  id: string;
  username: string;
  displayName: string;
}

type Tab = 'dm' | 'group' | 'channel';

export function NewChatDialog({
  myUserId,
  onClose,
  onCreated,
}: {
  myUserId: string | undefined;
  onClose: () => void;
  onCreated: (chatId: string) => void;
}) {
  const queryClient = useQueryClient();
  const closeRef = useRef(onClose);
  closeRef.current = onClose;
  useEscape(true, () => closeRef.current());
  const [tab, setTab] = useState<Tab>('dm');
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [isPublic, setIsPublic] = useState(false);
  const [username, setUsername] = useState('');
  const [search, setSearch] = useState('');
  const [results, setResults] = useState<FoundUser[]>([]);
  const [selected, setSelected] = useState<FoundUser[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSearch(value: string): Promise<void> {
    setSearch(value);
    if (value.trim().length < 2) {
      setResults([]);
      return;
    }
    try {
      const users = await apiClient.get<FoundUser[]>(
        `/api/v1/users/search?q=${encodeURIComponent(value.trim())}&limit=10`,
      );
      setResults(users.filter((u) => u.id !== myUserId));
    } catch {
      setResults([]);
    }
  }

  function toggle(user: FoundUser): void {
    setSelected((prev) =>
      prev.some((u) => u.id === user.id) ? prev.filter((u) => u.id !== user.id) : [...prev, user],
    );
  }

  async function create(): Promise<void> {
    setError(null);
    setBusy(true);
    try {
      let chat: Chat;
      if (tab === 'dm') {
        const peer = selected[0];
        if (!peer) {
          setError('Pick a person to message.');
          return;
        }
        chat = await chatApi.createDm(peer.id);
      } else if (tab === 'group') {
        if (!title.trim()) {
          setError('Give the group a title.');
          return;
        }
        if (selected.length === 0) {
          setError('Add at least one member.');
          return;
        }
        chat = await chatApi.createGroup({
          title: title.trim(),
          memberIds: selected.map((u) => u.id),
          ...(description.trim() ? { description: description.trim() } : {}),
        });
      } else {
        if (!title.trim()) {
          setError('Give the channel a title.');
          return;
        }
        if (isPublic && !/^[a-z0-9_]{5,32}$/i.test(username.trim())) {
          setError('Public channels need a username (5-32 chars: letters, digits, _).');
          return;
        }
        chat = await chatApi.createChannel({
          title: title.trim(),
          subscriberIds: selected.map((u) => u.id),
          isPublic,
          username: isPublic ? username.trim().toLowerCase() : undefined,
        });
      }
      await queryClient.invalidateQueries({ queryKey: ['chats'] });
      onCreated(chat.id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Could not create the conversation.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="dialog-backdrop" onClick={onClose} role="presentation">
      <div
        className="dialog"
        role="dialog"
        aria-modal="true"
        aria-label="New conversation"
        onClick={(e) => e.stopPropagation()}
      >
        <h2>New conversation</h2>
        <div className="folder-tabs" role="tablist">
          {(['dm', 'group', 'channel'] as Tab[]).map((t) => (
            <button
              key={t}
              role="tab"
              aria-selected={tab === t}
              className={`folder-tab${tab === t ? ' active' : ''}`}
              onClick={() => {
                setTab(t);
                setSelected([]);
                setError(null);
              }}
            >
              {t === 'dm' ? 'Direct' : t === 'group' ? 'Group' : 'Channel'}
            </button>
          ))}
        </div>

        {tab !== 'dm' ? (
          <div className="field">
            <label htmlFor="newchat-title">Title</label>
            <input
              id="newchat-title"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              maxLength={64}
              placeholder={tab === 'group' ? 'Group name' : 'Channel name'}
            />
          </div>
        ) : null}

        {tab === 'group' ? (
          <div className="field">
            <label htmlFor="newchat-desc">Description (optional)</label>
            <input
              id="newchat-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              maxLength={512}
              placeholder="What is this group about?"
            />
          </div>
        ) : null}

        {tab === 'channel' ? (
          <>
            <label className="check">
              <input type="checkbox" checked={isPublic} onChange={(e) => setIsPublic(e.target.checked)} />
              Public channel (joinable by username)
            </label>
            {isPublic ? (
              <div className="field">
                <label htmlFor="newchat-username">Username</label>
                <input
                  id="newchat-username"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  maxLength={32}
                  placeholder="e.g. opennews"
                />
              </div>
            ) : null}
          </>
        ) : null}

        <div className="field">
          <label htmlFor="newchat-search">
            {tab === 'dm' ? 'Find a person' : tab === 'group' ? 'Add members' : 'Add subscribers (optional)'}
          </label>
          <input
            id="newchat-search"
            autoFocus
            value={search}
            onChange={(e) => void onSearch(e.target.value)}
            placeholder="Type at least 2 characters…"
          />
        </div>

        {selected.length > 0 ? (
          <div className="chip-row">
            {selected.map((u) => (
              <button key={u.id} className="chip" onClick={() => toggle(u)} title="Remove">
                {u.displayName} ×
              </button>
            ))}
          </div>
        ) : null}

        <div className="picker-list">
          {results.map((u) => (
            <button
              key={u.id}
              className={`picker-row${selected.some((s) => s.id === u.id) ? ' active' : ''}`}
              onClick={() => {
                if (tab === 'dm') setSelected([u]);
                else toggle(u);
              }}
            >
              <strong>{u.displayName}</strong>
              <span className="hint">@{u.username}</span>
            </button>
          ))}
        </div>

        {error ? <div className="form-error">{error}</div> : null}
        <div className="row-actions">
          <button className="ghost-btn inline" onClick={onClose}>
            Cancel
          </button>
          <button className="primary-btn inline" onClick={() => void create()} disabled={busy}>
            {busy ? 'Creating…' : 'Create'}
          </button>
        </div>
      </div>
    </div>
  );
}
