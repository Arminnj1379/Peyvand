import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import { setRefreshHandler, setTokenGetter } from '../../shared/api/client';
import { authApi } from './authApi';
import type { User } from './authTypes';

export type AuthMode = 'anonymous' | 'authenticated';

interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  user: User | null;
  mode: AuthMode;
  login: (login: string, password: string) => Promise<void>;
  register: (input: { username: string; displayName: string; password: string; phone?: string }) => Promise<void>;
  logout: () => Promise<void>;
  refreshTokens: () => Promise<boolean>;
  setUser: (user: User) => void;
  clear: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      accessToken: null,
      refreshToken: null,
      user: null,
      mode: 'anonymous',

      login: async (login, password) => {
        const res = await authApi.login(login, password);
        set({ mode: 'authenticated', accessToken: res.accessToken, refreshToken: res.refreshToken, user: res.user });
      },

      register: async (input) => {
        const res = await authApi.register(input);
        set({ mode: 'authenticated', accessToken: res.accessToken, refreshToken: res.refreshToken, user: res.user });
      },

      logout: async () => {
        try {
          await authApi.logout();
        } catch {
          // Best effort: the session may already be gone.
        }
        get().clear();
      },

      refreshTokens: async () => {
        const { refreshToken } = get();
        if (!refreshToken) return false;
        try {
          const res = await authApi.refresh(refreshToken);
          set({ accessToken: res.accessToken, refreshToken: res.refreshToken, user: res.user });
          return true;
        } catch {
          get().clear();
          return false;
        }
      },

      setUser: (user) => set({ user }),

      clear: () => set({ mode: 'anonymous', accessToken: null, refreshToken: null, user: null }),
    }),
    {
      name: 'messenger-auth',
      version: 3,
      // v2 stored a 'preview' mode that no longer exists: drop it to anonymous.
      migrate: (persisted: unknown) => {
        const state = (persisted ?? {}) as { mode?: unknown };
        if (state.mode === 'preview') return { ...state, mode: 'anonymous' };
        return persisted as never;
      },
      partialize: (s) => ({ accessToken: s.accessToken, refreshToken: s.refreshToken, user: s.user, mode: s.mode }),
    },
  ),
);

// Single wiring points: token per request + single-flight refresh on 401.
setTokenGetter(() => useAuthStore.getState().accessToken);

let inflight: Promise<boolean> | null = null;
setRefreshHandler(() => {
  if (!inflight) {
    inflight = useAuthStore.getState().refreshTokens().finally(() => {
      inflight = null;
    });
  }
  return inflight;
});
