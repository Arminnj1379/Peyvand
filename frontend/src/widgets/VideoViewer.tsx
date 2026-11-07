import { useCallback, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { saveAttachment, useAttachmentUrl } from '../features/files/fileApi';
import type { Attachment } from '../features/files/fileTypes';
import { useT } from '../shared/i18n';
import { Icon } from '../shared/ui/Icon';
import { formatMediaTime, pauseOtherPlayers, seekFromPointer } from './players';

const SPEEDS = [1, 1.5, 2, 0.5];
const IDLE_MS = 2500;

function nextSpeed(cur: number): number {
  const i = SPEEDS.indexOf(cur);
  return SPEEDS[(i + 1) % SPEEDS.length] ?? 1;
}

/** Telegram-style fullscreen video viewer (portal above all app layers). */
export function VideoViewer({
  attachment,
  onClose,
  leaving,
}: {
  attachment: Attachment;
  onClose: () => void;
  leaving?: boolean;
}) {
  const t = useT();
  const url = useAttachmentUrl(attachment.fileId, attachment.kind);
  const fullRef = useRef<HTMLDivElement | null>(null);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const seekRef = useRef<HTMLDivElement | null>(null);
  const volRef = useRef<HTMLDivElement | null>(null);
  const idleTimer = useRef<number | null>(null);
  const [playing, setPlaying] = useState(false);
  const [time, setTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const [volume, setVolume] = useState(1);
  const [muted, setMuted] = useState(false);
  const [speed, setSpeed] = useState(1);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const [idle, setIdle] = useState(false);

  const progress = duration > 0 ? Math.min(1, time / duration) : 0;

  // rAF-smooth clock while playing; onTimeUpdate stays as a fallback.
  useEffect(() => {
    if (!playing) return;
    let raf = 0;
    const tick = () => {
      const v = videoRef.current;
      if (v && Number.isFinite(v.currentTime)) setTime(v.currentTime);
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [playing]);

  // Lock background scroll; unmount (close) restores it and halts playback.
  useEffect(() => {
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = prev;
      videoRef.current?.pause();
      if (idleTimer.current) window.clearTimeout(idleTimer.current);
    };
  }, []);

  // Escape closes; backdrop click closes; video/controls stop propagation.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  // Track native fullscreen changes (user may press Esc while fullscreen).
  useEffect(() => {
    const onFs = () => setIsFullscreen(document.fullscreenElement != null);
    document.addEventListener('fullscreenchange', onFs);
    return () => document.removeEventListener('fullscreenchange', onFs);
  }, []);

  // Autoplay on open / once the blob URL resolves; yields to single-playback.
  useEffect(() => {
    const v = videoRef.current;
    if (!v || !url || failed) return;
    pauseOtherPlayers(v);
    void v.play().catch(() => undefined);
  }, [url, failed, reloadKey]);

  // Chrome auto-hide: arm on play, disarm on pause, re-arm on interaction.
  const poke = useCallback(() => {
    setIdle(false);
    if (idleTimer.current) window.clearTimeout(idleTimer.current);
    idleTimer.current = null;
    const v = videoRef.current;
    if (v && !v.paused && !v.ended) {
      idleTimer.current = window.setTimeout(() => setIdle(true), IDLE_MS);
    }
  }, []);

  function toggle(): void {
    const v = videoRef.current;
    if (!v || failed) return;
    if (v.paused || v.ended) {
      if (v.ended) {
        try {
          v.currentTime = 0;
        } catch {
          // Non-seekable stream: replay from the start on next load.
        }
        setTime(0);
      }
      pauseOtherPlayers(v);
      void v.play().catch(() => undefined);
    } else {
      v.pause();
    }
  }

  function seekTo(ratio: number): void {
    const v = videoRef.current;
    if (!v || !Number.isFinite(v.duration) || v.duration <= 0) return;
    v.currentTime = Math.min(1, Math.max(0, ratio)) * v.duration;
  }

  function setVolumeTo(ratio: number): void {
    const v = videoRef.current;
    const clamped = Math.min(1, Math.max(0, ratio));
    setVolume(clamped);
    if (!v) return;
    v.volume = clamped;
    if (clamped > 0 && v.muted) {
      v.muted = false;
      setMuted(false);
    }
  }

  function toggleMuted(): void {
    const v = videoRef.current;
    if (!v) return;
    v.muted = !v.muted;
    setMuted(v.muted);
  }

  function cycleSpeed(): void {
    const v = videoRef.current;
    const next = nextSpeed(speed);
    setSpeed(next);
    if (v) {
      try {
        v.playbackRate = next;
      } catch {
        // Older engines ignore playbackRate; the label still reflects intent.
      }
    }
  }

  function toggleFullscreen(): void {
    const el = fullRef.current;
    if (!el) return;
    if (document.fullscreenElement) {
      void document.exitFullscreen().catch(() => undefined);
      return;
    }
    if (el.requestFullscreen) void el.requestFullscreen().catch(() => undefined);
  }

  function retry(): void {
    setFailed(false);
    setLoading(true);
    setTime(0);
    setReloadKey((k) => k + 1);
  }

  return createPortal(
    <div
      className={`vv-backdrop${leaving ? ' leaving' : ''}${idle ? ' idle' : ''}`}
      onClick={onClose}
      onMouseMove={poke}
      onTouchStart={poke}
      role="presentation"
    >
      <div ref={fullRef} className="vv-full" onClick={(e) => e.stopPropagation()} role="presentation">
        <header className="vv-chrome vv-top">
          <span className="vv-name" dir="auto">
            {attachment.fileName}
          </span>
          <span className="vv-actions">
            <button
              type="button"
              className="icon-btn"
              onClick={() => void saveAttachment(attachment.fileId, attachment.fileName).catch(() => undefined)}
              aria-label={t('viewer.download')}
              title={t('viewer.download')}
            >
              <Icon name="download" />
            </button>
            <button
              type="button"
              className="icon-btn vv-close"
              onClick={onClose}
              aria-label={t('viewer.closeViewer')}
              title={t('viewer.closeViewer')}
              autoFocus
            >
              <Icon name="close" />
            </button>
          </span>
        </header>

        <div className="vv-stage">
          <div className="vv-frame">
            {url ? (
              <video
                key={reloadKey}
                ref={videoRef}
                className="vv-video"
                data-tg-media
                src={url}
                preload="auto"
                playsInline
                aria-label={attachment.fileName}
                onClick={toggle}
                onPlay={() => {
                  pauseOtherPlayers(videoRef.current);
                  setPlaying(true);
                  poke();
                }}
                onPause={() => {
                  setPlaying(false);
                  poke();
                }}
                onEnded={(e) => {
                  setPlaying(false);
                  setTime(0);
                  try {
                    e.currentTarget.currentTime = 0;
                  } catch {
                    // Leave the element alone when the stream is not seekable.
                  }
                  poke();
                }}
                onTimeUpdate={(e) => setTime(e.currentTarget.currentTime)}
                onWaiting={() => setLoading(true)}
                onPlaying={() => setLoading(false)}
                onCanPlay={() => setLoading(false)}
                onError={() => {
                  setLoading(false);
                  setFailed(true);
                }}
                onLoadedMetadata={(e) => {
                  setDuration(Number.isFinite(e.currentTarget.duration) ? e.currentTarget.duration : 0);
                  setTime(e.currentTarget.currentTime);
                  setMuted(e.currentTarget.muted);
                  setLoading(false);
                }}
              />
            ) : null}
            {loading && !failed ? (
              <span className="vv-spinner" role="status" aria-label={t('viewer.loading')} />
            ) : null}
            {failed ? (
              <div className="vv-error" role="alert">
                <p className="vv-errmsg">{t('player.videoError')}</p>
                <button type="button" className="vv-retry" onClick={retry}>
                  {t('common.retry')}
                </button>
              </div>
            ) : null}
            {!playing && !loading && !failed && url ? (
              <button
                type="button"
                className="vv-bigplay"
                onClick={toggle}
                aria-label={t('player.playVideo')}
              >
                <Icon name="play" size={40} />
              </button>
            ) : null}
          </div>
        </div>

        <div className="vv-chrome vv-controls">
          <div
            ref={seekRef}
            className="vv-seek"
            role="slider"
            aria-label={t('player.seekVideo')}
            aria-valuemin={0}
            aria-valuemax={Math.round(duration)}
            aria-valuenow={Math.round(time)}
            tabIndex={0}
            onClick={(e) => {
              poke();
              seekFromPointer(e, seekRef.current, seekTo);
            }}
            onKeyDown={(e) => {
              const rtl = seekRef.current
                ? getComputedStyle(seekRef.current).direction === 'rtl'
                : false;
              const fwd = e.key === (rtl ? 'ArrowLeft' : 'ArrowRight');
              const back = e.key === (rtl ? 'ArrowRight' : 'ArrowLeft');
              if (fwd || back || e.key === 'Home' || e.key === 'End') e.preventDefault();
              else if (e.key !== ' ') return;
              if (e.key === 'Home') seekTo(0);
              else if (e.key === 'End') seekTo(1);
              else if (e.key === ' ') toggle();
              else if (fwd) seekTo(Math.min(1, (time + 5) / Math.max(1, duration)));
              else if (back) seekTo(Math.max(0, (time - 5) / Math.max(1, duration)));
            }}
          >
            <span className="vv-seek-fill" style={{ width: `${progress * 100}%` }} />
            <span className="vv-seek-knob" style={{ insetInlineStart: `${progress * 100}%` }} />
          </div>
          <div className="vv-row">
            <button
              type="button"
              className="vv-btn"
              onClick={() => {
                poke();
                toggle();
              }}
              aria-label={playing ? t('player.pauseVideo') : t('player.playVideo')}
              title={playing ? t('player.pauseVideo') : t('player.playVideo')}
            >
              <Icon name={playing ? 'pause' : 'play'} size={20} />
            </button>
            <span className="vv-time">
              {formatMediaTime(time)} / {formatMediaTime(duration)}
            </span>
            <span className="vv-spacer" />
            <span className="vv-vol">
              <button
                type="button"
                className="vv-btn"
                onClick={toggleMuted}
                aria-label={muted || volume === 0 ? t('player.unmute') : t('player.mute')}
                title={muted || volume === 0 ? t('player.unmute') : t('player.mute')}
              >
                <Icon name={muted || volume === 0 ? 'volumeOff' : 'volume'} size={18} />
              </button>
              <span
                ref={volRef}
                className="vv-vol-slider"
                role="slider"
                aria-label={t('player.volume')}
                aria-valuemin={0}
                aria-valuemax={100}
                aria-valuenow={Math.round((muted ? 0 : volume) * 100)}
                tabIndex={0}
                onClick={(e) => seekFromPointer(e, volRef.current, setVolumeTo)}
                onKeyDown={(e) => {
                  if (e.key === 'ArrowRight' || e.key === 'ArrowUp') {
                    e.preventDefault();
                    setVolumeTo(volume + 0.1);
                  } else if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') {
                    e.preventDefault();
                    setVolumeTo(volume - 0.1);
                  }
                }}
              >
                <span
                  className="vv-vol-fill"
                  style={{ width: `${(muted ? 0 : volume) * 100}%` }}
                />
              </span>
            </span>
            <button
              type="button"
              className="vv-speed"
              onClick={cycleSpeed}
              aria-label={t('player.speed')}
              title={t('player.speed')}
            >
              {speed}×
            </button>
            <button
              type="button"
              className="vv-btn"
              onClick={toggleFullscreen}
              aria-label={isFullscreen ? t('player.exitFullscreen') : t('player.fullscreen')}
              title={isFullscreen ? t('player.exitFullscreen') : t('player.fullscreen')}
            >
              <Icon name={isFullscreen ? 'compress' : 'expand'} size={18} />
            </button>
          </div>
        </div>
      </div>
    </div>,
    document.body,
  );
}
