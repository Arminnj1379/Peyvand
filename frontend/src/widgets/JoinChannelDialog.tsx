import { useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { chatApi } from '../features/chats/chatApi';
import type { PublicChannel } from '../features/chats/chatTypes';
import { ApiError } from '../shared/api/client';
import { useT } from '../shared/i18n';
import { useEscape } from '../shared/ui/useEscape';

/** Join a public channel by username, or any group/channel via invite code. */
export function JoinChannelDialog({
  onClose,
  onJoined,
  leaving,
}: {
  onClose: () => void;
  onJoined: (chatId: string) => void;
  leaving?: boolean;
}) {
  const queryClient = useQueryClient();
  const t = useT();
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
      setError(t('joinchannel.errUsername'));
      return;
    }
    setBusy(true);
    try {
      setPreview(await chatApi.publicChannel(username.trim()));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('joinchannel.errNotFound'));
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
      setError(e instanceof ApiError ? e.message : t('joinchannel.errSubscribe'));
    } finally {
      setBusy(false);
    }
  }

  async function joinWithCode(): Promise<void> {
    setError(null);
    if (!code.trim()) {
      setError(t('joinchannel.errCode'));
      return;
    }
    setBusy(true);
    try {
      const chat = await chatApi.join(code.trim());
      await queryClient.invalidateQueries({ queryKey: ['chats'] });
      onJoined(chat.id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('joinchannel.errInvalid'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div
      className={`dialog-backdrop${leaving ? ' leaving' : ''}`}
      onClick={onClose}
      role="presentation"
    >
      <div
        className="dialog"
        role="dialog"
        aria-modal="true"
        aria-label={t('joinchannel.dialogLabel')}
        onClick={(e) => e.stopPropagation()}
      >
        <h2>{t('joinchannel.title')}</h2>
        <div className="field">
          <label htmlFor="join-username">{t('joinchannel.usernameLabel')}</label>
          <input
            id="join-username"
            autoFocus
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            placeholder={t('joinchannel.usernamePh')}
          />
        </div>
        <div className="row-actions">
          <button className="ghost-btn inline" onClick={() => void lookup()} disabled={busy}>
            {t('joinchannel.lookup')}
          </button>
        </div>
        {preview ? (
          <div className="picker-list">
            <div className="picker-row">
              <strong>{preview.title}</strong>
              <span className="hint">@{preview.username} · {t('joinchannel.subscribers', { count: preview.memberCount })}</span>
              <button className="primary-btn inline" onClick={() => void subscribe(preview.id)} disabled={busy}>
                {t('joinchannel.subscribe')}
              </button>
            </div>
          </div>
        ) : null}

        <h2 style={{ marginTop: 16 }}>{t('joinchannel.inviteTitle')}</h2>
        <div className="field">
          <label htmlFor="join-code">{t('joinchannel.codeLabel')}</label>
          <input
            id="join-code"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            placeholder={t('joinchannel.codePh')}
          />
        </div>
        <div className="row-actions">
          <button className="ghost-btn inline" onClick={() => void joinWithCode()} disabled={busy}>
            {t('joinchannel.join')}
          </button>
        </div>

        {error ? <div className="form-error">{error}</div> : null}
        <div className="row-actions">
          <button className="ghost-btn inline" onClick={onClose}>
            {t('common.close')}
          </button>
        </div>
      </div>
    </div>
  );
}
