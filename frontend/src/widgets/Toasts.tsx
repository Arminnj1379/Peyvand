import { useEffect, useRef, useState } from 'react';
import { Icon } from '../shared/ui/Icon';
import { useT } from '../shared/i18n';

export interface Toast {
  id: number;
  title: string;
  preview: string;
  chatId: string;
}

/** Bottom-corner toasts for messages arriving outside the open chat. */
export function Toasts({ toasts, onOpen, onDismiss }: {
  toasts: Toast[];
  onOpen: (chatId: string) => void;
  onDismiss: (id: number) => void;
}) {
  const t = useT();
  const [leaving, setLeaving] = useState<ReadonlySet<number>>(new Set());
  const timers = useRef<Map<number, number>>(new Map());
  useEffect(() => () => {
    for (const timer of timers.current.values()) window.clearTimeout(timer);
  }, []);

  function dismissWithExit(id: number): void {
    if (leaving.has(id)) return;
    setLeaving((prev) => new Set(prev).add(id));
    const timer = window.setTimeout(() => {
      timers.current.delete(id);
      onDismiss(id);
    }, 180);
    timers.current.set(id, timer);
  }

  // Parent removes toasts after timeout; drop their leaving flags so
  // re-used ids don't start in the exit state.
  useEffect(() => {
    setLeaving((prev) => {
      if (prev.size === 0) return prev;
      const ids = new Set(toasts.map((toast) => toast.id));
      let changed = false;
      const next = new Set<number>();
      for (const id of prev) {
        if (ids.has(id)) next.add(id);
        else changed = true;
      }
      return changed ? next : prev;
    });
  }, [toasts]);

  if (toasts.length === 0) return null;
  return (
    <div className="toast-stack" role="status" aria-live="polite">
      {toasts.map((toast) => (
        <div key={toast.id} className={`toast${leaving.has(toast.id) ? ' leaving' : ''}`}>
          <button
            className="toast-main"
            onClick={() => {
              dismissWithExit(toast.id);
              onOpen(toast.chatId);
            }}
          >
            <strong>{toast.title}</strong>
            <span className="hint">{toast.preview}</span>
          </button>
          <button
            className="icon-btn"
            aria-label={t('toast.dismiss')}
            onClick={() => dismissWithExit(toast.id)}
          >
            <Icon name="close" size={14} />
          </button>
        </div>
      ))}
    </div>
  );
}
