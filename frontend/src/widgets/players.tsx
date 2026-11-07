import { memo, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { RefObject } from 'react';
import { useT } from '../shared/i18n';
import { Icon } from '../shared/ui/Icon';

/** Telegram-web-style inline players (music, voice, video). */

export function formatMediaTime(totalSeconds: number): string {
  if (!Number.isFinite(totalSeconds) || totalSeconds < 0) return '0:00';
  const m = Math.floor(totalSeconds / 60);
  const s = Math.floor(totalSeconds % 60);
  return `${m}:${s.toString().padStart(2, '0')}`;
}

/** Pause other bubble players so two tracks never overlap (call tiles excluded). */
export function pauseOtherPlayers(except: HTMLMediaElement | null): void {
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

/** Deterministic pseudo-waveform so every voice message has its own shape.
 *  Smoothed neighbours + gentle mid-lift envelope keep the Telegram look:
 *  tapered ends, lively middle, no uniform picket-fence bars. */
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
  const raw = Array.from({ length: count }, () => rnd());
  return raw.map((v, i) => {
    const prev = raw[Math.max(0, i - 1)];
    const next = raw[Math.min(count - 1, i + 1)];
    const smooth = (prev + v * 2 + next) / 4;
    const t = count <= 1 ? 0.5 : i / (count - 1);
    const env = 0.45 + 0.55 * Math.sin(Math.PI * (0.08 + 0.84 * t));
    return Math.min(1, Math.max(0.14, (0.18 + smooth * 0.82) * env + 0.08));
  });
}

export function seekFromPointer(
  e: { clientX: number },
  el: HTMLElement | null,
  apply: (ratio: number) => void,
): void {
  if (!el) return;
  const rect = el.getBoundingClientRect();
  if (rect.width <= 0) return;
  let ratio = Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width));
  // In RTL the waveform/fill grows from the right; mirror the pointer ratio.
  try {
    if (getComputedStyle(el).direction === 'rtl') ratio = 1 - ratio;
  } catch {
    // Non-DOM environment: keep the LTR ratio.
  }
  apply(ratio);
}

/* ── Music (audio file): round play button + title + seek bar ─────────── */

export function MusicPlayer({ url, fileName }: { url: string; fileName: string }) {
  const t = useT();
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
        aria-label={playing ? t('player.pauseTrack', { name: fileName }) : t('player.playTrack', { name: fileName })}
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
          aria-label={t('player.seek')}
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
          <span className="music-bar-knob" style={{ insetInlineStart: `${progress * 100}%` }} />
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

/* ── Voice: round play button + waveform + duration (Telegram pill) ──────
 *  Logic lives in VoicePlayer; bars are a pure presentational view so the
 *  waveform can evolve without touching playback state. */

function VoiceWaveformView({
  bars,
  playedCount,
  playing,
  barsRef,
  duration,
  time,
  label,
  onSeek,
}: {
  bars: number[];
  playedCount: number;
  playing: boolean;
  barsRef: RefObject<HTMLSpanElement | null>;
  duration: number;
  time: number;
  label: string;
  onSeek: (ratio: number) => void;
}) {
  return (
    <span
      ref={barsRef}
      className={`voice-bars${playing ? ' playing' : ''}`}
      role="slider"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={Math.round(duration)}
      aria-valuenow={Math.round(time)}
      tabIndex={0}
      onClick={(e) => seekFromPointer(e, barsRef.current, onSeek)}
      onKeyDown={(e) => {
        const el = barsRef.current;
        const rtl = el ? getComputedStyle(el).direction === 'rtl' : false;
        const fwdKey = rtl ? 'ArrowLeft' : 'ArrowRight';
        const backKey = rtl ? 'ArrowRight' : 'ArrowLeft';
        if (e.key === fwdKey || e.key === backKey || e.key === 'Home' || e.key === 'End') {
          e.preventDefault();
        } else {
          return;
        }
        if (e.key === 'Home') onSeek(0);
        else if (e.key === 'End') onSeek(1);
        else if (e.key === fwdKey) onSeek(Math.min(1, (time + 5) / Math.max(1, duration)));
        else onSeek(Math.max(0, (time - 5) / Math.max(1, duration)));
      }}
    >
      {bars.map((h, i) => (
        <i
          key={i}
          style={{ height: `${Math.round(h * 20) + 4}px` }}
          className={i < playedCount ? 'on' : undefined}
        />
      ))}
    </span>
  );
}

// Memoized on playedCount (bar flip), not on the 60fps rAF clock: the parent
// re-renders every frame for the time text while bars only change ~4/sec.
const VoiceWaveform = memo(VoiceWaveformView, (p, n) => (
  p.playedCount === n.playedCount
  && p.playing === n.playing
  && p.duration === n.duration
  && p.bars === n.bars
  && p.label === n.label
  && p.onSeek === n.onSeek
  && p.barsRef === n.barsRef
  && Math.round(p.time) === Math.round(n.time)
));

export function VoicePlayer({ url, fileId }: { url: string; fileId: string }) {
  const t = useT();
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const barsRef = useRef<HTMLSpanElement | null>(null);
  const rafRef = useRef<number>(0);
  const [playing, setPlaying] = useState(false);
  const [listened, setListened] = useState(false);
  const [time, setTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  const bars = useMemo(() => barsFor(fileId), [fileId]);
  const progress = duration > 0 ? Math.min(1, time / duration) : 0;
  const playedCount = Math.round(progress * bars.length);
  // Single Telegram-style clock: duration at rest, elapsed once touched.
  const displayTime = duration > 0 && !(playing || time > 0.05) ? duration : time;
  // Telegram bubbles grow with length: short ≈188px, long caps at 264px.
  const widthPx = duration > 0 ? Math.round(Math.min(264, 188 + Math.min(76, duration * 2.4))) : undefined;

  // rAF-smooth clock while playing; onTimeUpdate stays as a fallback.
  useEffect(() => {
    if (!playing) return;
    const tick = () => {
      const a = audioRef.current;
      if (a && Number.isFinite(a.currentTime)) setTime(a.currentTime);
      rafRef.current = requestAnimationFrame(tick);
    };
    rafRef.current = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(rafRef.current);
  }, [playing]);

  const seekTo = useCallback((ratio: number): void => {
    const audio = audioRef.current;
    if (!audio || !Number.isFinite(audio.duration) || audio.duration <= 0) return;
    audio.currentTime = Math.min(1, Math.max(0, ratio)) * audio.duration;
  }, []);

  function toggle(): void {
    const audio = audioRef.current;
    if (!audio || failed) return;
    if (audio.paused) {
      pauseOtherPlayers(audio);
      // After a natural end the element sits at duration: rewind for replay.
      if (audio.ended || (Number.isFinite(audio.duration) && audio.currentTime >= audio.duration - 0.15)) {
        try {
          audio.currentTime = 0;
        } catch {
          // Non-seekable stream: replay from the start on next load.
        }
        setTime(0);
      }
      setListened(true);
      void audio.play().catch(() => undefined);
    } else {
      audio.pause();
    }
  }

  function retry(): void {
    setFailed(false);
    setLoading(true);
    setReloadKey((k) => k + 1);
  }

  if (failed) {
    return (
      <span className="voice-player voice-error">
        <button
          type="button"
          className="tg-playbtn sm"
          onClick={retry}
          aria-label={t('common.retry')}
          title={t('common.retry')}
        >
          <Icon name="refresh" size={18} />
        </button>
        <span className="voice-main">
          <span className="voice-errmsg" role="alert">
            {t('player.audioError')}
          </span>
        </span>
      </span>
    );
  }

  return (
    <span className="voice-player" style={widthPx ? { width: `${widthPx}px` } : undefined}>
      <button
        type="button"
        className="tg-playbtn sm"
        onClick={toggle}
        disabled={loading}
        aria-label={playing ? t('player.pauseVoice') : t('player.playVoice')}
      >
        {loading ? (
          <span className="tg-spinner" aria-hidden="true" />
        ) : (
          <Icon name={playing ? 'pause' : 'play'} size={19} />
        )}
      </button>
      <span className="voice-main">
        <VoiceWaveform
          bars={bars}
          playedCount={playedCount}
          playing={playing}
          barsRef={barsRef}
          duration={duration}
          time={time}
          label={t('player.seekVoice')}
          onSeek={seekTo}
        />
        <span className="voice-time">
          <span className="voice-time-text">{formatMediaTime(displayTime)}</span>
          {!listened ? <span className="voice-unplayed" aria-label={t('player.unplayed')} /> : null}
        </span>
      </span>
      <audio
        key={reloadKey}
        ref={audioRef}
        data-tg-media
        src={url}
        preload="metadata"
        aria-label={t('player.voiceMessage')}
        onPlay={() => {
          setPlaying(true);
          setListened(true);
        }}
        onPause={() => setPlaying(false)}
        onEnded={(e) => {
          setPlaying(false);
          setTime(0);
          try {
            e.currentTarget.currentTime = 0;
          } catch {
            // Leave the element alone when the stream is not seekable.
          }
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
          setLoading(false);
        }}
      />
    </span>
  );
}

/* ── Video preview: compact thumbnail; tap opens the fullscreen viewer ──
 *  The bubble never plays video inline: no controls, no autoplay. */

export function VideoPreview({ url, fileName, onOpen }: { url: string; fileName: string; onOpen: () => void }) {
  const t = useT();
  const [duration, setDuration] = useState(0);

  return (
    <button
      type="button"
      className="tg-video"
      onClick={onOpen}
      aria-label={t('viewer.openVideo', { name: fileName })}
    >
      <video
        src={url}
        preload="metadata"
        muted
        playsInline
        disablePictureInPicture
        aria-hidden="true"
        tabIndex={-1}
        onLoadedMetadata={(e) => {
          setDuration(Number.isFinite(e.currentTarget.duration) ? e.currentTarget.duration : 0);
        }}
      />
      <span className="tgv-play" aria-hidden="true">
        <Icon name="play" size={26} />
      </span>
      {duration > 0 ? (
        <span className="tgv-dur">{formatMediaTime(duration)}</span>
      ) : null}
    </button>
  );
}
