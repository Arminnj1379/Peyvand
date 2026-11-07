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
  if (toasts.length === 0) return null;
  return (
    <div className="toast-stack" role="status" aria-live="polite">
      {toasts.map((toast) => (
        <div key={toast.id} className="toast">
          <button
            className="toast-main"
            onClick={() => {
              onDismiss(toast.id);
              onOpen(toast.chatId);
            }}
          >
            <strong>{toast.title}</strong>
            <span className="hint">{toast.preview}</span>
          </button>
          <button className="icon-btn" aria-label={t('toast.dismiss')} onClick={() => onDismiss(toast.id)}>
            <Icon name="close" size={14} />
          </button>
        </div>
      ))}
    </div>
  );
}
