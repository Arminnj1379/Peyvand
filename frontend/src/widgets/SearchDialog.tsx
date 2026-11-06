import { useEffect, useRef, useState } from 'react';
import { chatApi } from '../features/chats/chatApi';
import { chatDisplayName } from '../features/chats/chatTypes';
import { searchApi } from '../features/search/searchApi';
import type { DirectoryUser } from '../features/search/searchApi';
import type { Chat } from '../features/chats/chatTypes';
import type { Message } from '../features/messages/messageTypes';
import { ApiError } from '../shared/api/client';
import { useEscape } from '../shared/ui/useEscape';

type Tab = 'messages' | 'chats' | 'people';

const TABS: { key: Tab; label: string }[] = [
  { key: 'messages', label: 'Messages' },
  { key: 'chats', label: 'Chats' },
  { key: 'people', label: 'People' },
];

/**
 * Global search (Ctrl+K): server-side message, conversation, and directory
 * search. Enter opens the top result; Escape closes.
 */
export function SearchDialog({
  myUserId,
  onOpenChat,
  onClose,
}: {
  myUserId: string | undefined;
  onOpenChat: (chatId: string) => void;
  onClose: () => void;
}) {
  const [query, setQuery] = useState('');
  const [tab, setTab] = useState<Tab>('messages');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [chats, setChats] = useState<Chat[]>([]);
  const [people, setPeople] = useState<DirectoryUser[]>([]);
  const [searched, setSearched] = useState(false);
  const closeRef = useRef(onClose);
  closeRef.current = onClose;
  useEscape(true, () => closeRef.current());

  useEffect(() => {
    const q = query.trim();
    if (q.length < 2) {
      setMessages([]);
      setChats([]);
      setPeople([]);
      setSearched(false);
      setBusy(false);
      return;
    }
    setBusy(true);
    setError(null);
    const controller = new AbortController();
    const t = window.setTimeout(() => {
      const run = async () => {
        try {
          if (tab === 'messages') setMessages(await searchApi.messages(q, 20, controller.signal));
          else if (tab === 'chats') setChats(await searchApi.chats(q, 20, controller.signal));
          else setPeople(await searchApi.people(q, 10, controller.signal));
          setSearched(true);
        } catch (e) {
          if (e instanceof ApiError && e.status === 0) return; // aborted
          setError(e instanceof ApiError ? e.message : 'Search failed.');
        } finally {
          setBusy(false);
        }
      };
      void run();
    }, 300);
    return () => {
      window.clearTimeout(t);
      controller.abort();
    };
  }, [query, tab]);

  async function openDm(userId: string): Promise<void> {
    try {
      const chat = await chatApi.createDm(userId);
      onOpenChat(chat.id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Could not open conversation.');
    }
  }

  function openTop(): void {
    if (tab === 'messages' && messages[0]) onOpenChat(messages[0].chatId);
    else if (tab === 'chats' && chats[0]) onOpenChat(chats[0].id);
    else if (tab === 'people' && people[0]) void openDm(people[0].id);
  }

  return (
    <div className="dialog-backdrop" onClick={onClose} role="presentation">
      <div
        className="dialog search-dialog"
        role="dialog"
        aria-modal="true"
        aria-label="Search messages, chats, and people"
        onClick={(e) => e.stopPropagation()}
      >
        <input
          autoFocus
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') openTop();
          }}
          placeholder="Search messages, chats, people… (min 2 chars)"
          aria-label="Search query"
          maxLength={200}
        />
        <nav className="folder-tabs" aria-label="Search scope">
          {TABS.map((t) => (
            <button
              key={t.key}
              className={`folder-tab${tab === t.key ? ' active' : ''}`}
              onClick={() => setTab(t.key)}
              aria-pressed={tab === t.key}
            >
              {t.label}
            </button>
          ))}
        </nav>
        <div className="search-results" role="listbox" aria-label="Search results">
          {busy ? <p className="hint">Searching…</p> : null}
          {error ? <div className="form-error">{error}</div> : null}
          {!busy && !error && searched && tab === 'messages' && messages.length === 0 ? (
            <p className="hint">No messages found.</p>
          ) : null}
          {!busy && !error && searched && tab === 'chats' && chats.length === 0 ? (
            <p className="hint">No conversations found.</p>
          ) : null}
          {!busy && !error && searched && tab === 'people' && people.length === 0 ? (
            <p className="hint">Nobody found.</p>
          ) : null}
          {tab === 'messages'
            ? messages.map((m) => (
                <button key={m.id} className="picker-row" role="option" aria-selected="false" onClick={() => onOpenChat(m.chatId)}>
                  <strong>{m.senderName ?? m.senderUsername ?? 'Unknown'}</strong>
                  <span className="hint">{m.isDeleted ? 'Message deleted' : m.body.slice(0, 120)}</span>
                </button>
              ))
            : null}
          {tab === 'chats'
            ? chats.map((c) => (
                <button key={c.id} className="picker-row" role="option" aria-selected="false" onClick={() => onOpenChat(c.id)}>
                  <strong>{chatDisplayName(c, myUserId)}</strong>
                  <span className="hint">{c.type}</span>
                </button>
              ))
            : null}
          {tab === 'people'
            ? people.map((p) => (
                <button key={p.id} className="picker-row" role="option" aria-selected="false" onClick={() => void openDm(p.id)}>
                  <strong>{p.displayName}</strong>
                  <span className="hint">@{p.username}</span>
                </button>
              ))
            : null}
        </div>
      </div>
    </div>
  );
}
