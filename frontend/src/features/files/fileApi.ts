import { useEffect, useState } from 'react';
import { apiClient, ApiError } from '../../shared/api/client';
import type { UploadedFile } from './fileTypes';

export const fileApi = {
  /**
   * Multipart upload with progress (fetch has no upload progress, hence XHR).
   * Returns server-validated metadata for attach-on-send.
   */
  upload(file: File, onProgress: (ratio: number) => void, signal?: AbortSignal) {
    return apiClient.uploadWithProgress<UploadedFile>('/api/v1/files', file, onProgress, signal);
  },

  /** Authorized binary fetch; MinIO is never exposed to the browser. */
  downloadBlob(fileId: string, signal?: AbortSignal) {
    return apiClient.blob(`/api/v1/files/${fileId}`, signal);
  },

  remove(fileId: string, signal?: AbortSignal) {
    return apiClient.delete<void>(`/api/v1/files/${fileId}`, signal);
  },
};

/** Object URL for an attachment (authorized fetch, revoked on unmount/change). */
export function useAttachmentUrl(fileId: string | undefined, kind: string): string | null {
  const [url, setUrl] = useState<string | null>(null);

  useEffect(() => {
    if (!fileId) {
      setUrl(null);
      return;
    }
    // Documents render as rows; only media needs blob URLs.
    if (kind !== 'Image' && kind !== 'Video' && kind !== 'Audio') {
      setUrl(null);
      return;
    }
    const controller = new AbortController();
    let objectUrl: string | null = null;
    fileApi
      .downloadBlob(fileId, controller.signal)
      .then((blob) => {
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch((e: unknown) => {
        if (!(e instanceof ApiError)) setUrl(null);
      });
    return () => {
      controller.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [fileId, kind]);

  return url;
}

/** Saves an attachment through the authorized endpoint (no public URLs). */
export async function saveAttachment(fileId: string, fileName: string): Promise<void> {
  const blob = await fileApi.downloadBlob(fileId);
  const url = URL.createObjectURL(blob);
  try {
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
  } finally {
    setTimeout(() => URL.revokeObjectURL(url), 5000);
  }
}
