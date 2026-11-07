import { useEffect, useRef, useState } from 'react';

/**
 * Minimal AnimatePresence equivalent for CSS-only overlays.
 * Keeps the node mounted for `duration` ms after `open` becomes false
 * so the `.leaving` exit animation can play, then unmounts.
 * Transform/opacity only; respects reduced-motion via CSS (durations collapse).
 */
export function useOverlayTransition(open: boolean, duration = 180): {
  shouldRender: boolean;
  leaving: boolean;
} {
  const [shouldRender, setShouldRender] = useState(open);
  const [leaving, setLeaving] = useState(false);
  const timer = useRef<number | null>(null);

  useEffect(() => {
    if (open) {
      if (timer.current) window.clearTimeout(timer.current);
      setShouldRender(true);
      setLeaving(false);
      return;
    }
    if (!shouldRender) return;
    // Start exit; unmount after the CSS exit finishes.
    setLeaving(true);
    timer.current = window.setTimeout(() => {
      setShouldRender(false);
      setLeaving(false);
    }, duration);
    return () => {
      if (timer.current) window.clearTimeout(timer.current);
    };
  }, [open, shouldRender, duration]);

  // First mount closed → render nothing.
  useEffect(() => () => {
    if (timer.current) window.clearTimeout(timer.current);
  }, []);

  return { shouldRender, leaving };
}
