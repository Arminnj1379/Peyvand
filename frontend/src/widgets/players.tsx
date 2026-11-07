import { useMemo, useRef, useState } from 'react';
import { Icon } from '../shared/ui/Icon';

/** Telegram-web-style inline players (music, voice, video). */

export function formatMediaTime(totalSeconds: number): string {
  if (!Number.isFinite(totalSeconds) || totalSeconds < 0) return '0:00';
  const m = Math.floor(totalSeconds / 60);
  const s = Math.floor(totalSeconds % 60);
  return `${m}:${s.toString().padStart(2, '0')}`;
}

/** Pause other bubble players so two tracks never overlap (call tiles excluded). */
function pauseOtherPlayers(except: HTMLMediaElement | null): void {
  document
    .querySelectorAll<HTMLMediaElement>('audio[data-tg-media], video[data-tg-media]')
    .forEach((el) => {
      if (el !== except && !el.paused) void el.pause();
    });
}

function prettyTitle(fileName: string): string {
  const base = fileName.split('/').pop() ?? fileName;
  const dot = base.lastIndexOf('.');
  const noExt = dot > 0 ? base.slice(0, dot) : base;
  return noExt.replace(/[_+]+/g, ' ').trim() || base;
}

/** Deterministic pseudo-waveform so every voice message has its own shape. */
function barsFor(seed: string, count = 44): number[] {
  let h = 2166136261;
  for (const c of seed) {
    h ^= c.charCodeAt(0);
    h = Math.imul(h, 16777619);
  }
  const rnd = () => {
    h = (Math.imul(h ^ (h >>> 15), 2246822519) >>> 0) || 1;
    h = (Math.imul(h ^ (h >>> 13), 3266489917) >>> 0) || 1;
    h ^= h >>> 16;
    return (h >>> 0) / 4294967296;
  };
  return Array.from({ length: count }, () => 0.22 + rnd() * 0.78);
}

function seekFromPointer(
  e: { clientX: number },
  el: HTMLElement | null,
  apply: (ratio: number) => void,
): void {
  if (!el) return;
  const rect = el.getBoundingClientRect();
  if (rect.width <= 0) return;
  const ratio = Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width));
  apply(ratio);
}

/* ── Music (audio file): round play button + title + seek bar ─────────── */

export function MusicPlayer({ url, fileName }: { url: string; fileName: string }) {
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const barRef = useRef<HTMLDivElement | null>(null);
  const [playing, setPlaying] = useState(false);
  const [time, setTime] = useState(0);
  const [duration, setDuration] = useState(0);

  const progress = duration > 0 ? Math.min(1, time / duration) : 0;

  function toggle(): void {
    const audio = audioRef.current;
    if (!audio) return;
    if (audio.paused) {
      pauseOtherPlayers(audio);
      void audio.play().catch(() => undefined);
    } else {
      audio.pause();
    }
  }

  return (
    <span className="music-player">
      <button
        type="button"
        className="tg-playbtn"
        onClick={toggle}
        aria-label={playing ? `Pause ${fileName}` : `Play ${fileName}`}
      >
        <Icon name={playing ? 'pause' : 'play'} size={22} />
      </button>
      <span className="music-meta">
        <span className="music-title" dir="auto" title={fileName}>
          {prettyTitle(fileName)}
        </span>
        <span
          ref={barRef}
          className="music-bar"
          role="slider"
          aria-label="Seek"
          aria-valuemin={0}
          aria-valuemax={Math.round(duration)}
          aria-valuenow={Math.round(time)}
          tabIndex={0}
          onClick={(e) => seekFromPointer(e, barRef.current, (r) => {
            const audio = audioRef.current;
            if (audio && Number.isFinite(audio.duration)) audio.currentTime = r * audio.duration;
          })}
          onKeyDown={(e) => {
            const audio = audioRef.current;
            if (!audio || !Number.isFinite(audio.duration)) return;
            if (e.key === 'ArrowRight') audio.currentTime = Math.min(audio.duration, audio.currentTime + 5);
            if (e.key === 'ArrowLeft') audio.currentTime = Math.max(0, audio.currentTime - 5);
          }}
        >
          <span className="music-bar-fill" style={{ width: `${progress * 100}%` }} />
          <span className="music-bar-knob" style={{ left: `${progress * 100}%` }} />
        </span>
        <span className="music-time">
          {formatMediaTime(time)} / {formatMediaTime(duration)}
        </span>
      </span>
      <audio
        ref={audioRef}
        data-tg-media
        src={url}
        preload="metadata"
        aria-label={fileName}
        onPlay={() => setPlaying(true)}
        onPause={() => setPlaying(false)}
        onEnded={() => setPlaying(false)}
        onTimeUpdate={(e) => setTime(e.currentTarget.currentTime)}
        onLoadedMetadata={(e) => {
          setDuration(Number.isFinite(e.currentTarget.duration) ? e.currentTarget.duration : 0);
          setTime(e.currentTarget.currentTime);
        }}
      />
    </span>
  );
}

/* ── Voice: round play button + waveform + duration (Telegram pill) ────── */

export function VoicePlayer({ url, fileId }: { url: string; fileId: string }) {
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const barsRef = useRef<HTMLSpanElement | null>(null);
  const [playing, setPlaying] = useState(false);
  const [listened, setListened] = useState(false);
  const [time, setTime] = useState(0);
  const [duration, setDuration] = useState(0);

  const bars = useMemo(() => barsFor(fileId), [fileId]);
  const progress = duration > 0 ? Math.min(1, time / duration) : 0;
  const playedCount = Math.round(progress * bars.length);

  function toggle(): void {
    const audio = audioRef.current;
    if (!audio) return;
    if (audio.paused) {
      pauseOtherPlayers(audio);
      setListened(true);
      void audio.play().catch(() => undefined);
    } else {
      audio.pause();
    }
  }

  return (
    <span className="voice-player">
      <button
        type="button"
        className="tg-playbtn sm"
        onClick={toggle}
        aria-label={playing ? 'Pause voice message' : 'Play voice message'}
      >
        <Icon name={playing ? 'pause' : 'play'} size={19} />
      </button>
      <span className="voice-main">
        <span
          ref={barsRef}
          className="voice-bars"
          role="slider"
          aria-label="Seek voice message"
          aria-valuemin={0}
          aria-valuemax={Math.round(duration)}
          aria-valuenow={Math.round(time)}
          tabIndex={0}
          onClick={(e) => seekFromPointer(e, barsRef.current, (r) => {
            const audio = audioRef.current;
            if (audio && Number.isFinite(audio.duration)) audio.currentTime = r * audio.duration;
          })}
          onKeyDown={(e) => {
            const audio = audioRef.current;
            if (!audio || !Number.isFinite(audio.duration)) return;
            if (e.key === 'ArrowRight') audio.currentTime = Math.min(audio.duration, audio.currentTime + 5);
            if (e.key === 'ArrowLeft') audio.currentTime = Math.max(0, audio.currentTime - 5);
          }}
        >
          {bars.map((h, i) => (
            <i
              key={i}
              style={{ height: `${Math.round(h * 22) + 4}px` }}
              className={i < playedCount ? 'on' : undefined}
            />
          ))}
        </span>
        <span className="voice-time">
          {formatMediaTime(time > 0 || playing ? time : duration)}
          {!listened ? <span className="voice-dot" aria-label="Unplayed" /> : null}
        </span>
      </span>
      <audio
        ref={audioRef}
        data-tg-media
        src={url}
        preload="metadata"
        aria-label="Voice message"
        onPlay={() => {
          setPlaying(true);
          setListened(true);
        }}
        onPause={() => setPlaying(false)}
        onEnded={() => setPlaying(false)}
        onTimeUpdate={(e) => setTime(e.currentTarget.currentTime)}
        onLoadedMetadata={(e) => {
          setDuration(Number.isFinite(e.currentTarget.duration) ? e.currentTarget.duration : 0);
          setTime(e.currentTarget.currentTime);
        }}
      />
    </span>
  );
}

/* ── Video: rounded frame, center play, duration badge, mini controls ──── */

export function VideoPlayer({ url, fileName }: { url: string; fileName: string }) {
  const wrapRef = useRef<HTMLSpanElement | null>(null);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const progressRef = useRef<HTMLSpanElement | null>(null);
  const [playing, setPlaying] = useState(false);
  const [started, setStarted] = useState(false);
  const [muted, setMuted] = useState(false);
  const [time, setTime] = useState(0);
  const [duration, setDuration] = useState(0);

  const progress = duration > 0 ? Math.min(1, time / duration) : 0;

  function toggle(): void {
    const video = videoRef.current;
    if (!video) return;
    if (video.paused) {
      pauseOtherPlayers(video);
      void video.play().catch(() => undefined);
    } else {
      video.pause();
    }
  }

  function toggleFullscreen(): void {
    const wrap = wrapRef.current;
    if (!wrap) return;
    if (document.fullscreenElement) {
      void document.exitFullscreen().catch(() => undefined);
      return;
    }
    if (wrap.requestFullscreen) void wrap.requestFullscreen().catch(() => undefined);
  }

  function toggleMuted(): void {
    const video = videoRef.current;
    if (!video) return;
    video.muted = !video.muted;
    setMuted(video.muted);
  }

  return (
    <span ref={wrapRef} className="tg-video">
      <video
        ref={videoRef}
        data-tg-media
        src={url}
        preload="metadata"
        playsInline
        aria-label={fileName}
        onClick={toggle}
        onPlay={() => {
          pauseOtherPlayers(videoRef.current);
          setPlaying(true);
          setStarted(true);
        }}
        onPause={() => setPlaying(false)}
        onEnded={() => setPlaying(false)}
        onTimeUpdate={(e) => setTime(e.currentTarget.currentTime)}
        onLoadedMetadata={(e) => {
          setDuration(Number.isFinite(e.currentTarget.duration) ? e.currentTarget.duration : 0);
          setTime(e.currentTarget.currentTime);
          setMuted(e.currentTarget.muted);
        }}
      />
      {!playing ? (
        <button type="button" className="tg-video-play" onClick={toggle} aria-label={`Play ${fileName}`}>
          <Icon name="play" size={28} />
        </button>
      ) : null}
      <span className="tg-video-bottom">
        <span className="tg-video-badge">
          {started && duration > 0
            ? `${formatMediaTime(time)} / ${formatMediaTime(duration)}`
            : formatMediaTime(duration)}
        </span>
        <span className="tg-video-btns">
          <button
            type="button"
            className="tg-video-icon"
            onClick={toggleMuted}
            aria-label={muted ? 'Unmute' : 'Mute'}
            title={muted ? 'Unmute' : 'Mute'}
          >
            <Icon name={muted ? 'volumeOff' : 'volume'} size={16} />
          </button>
          <button
            type="button"
            className="tg-video-icon"
            onClick={toggleFullscreen}
            aria-label="Fullscreen"
            title="Fullscreen"
          >
            <Icon name="expand" size={16} />
          </button>
        </span>
      </span>
      <span
        ref={progressRef}
        className="tg-video-progress"
        role="slider"
        aria-label="Seek video"
        aria-valuemin={0}
        aria-valuemax={Math.round(duration)}
        aria-valuenow={Math.round(time)}
        tabIndex={0}
        onClick={(e) => seekFromPointer(e, progressRef.current, (r) => {
          const video = videoRef.current;
          if (video && Number.isFinite(video.duration)) video.currentTime = r * video.duration;
        })}
        onKeyDown={(e) => {
          const video = videoRef.current;
          if (!video || !Number.isFinite(video.duration)) return;
          if (e.key === 'ArrowRight') video.currentTime = Math.min(video.duration, video.currentTime + 5);
          if (e.key === 'ArrowLeft') video.currentTime = Math.max(0, video.currentTime - 5);
        }}
      >
        <span className="tg-video-progress-fill" style={{ width: `${progress * 100}%` }} />
      </span>
    </span>
  );
}
