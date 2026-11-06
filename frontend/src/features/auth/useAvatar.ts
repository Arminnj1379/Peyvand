import { useEffect, useRef, useState } from 'react';
import { authApi } from '../auth/authApi';

/**
 * Loads an API-served avatar (authorized fetch) into an object URL.
 * Returns null while loading or when there is no avatar.
 */
export function useAvatarUrl(avatarUrl: string | null, bust: number): string | null {
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const createdRef = useRef<string | null>(null);

  useEffect(() => {
    if (createdRef.current) {
      URL.revokeObjectURL(createdRef.current);
      createdRef.current = null;
    }
    if (!avatarUrl) {
      return;
    }
    const controller = new AbortController();
    authApi
      .avatarBlob(avatarUrl, controller.signal)
      .then((blob) => {
        if (controller.signal.aborted) return;
        const url = URL.createObjectURL(blob);
        createdRef.current = url;
        setObjectUrl(url);
      })
      .catch(() => {
        // Offline or removed mid-flight: keep showing initials.
      });
    return () => controller.abort();
  }, [avatarUrl, bust]);

  return avatarUrl ? objectUrl : null;
}
