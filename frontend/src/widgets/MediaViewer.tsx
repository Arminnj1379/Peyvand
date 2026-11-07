import { useEffect } from 'react';
import { saveAttachment, useAttachmentUrl } from '../features/files/fileApi';
import { formatSize } from '../features/files/fileTypes';
import type { Attachment } from '../features/files/fileTypes';
import type { Message } from '../features/messages/messageTypes';
import { Icon } from '../shared/ui/Icon';
import { useT } from '../shared/i18n';
import { MusicPlayer, VideoPreview, VoicePlayer } from './players';

/** Inline attachment rendering inside a bubble (images, video, audio, documents). */
export function AttachmentView({
  message,
  onOpenImage,
  onOpenVideo,
}: {
  message: Message;
  onOpenImage: (attachment: Attachment) => void;
  onOpenVideo: (attachment: Attachment) => void;
}) {
  const attachment = message.attachment;
  const url = useAttachmentUrl(attachment?.fileId, attachment?.kind ?? '');
  const t = useT();

  if (!attachment || message.isDeleted) return null;

  if (attachment.kind === 'Image') {
    const isGif =
      attachment.contentType.toLowerCase() === 'image/gif' ||
      attachment.fileName.toLowerCase().endsWith('.gif');
    return (
      <button
        className="attachment-image"
        onClick={() => onOpenImage(attachment)}
        aria-label={t('viewer.openImage', { name: attachment.fileName })}
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
          <span className="attachment-loading">{t('viewer.loadingImage')}</span>
        )}
      </button>
    );
  }

  if (attachment.kind === 'Video') {
    return url ? (
      <span className="attachment-media-wrap">
        <VideoPreview url={url} fileName={attachment.fileName} onOpen={() => onOpenVideo(attachment)} />
      </span>
    ) : (
      <span className="attachment-loading">{t('viewer.loadingVideo')}</span>
    );
  }

  if (attachment.kind === 'Audio') {
    const isVoice =
      attachment.fileName.toLowerCase().startsWith('voice-') ||
      attachment.fileName.toLowerCase().endsWith('.weba');
    return url ? (
      isVoice ? (
        <VoicePlayer url={url} fileId={attachment.fileId} />
      ) : (
        <MusicPlayer url={url} fileName={attachment.fileName} />
      )
    ) : (
      <span className="attachment-loading">{t('viewer.loadingAudio')}</span>
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
        aria-label={t('viewer.downloadFile', { name: attachment.fileName })}
        title={t('viewer.download')}
        onClick={() => void saveAttachment(attachment.fileId, attachment.fileName).catch(() => undefined)}
      >
        <Icon name="download" size={18} />
      </button>
    </div>
  );
}

/** Fullscreen image viewer (lightbox). Videos open in VideoViewer; audio plays inline. */
export function MediaViewer({
  attachment,
  onClose,
  leaving,
}: {
  attachment: Attachment;
  onClose: () => void;
  leaving?: boolean;
}) {
  const url = useAttachmentUrl(attachment.fileId, attachment.kind);
  const t = useT();

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div
      className={`viewer-backdrop${leaving ? ' leaving' : ''}`}
      onClick={onClose}
      role="presentation"
    >
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
              aria-label={t('viewer.download')}
              title={t('viewer.download')}
              onClick={() => void saveAttachment(attachment.fileId, attachment.fileName).catch(() => undefined)}
            >
              <Icon name="download" />
            </button>
            <button className="icon-btn" aria-label={t('viewer.closeViewer')} onClick={onClose}>
              <Icon name="close" />
            </button>
          </span>
        </header>
        {url ? (
          <img className="viewer-image" src={url} alt={attachment.fileName} />
        ) : (
          <p className="hint">{t('viewer.loading')}</p>
        )}
      </div>
    </div>
  );
}
