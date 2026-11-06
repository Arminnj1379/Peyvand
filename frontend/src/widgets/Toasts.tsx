import { Icon } from '../shared/ui/Icon';

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
  if (toasts.length === 0) return null;
  return (
    <div className="toast-stack" role="status" aria-live="polite">
      {toasts.map((t) => (
        <div key={t.id} className="toast">
          <button
            className="toast-main"
            onClick={() => {
              onDismiss(t.id);
              onOpen(t.chatId);
            }}
          >
            <strong>{t.title}</strong>
            <span className="hint">{t.preview}</span>
          </button>
          <button className="icon-btn" aria-label="Dismiss notification" onClick={() => onDismiss(t.id)}>
            <Icon name="close" size={14} />
          </button>
        </div>
      ))}
    </div>
  );
}
