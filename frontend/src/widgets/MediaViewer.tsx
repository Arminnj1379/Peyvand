import { useEffect } from 'react';
import { saveAttachment, useAttachmentUrl } from '../features/files/fileApi';
import { formatSize } from '../features/files/fileTypes';
import type { Attachment } from '../features/files/fileTypes';
import type { Message } from '../features/messages/messageTypes';
import { Icon } from '../shared/ui/Icon';

/** Inline attachment rendering inside a bubble (images, video, audio, documents). */
export function AttachmentView({
  message,
  onOpenImage,
}: {
  message: Message;
  onOpenImage: (attachment: Attachment) => void;
}) {
  const attachment = message.attachment;
  const url = useAttachmentUrl(attachment?.fileId, attachment?.kind ?? '');

  if (!attachment || message.isDeleted) return null;

  if (attachment.kind === 'Image') {
    const isGif =
      attachment.contentType.toLowerCase() === 'image/gif' ||
      attachment.fileName.toLowerCase().endsWith('.gif');
    return (
      <button
        className="attachment-image"
        onClick={() => onOpenImage(attachment)}
        aria-label={`Open image ${attachment.fileName}`}
      >
        {url ? (
          <span className="attachment-img-wrap">
            <img src={url} alt={attachment.fileName} loading="lazy" />
            {isGif ? (
              <span className="gif-badge" aria-hidden="true">
                GIF
              </span>
            ) : null}
          </span>
        ) : (
          <span className="attachment-loading">Loading image…</span>
        )}
      </button>
    );
  }

  if (attachment.kind === 'Video') {
    return url ? (
      <span className="attachment-media-wrap">
        {/* Direct blob playback in-app: no external player, seek via preloaded blob. */}
        <video
          className="attachment-video"
          src={url}
          controls
          preload="metadata"
          playsInline
          aria-label={attachment.fileName}
        />
      </span>
    ) : (
      <span className="attachment-loading">Loading video…</span>
    );
  }

  if (attachment.kind === 'Audio') {
    const isVoice =
      attachment.fileName.toLowerCase().startsWith('voice-') ||
      attachment.fileName.toLowerCase().endsWith('.weba');
    return url ? (
      <span className="attachment-audio-wrap">
        <span className="attachment-audio-name" dir="auto" title={attachment.fileName}>
          {isVoice ? '🎤' : '🎵'} {attachment.fileName}
        </span>
        <audio
          className="attachment-audio"
          src={url}
          controls
          preload="metadata"
          aria-label={attachment.fileName}
        />
      </span>
    ) : (
      <span className="attachment-loading">Loading audio…</span>
    );
  }

  return (
    <div className="attachment-doc">
      <Icon name="file" size={28} />
      <span className="attachment-doc-meta">
        <strong>{attachment.fileName}</strong>
        <span className="hint">{formatSize(attachment.sizeBytes)}</span>
      </span>
      <button
        className="icon-btn"
        aria-label={`Download ${attachment.fileName}`}
        title="Download"
        onClick={() => void saveAttachment(attachment.fileId, attachment.fileName).catch(() => undefined)}
      >
        <Icon name="download" size={18} />
      </button>
    </div>
  );
}

/** Fullscreen image viewer (lightbox). Videos play inline; audio too. */
export function MediaViewer({
  attachment,
  onClose,
}: {
  attachment: Attachment;
  onClose: () => void;
}) {
  const url = useAttachmentUrl(attachment.fileId, attachment.kind);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="viewer-backdrop" onClick={onClose} role="presentation">
      <div
        className="viewer"
        role="dialog"
        aria-modal="true"
        aria-label={attachment.fileName}
        onClick={(e) => e.stopPropagation()}
      >
        <header className="viewer-head">
          <span className="viewer-name">{attachment.fileName}</span>
          <span className="viewer-actions">
            <button
              className="icon-btn"
              aria-label="Download"
              title="Download"
              onClick={() => void saveAttachment(attachment.fileId, attachment.fileName).catch(() => undefined)}
            >
              <Icon name="download" />
            </button>
            <button className="icon-btn" aria-label="Close viewer" onClick={onClose}>
              <Icon name="close" />
            </button>
          </span>
        </header>
        {url ? (
          <img className="viewer-image" src={url} alt={attachment.fileName} />
        ) : (
          <p className="hint">Loading…</p>
        )}
      </div>
    </div>
  );
}
