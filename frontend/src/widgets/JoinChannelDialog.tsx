import { useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { chatApi } from '../features/chats/chatApi';
import type { PublicChannel } from '../features/chats/chatTypes';
import { ApiError } from '../shared/api/client';
import { useEscape } from '../shared/ui/useEscape';

/** Join a public channel by username, or any group/channel via invite code. */
export function JoinChannelDialog({
  onClose,
  onJoined,
}: {
  onClose: () => void;
  onJoined: (chatId: string) => void;
}) {
  const queryClient = useQueryClient();
  const closeRef = useRef(onClose);
  closeRef.current = onClose;
  useEscape(true, () => closeRef.current());
  const [username, setUsername] = useState('');
  const [code, setCode] = useState('');
  const [preview, setPreview] = useState<PublicChannel | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function lookup(): Promise<void> {
    setError(null);
    setPreview(null);
    if (!username.trim()) {
      setError('Enter a channel username.');
      return;
    }
    setBusy(true);
    try {
      setPreview(await chatApi.publicChannel(username.trim()));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Channel not found.');
    } finally {
      setBusy(false);
    }
  }

  async function subscribe(id: string): Promise<void> {
    setError(null);
    setBusy(true);
    try {
      const chat = await chatApi.subscribe(id);
      await queryClient.invalidateQueries({ queryKey: ['chats'] });
      onJoined(chat.id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Could not subscribe.');
    } finally {
      setBusy(false);
    }
  }

  async function joinWithCode(): Promise<void> {
    setError(null);
    if (!code.trim()) {
      setError('Paste an invite code.');
      return;
    }
    setBusy(true);
    try {
      const chat = await chatApi.join(code.trim());
      await queryClient.invalidateQueries({ queryKey: ['chats'] });
      onJoined(chat.id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Invalid or revoked invite link.');
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
        aria-label="Join a conversation"
        onClick={(e) => e.stopPropagation()}
      >
        <h2>Join a channel</h2>
        <div className="field">
          <label htmlFor="join-username">Public channel username</label>
          <input
            id="join-username"
            autoFocus
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            placeholder="e.g. opennews"
          />
        </div>
        <div className="row-actions">
          <button className="ghost-btn inline" onClick={() => void lookup()} disabled={busy}>
            Look up
          </button>
        </div>
        {preview ? (
          <div className="picker-list">
            <div className="picker-row">
              <strong>{preview.title}</strong>
              <span className="hint">@{preview.username} · {preview.memberCount} subscribers</span>
              <button className="primary-btn inline" onClick={() => void subscribe(preview.id)} disabled={busy}>
                Subscribe
              </button>
            </div>
          </div>
        ) : null}

        <h2 style={{ marginTop: 16 }}>Have an invite link?</h2>
        <div className="field">
          <label htmlFor="join-code">Invite code</label>
          <input
            id="join-code"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            placeholder="Paste the code…"
          />
        </div>
        <div className="row-actions">
          <button className="ghost-btn inline" onClick={() => void joinWithCode()} disabled={busy}>
            Join
          </button>
        </div>

        {error ? <div className="form-error">{error}</div> : null}
        <div className="row-actions">
          <button className="ghost-btn inline" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
