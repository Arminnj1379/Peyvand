import { useCallback, useEffect, useRef, useState } from 'react';
import { useT } from '../../shared/i18n';

/**
 * In-app voice recording via MediaRecorder.
 *
 * Mime fallback chain (first supported wins): Chrome/Edge/Firefox record
 * `audio/webm;codecs=opus`, Safari records `audio/mp4`. The backend accepts
 * both spellings (see FileValidation.ExtensionAllows), so the produced File
 * uses the matching extension (.webm / .m4a / .ogg) and uploads through the
 * normal upload-then-attach pipeline.
 */
export type VoiceState = 'idle' | 'recording';

const MIME_CANDIDATES = [
  'audio/webm;codecs=opus',
  'audio/webm',
  'audio/mp4',
  'audio/ogg;codecs=opus',
];

function extensionFor(mime: string): string {
  if (mime.includes('mp4')) return 'm4a';
  if (mime.includes('ogg')) return 'ogg';
  return 'webm';
}

export function formatVoiceTime(totalSeconds: number): string {
  const m = Math.floor(totalSeconds / 60);
  const s = totalSeconds % 60;
  return `${m}:${s.toString().padStart(2, '0')}`;
}

export function useVoiceRecorder() {
  const t = useT();
  const [state, setState] = useState<VoiceState>('idle');
  const [seconds, setSeconds] = useState(0);
  const [error, setError] = useState<string | null>(null);
  /** Live mic levels (0..1, ~28 bands) for the recording waveform. Empty when idle. */
  const [levels, setLevels] = useState<number[]>([]);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const timerRef = useRef<number | null>(null);
  const audioCtxRef = useRef<AudioContext | null>(null);
  const meterTimerRef = useRef<number | null>(null);

  const cleanup = useCallback(() => {
    if (timerRef.current) {
      window.clearInterval(timerRef.current);
      timerRef.current = null;
    }
    if (meterTimerRef.current) {
      window.clearInterval(meterTimerRef.current);
      meterTimerRef.current = null;
    }
    if (audioCtxRef.current) {
      void audioCtxRef.current.close().catch(() => undefined);
      audioCtxRef.current = null;
    }
    if (streamRef.current) {
      streamRef.current.getTracks().forEach((t) => t.stop());
      streamRef.current = null;
    }
    recorderRef.current = null;
    chunksRef.current = [];
    setLevels([]);
  }, []);

  useEffect(() => cleanup, [cleanup]);

  /**
   * Live input meter for the recording waveform. Best-effort: when
   * WebAudio is unavailable the UI falls back to a CSS pulse animation.
   */
  const startMeter = useCallback((stream: MediaStream) => {
    try {
      const Ctx = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
      if (!Ctx) return;
      const ctx = new Ctx();
      audioCtxRef.current = ctx;
      const source = ctx.createMediaStreamSource(stream);
      const analyser = ctx.createAnalyser();
      analyser.fftSize = 64;
      source.connect(analyser);
      const bins = new Uint8Array(analyser.frequencyBinCount);
      meterTimerRef.current = window.setInterval(() => {
        analyser.getByteFrequencyData(bins);
        const bands = 28;
        const per = Math.max(1, Math.floor(bins.length / bands));
        const next: number[] = [];
        for (let i = 0; i < bands; i++) {
          let sum = 0;
          for (let j = 0; j < per && i * per + j < bins.length; j++) sum += bins[i * per + j];
          next.push(Math.min(1, sum / (per * 128)));
        }
        setLevels(next);
      }, 120);
    } catch {
      // Meter is decorative; recording works without it.
    }
  }, []);

  const start = useCallback(async () => {
    setError(null);
    if (typeof MediaRecorder === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
      setError(t('chat.voiceNotSupported'));
      return false;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      streamRef.current = stream;
      chunksRef.current = [];
      startMeter(stream);
      const mimeType =
        MIME_CANDIDATES.find((m) => {
          try {
            return MediaRecorder.isTypeSupported(m);
          } catch {
            return false;
          }
        }) ?? '';
      const recorder = mimeType
        ? new MediaRecorder(stream, { mimeType })
        : new MediaRecorder(stream);
      recorderRef.current = recorder;
      recorder.ondataavailable = (e) => {
        if (e.data.size > 0) chunksRef.current.push(e.data);
      };
      recorder.start(250);
      setSeconds(0);
      setState('recording');
      timerRef.current = window.setInterval(() => setSeconds((s) => s + 1), 1000);
      return true;
    } catch (e) {
      cleanup();
      if (e instanceof DOMException && e.name === 'NotAllowedError') {
        setError(t('chat.micDenied'));
      } else if (e instanceof DOMException && e.name === 'NotFoundError') {
        setError(t('chat.noMic'));
      } else {
        setError(t('chat.recStartFailed'));
      }
      return false;
    }
  }, [cleanup, t]);

  /** Stops recording and resolves with a voice File, or null when empty/cancelled. */
  const stop = useCallback(async (cancelled = false): Promise<File | null> => {
    const recorder = recorderRef.current;
    if (!recorder || recorder.state === 'inactive') {
      cleanup();
      setState('idle');
      return null;
    }
    const done = new Promise<Blob[]>((resolve) => {
      recorder.onstop = () => resolve([...chunksRef.current]);
    });
    recorder.stop();
    const chunks = await done;
    const mime = recorder.mimeType || 'audio/webm';
    cleanup();
    setState('idle');
    if (cancelled || chunks.length === 0) return null;
    const blob = new Blob(chunks, { type: mime.split(';')[0] });
    if (blob.size === 0) {
      setError(t('chat.recEmpty'));
      return null;
    }
    const id = Date.now().toString(36);
    return new File([blob], `voice-${id}.${extensionFor(mime)}`, { type: blob.type });
  }, [cleanup, t]);

  const cancel = useCallback(() => stop(true), [stop]);

  return { state, seconds, levels, error, start, stop, cancel, clearError: () => setError(null) };
}
