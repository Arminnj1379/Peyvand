import { useEffect, useRef, useState } from 'react';
import { notificationApi } from '../features/notifications/notificationApi';
import type { NotificationItem } from '../features/notifications/notificationApi';
import { useSettingsStore } from '../features/settings/settingsStore';
import { ApiError } from '../shared/api/client';
import { Icon } from '../shared/ui/Icon';
import { useEscape } from '../shared/ui/useEscape';
import { useOverlayTransition } from '../shared/motion/useOverlayTransition';
import { useT } from '../shared/i18n';

/** Inbox bell: unread total badge + dropdown of unread conversations. */
export function NotificationBell({
  totalUnread,
  onOpenChat,
}: {
  totalUnread: number;
  onOpenChat: (chatId: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<NotificationItem[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const notificationsEnabled = useSettingsStore((s) => s.notificationsEnabled);
  const wrapRef = useRef<HTMLDivElement | null>(null);
  const bellTransition = useOverlayTransition(open, 150);
  const t = useT();
  useEscape(open, () => setOpen(false));

  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    setBusy(true);
    setError(null);
    notificationApi
      .list(20, controller.signal)
      .then(setItems)
      .catch((e: unknown) => {
        if (e instanceof ApiError && e.status === 0) return;
        setError(e instanceof ApiError ? e.message : 'Could not load notifications.');
      })
      .finally(() => setBusy(false));
    return () => controller.abort();
  }, [open ]);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) setOpen(false);
    };
    window.addEventListener('mousedown', onDown);
    return () => window.removeEventListener('mousedown', onDown);
  }, [open ]);

  return (
    <div className="bell-wrap" ref={wrapRef}>
      <button
        className="icon-btn bell-btn"
        aria-label={totalUnread > 0 ? t('notif.bellWithUnread', { count: totalUnread }) : t('notif.bell')}
        aria-expanded={open}
        aria-haspopup="menu"
        title={t('notif.bell')}
        onClick={() => setOpen((v) => !v)}
      >
        <Icon name="bell" />
        {totalUnread > 0 ? (
          <span key={totalUnread} className="bell-badge">
            {totalUnread > 99 ? '99+' : totalUnread}
          </span>
        ) : null}
      </button>
      {bellTransition.shouldRender ? (
        <div
          className={`bell-menu${bellTransition.leaving ? ' leaving' : ''}`}
          role="menu"
          aria-label={t('notif.unreadConversations')}
        >
          {!notificationsEnabled ? (
            <p className="hint bell-paused">{t('notif.paused')}</p>
          ) : null}
          {busy ? <p className="hint">{t('common.loading')}</p> : null}
          {error ? <div className="form-error">{error}</div> : null}
          {!busy && !error && items.length === 0 ? <p className="hint">{t('notif.allCaughtUp')}</p> : null}
          {items.map((n) => (
            <button
              key={n.chatId}
              className="picker-row"
              role="menuitem"
              onClick={() => {
                setOpen(false);
                onOpenChat(n.chatId);
              }}
            >
              <strong>
                {n.title}
                {n.isMuted ? (
                  <span className="mute-ic" title={t('chat.muted')}>
                    <Icon name="mute" size={12} />
                  </span>
                ) : null}
              </strong>
              <span className="hint">
                {t('notif.unreadCount', { count: n.unreadCount })}{n.preview ? ` · ${n.preview.slice(0, 80)}` : ''}
              </span>
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
