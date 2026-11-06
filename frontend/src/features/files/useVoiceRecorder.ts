import { useCallback, useEffect, useRef, useState } from 'react';

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
  const [state, setState] = useState<VoiceState>('idle');
  const [seconds, setSeconds] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const timerRef = useRef<number | null>(null);

  const cleanup = useCallback(() => {
    if (timerRef.current) {
      window.clearInterval(timerRef.current);
      timerRef.current = null;
    }
    if (streamRef.current) {
      streamRef.current.getTracks().forEach((t) => t.stop());
      streamRef.current = null;
    }
    recorderRef.current = null;
    chunksRef.current = [];
  }, []);

  useEffect(() => cleanup, [cleanup]);

  const start = useCallback(async () => {
    setError(null);
    if (typeof MediaRecorder === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
      setError('Voice recording is not supported in this browser.');
      return false;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      streamRef.current = stream;
      chunksRef.current = [];
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
        setError('Microphone access was denied. Allow it in the browser address bar and try again.');
      } else if (e instanceof DOMException && e.name === 'NotFoundError') {
        setError('No microphone found on this device.');
      } else {
        setError('Could not start recording. Check the microphone and try again.');
      }
      return false;
    }
  }, [cleanup]);

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
      setError('The recording was empty — nothing was sent.');
      return null;
    }
    const id = Date.now().toString(36);
    return new File([blob], `voice-${id}.${extensionFor(mime)}`, { type: blob.type });
  }, [cleanup]);

  const cancel = useCallback(() => stop(true), [stop]);

  return { state, seconds, error, start, stop, cancel, clearError: () => setError(null) };
}
