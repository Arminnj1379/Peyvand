/**
 * Centralized API client. Every request carries JSON + X-Correlation-ID and,
 * when present, the bearer token from the auth store. All calls accept an
 * AbortSignal (the frontend equivalent of CancellationToken).
 *
 * 401 handling: one single-flight refresh attempt (wired by the auth store)
 * before surfacing the error. Auth endpoints themselves never trigger it.
 */

export class ApiError extends Error {
  readonly status: number;
  readonly details: unknown;

  constructor(status: number, message: string, details: unknown = null) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.details = details;
  }
}

export const apiBaseUrl =
  (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '') ??
  'http://localhost:5094';

let tokenGetter: () => string | null = () => null;

/** Wired once by the auth store module; avoids an import cycle. */
export function setTokenGetter(fn: () => string | null): void {
  tokenGetter = fn;
}

let refreshHandler: (() => Promise<boolean>) | null = null;

/** Wired once by the auth module: rotates tokens, returns false when signed out. */
export function setRefreshHandler(fn: () => Promise<boolean>): void {
  refreshHandler = fn;
}

const NO_REFRESH_PREFIXES = ['/api/v1/auth/login', '/api/v1/auth/register', '/api/v1/auth/refresh'];

function newCorrelationId(): string {
  return typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID().replace(/-/g, '')
    : Math.random().toString(36).slice(2);
}

function authHeaders(extra?: HeadersInit): HeadersInit {
  const token = tokenGetter();
  return {
    'X-Correlation-ID': newCorrelationId(),
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(extra ?? {}),
  };
}

function toApiError(status: number, data: unknown): ApiError {
  const message =
    typeof data === 'object' && data !== null && 'title' in data
      ? String((data as { title: unknown }).title)
      : `Request failed (${status})`;
  return new ApiError(status, message, data);
}

async function request<T>(path: string, init: RequestInit, signal?: AbortSignal, retry = true): Promise<T> {
  const res = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    signal,
    headers: authHeaders({ 'Content-Type': 'application/json', ...(init.headers ?? {}) }),
  });

  if (res.status === 401 && retry && refreshHandler && !NO_REFRESH_PREFIXES.some((p) => path.startsWith(p))) {
    const rotated = await refreshHandler();
    if (rotated) {
      return request<T>(path, init, signal, false);
    }
  }

  if (res.status === 204) {
    return undefined as T;
  }

  const text = await res.text();
  const data: unknown = text ? (JSON.parse(text) as unknown) : null;

  if (!res.ok) {
    throw toApiError(res.status, data);
  }

  return data as T;
}

export const apiClient = {
  get: <T>(path: string, signal?: AbortSignal): Promise<T> =>
    request<T>(path, { method: 'GET' }, signal),
  post: <T>(path: string, body: unknown, signal?: AbortSignal): Promise<T> =>
    request<T>(path, { method: 'POST', body: JSON.stringify(body) }, signal),
  patch: <T>(path: string, body: unknown, signal?: AbortSignal): Promise<T> =>
    request<T>(path, { method: 'PATCH', body: JSON.stringify(body) }, signal),
  put: <T>(path: string, body: unknown, signal?: AbortSignal): Promise<T> =>
    request<T>(path, { method: 'PUT', body: JSON.stringify(body) }, signal),
  delete: <T>(path: string, signal?: AbortSignal): Promise<T> =>
    request<T>(path, { method: 'DELETE' }, signal),

  /** Multipart upload (browser sets the boundary; no JSON content type). */
  upload: async <T>(path: string, form: FormData, signal?: AbortSignal): Promise<T> => {
    const res = await fetch(`${apiBaseUrl}${path}`, {
      method: 'POST',
      body: form,
      signal,
      headers: authHeaders(),
    });
    const text = await res.text();
    const data: unknown = text ? (JSON.parse(text) as unknown) : null;
    if (!res.ok) {
      throw toApiError(res.status, data);
    }
    return data as T;
  },

  /** Authorized binary fetch (avatars served by the API, not public URLs). */
  blob: async (path: string, signal?: AbortSignal): Promise<Blob> => {
    const res = await fetch(`${apiBaseUrl}${path}`, { signal, headers: authHeaders() });
    if (!res.ok) {
      const text = await res.text();
      throw toApiError(res.status, text ? (JSON.parse(text) as unknown) : null);
    }
    return res.blob();
  },

  /**
   * Multipart upload with progress. fetch exposes no upload progress, so this
   * uses XHR with identical auth/correlation semantics. Aborting the signal
   * aborts the request.
   */
  uploadWithProgress: <T>(
    path: string,
    file: File,
    onProgress: (ratio: number) => void,
    signal?: AbortSignal,
  ): Promise<T> =>
    new Promise<T>((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      const form = new FormData();
      form.append('file', file, file.name);
      xhr.open('POST', `${apiBaseUrl}${path}`);
      const headers = authHeaders();
      for (const [key, value] of Object.entries(headers)) {
        xhr.setRequestHeader(key, value as string);
      }
      signal?.addEventListener('abort', () => xhr.abort(), { once: true });
      xhr.upload.addEventListener('progress', (e) => {
        if (e.lengthComputable && e.total > 0) onProgress(e.loaded / e.total);
      });
      xhr.addEventListener('load', () => {
        const data: unknown = xhr.responseText ? (JSON.parse(xhr.responseText) as unknown) : null;
        if (xhr.status >= 200 && xhr.status < 300) {
          resolve(data as T);
        } else {
          reject(toApiError(xhr.status, data));
        }
      });
      xhr.addEventListener('error', () => reject(new ApiError(0, 'Upload failed (network error).')));
      xhr.addEventListener('abort', () => reject(new ApiError(0, 'Upload cancelled.')));
      xhr.send(form);
    }),
};
