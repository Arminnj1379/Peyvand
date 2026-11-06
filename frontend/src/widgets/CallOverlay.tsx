import { useEffect, useState } from 'react';
import { callApi } from '../features/calls/callApi';
import { useCallStore } from '../features/calls/callStore';
import { formatCallDuration } from '../features/calls/callTypes';
import type { CallInfo } from '../features/calls/callTypes';
import { PeerTile, useCallRoom } from '../features/calls/useCallRoom';
import type { Chat } from '../features/chats/chatTypes';
import { chatDisplayName } from '../features/chats/chatTypes';
import { ApiError } from '../shared/api/client';
import { Icon } from '../shared/ui/Icon';

/** Resolves display names from chat members (server sends ids only). */
function namesFor(call: CallInfo, chats: Chat[], myUserId: string | undefined): Map<string, string> {
  const map = new Map<string, string>();
  const chat = chats.find((c) => c.id === call.chatId);
  for (const m of chat?.members ?? []) {
    map.set(m.userId, m.displayName || m.username || 'Guest');
  }
  if (myUserId && !map.has(myUserId)) map.set(myUserId, 'You');
  return map;
}

function IncomingCall({ chats, myUserId }: { chats: Chat[]; myUserId: string | undefined }) {
  const incoming = useCallStore((s) => s.incoming);
  const setActive = useCallStore((s) => s.setActive);
  const setIncoming = useCallStore((s) => s.setIncoming);
  const setError = useCallStore((s) => s.setError);
  const [busy, setBusy] = useState(false);

  if (!incoming || incoming.status === 'Ended') return null;

  const names = namesFor(incoming, chats, myUserId);
  const caller = names.get(incoming.initiatorId) ?? 'Someone';
  const chat = chats.find((c) => c.id === incoming.chatId);
  const chatName = chat ? chatDisplayName(chat, myUserId) : 'conversation';

  async function accept(): Promise<void> {
    setBusy(true);
    try {
      await callApi.accept(incoming!.id);
      const join = await callApi.join(incoming!.id);
      setActive(join.call, join);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Could not join the call.');
      setIncoming(null);
    } finally {
      setBusy(false);
    }
  }

  async function decline(): Promise<void> {
    setBusy(true);
    try {
      await callApi.reject(incoming!.id);
    } catch {
      // Ringing state resolves via the call:ended event either way.
    } finally {
      setIncoming(null);
      setBusy(false);
    }
  }

  return (
    <div className="call-incoming-backdrop" role="presentation">
      <div className="call-incoming" role="dialog" aria-modal="true" aria-label="Incoming call">
        <div className="call-incoming-avatar" aria-hidden="true">{caller.slice(0, 1).toUpperCase()}</div>
        <strong>{caller}</strong>
        <span className="hint">
          Incoming {incoming.mediaType === 'Video' ? 'video' : 'voice'} call · {chatName}
        </span>
        <div className="call-incoming-actions">
          <button className="call-btn decline" onClick={() => void decline()} disabled={busy} aria-label="Decline call">
            <Icon name="phoneDown" size={22} />
          </button>
          <button className="call-btn accept" onClick={() => void accept()} disabled={busy} aria-label="Accept call">
            <Icon name="phone" size={22} />
          </button>
        </div>
      </div>
    </div>
  );
}

function ActiveCall({ chats, myUserId }: { chats: Chat[]; myUserId: string | undefined }) {
  const active = useCallStore((s) => s.active);
  const join = useCallStore((s) => s.join);
  const setActive = useCallStore((s) => s.setActive);
  const callError = useCallStore((s) => s.error);
  const setError = useCallStore((s) => s.setError);
  const [elapsed, setElapsed] = useState(0);
  const [showPeople, setShowPeople] = useState(false);
  const isVideo = active?.mediaType === 'Video';
  const { peers, connected, muted, cameraOff, error, toggleMute, toggleCamera, disconnect } =
    useCallRoom(join, isVideo);

  // Elapsed clock from activation (server duration lands in history after end).
  useEffect(() => {
    if (active?.status !== 'Active') return;
    const t = window.setInterval(() => setElapsed((s) => s + 1), 1000);
    return () => window.clearInterval(t);
  }, [active?.status, active?.id]);

  useEffect(() => {
    setElapsed(0);
    setShowPeople(false);
  }, [active?.id]);

  if (!active || active.status === 'Ended' || !join) return null;

  const names = namesFor(active, chats, myUserId);
  const labeled = peers.map((p) => ({
    ...p,
    name: p.isLocal ? 'You' : (names.get(p.identity) ?? p.name),
  }));
  const status = active.status === 'Ringing' ? 'Ringing…' : formatCallDuration(elapsed);

  async function hangUp(endForAll: boolean): Promise<void> {
    disconnect();
    try {
      if (endForAll) await callApi.end(active!.id);
      else await callApi.leave(active!.id);
    } catch {
      // Media already dropped; server state reconciles via events/history.
    } finally {
      setActive(null);
    }
  }

  return (
    <div className="call-overlay" role="dialog" aria-modal="true" aria-label={isVideo ? 'Video call' : 'Voice call'}>
      <header className="call-head">
        <div>
          <strong>{isVideo ? 'Video call' : 'Voice call'}</strong>
          <span className="hint">
            {connected ? status : 'Connecting media…'}
            {error ? ` · ${error}` : null}
          </span>
        </div>
        <button className="icon-btn" aria-label="Participants" aria-pressed={showPeople} onClick={() => setShowPeople((v) => !v)}>
          <Icon name="users" />
        </button>
      </header>

      <div className={`peer-grid peers-${Math.min(Math.max(labeled.length, 1), 4)}`}>
        {labeled.length === 0 ? (
          <div className="peer-tile"><div className="peer-avatar">…</div></div>
        ) : null}
        {labeled.map((p) =>
          isVideo ? (
            <PeerTile key={p.identity} peer={p} />
          ) : (
            <div key={p.identity} className={`peer-tile audio${p.isSpeaking ? ' speaking' : ''}`}>
              <div className="peer-avatar" aria-hidden="true">
                {(p.name || '?').slice(0, 1).toUpperCase()}
              </div>
              <div className="peer-label">
                <span>{p.name}</span>
                {p.isMuted ? <span className="peer-flag">muted</span> : null}
              </div>
            </div>
          ),
        )}
      </div>

      {showPeople ? (
        <ul className="call-roster" aria-label="Call participants">
          {active.participants.map((p) => (
            <li key={p.userId}>
              <span>{p.userId === myUserId ? 'You' : (names.get(p.userId) ?? 'Guest')}</span>
              <span className="hint">
                {p.isJoined ? 'in call' : p.status === 'Rejected' ? 'declined' : p.status === 'Accepted' ? 'accepted' : 'ringing'}
              </span>
            </li>
          ))}
        </ul>
      ) : null}

      {callError ? <p className="form-error">{callError}</p> : null}

      <footer className="call-controls">
        <button
          className={`call-btn round${muted ? ' off' : ''}`}
          onClick={toggleMute}
          aria-label={muted ? 'Unmute microphone' : 'Mute microphone'}
          aria-pressed={muted}
          title={muted ? 'Unmute' : 'Mute'}
        >
          <Icon name={muted ? 'micOff' : 'mic'} size={20} />
        </button>
        {isVideo ? (
          <button
            className={`call-btn round${cameraOff ? ' off' : ''}`}
            onClick={toggleCamera}
            aria-label={cameraOff ? 'Turn camera on' : 'Turn camera off'}
            aria-pressed={cameraOff}
            title="Camera"
          >
            <Icon name="video" size={20} />
          </button>
        ) : null}
        <button
          className="call-btn round"
          onClick={() => void hangUp(false)}
          aria-label="Leave call"
          title="Leave (keeps the call up for others)"
        >
          <Icon name="back" size={20} />
        </button>
        <button
          className="call-btn decline"
          onClick={() => {
            setError(null);
            void hangUp(true);
          }}
          aria-label="End call for everyone"
          title="End for everyone"
        >
          <Icon name="phoneDown" size={22} />
        </button>
      </footer>
    </div>
  );
}

export function CallOverlay({ chats, myUserId }: { chats: Chat[]; myUserId: string | undefined }) {
  return (
    <>
      <IncomingCall chats={chats} myUserId={myUserId} />
      <ActiveCall chats={chats} myUserId={myUserId} />
    </>
  );
}
