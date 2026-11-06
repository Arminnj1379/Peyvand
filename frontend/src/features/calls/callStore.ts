import { create } from 'zustand';
import type { CallInfo, CallJoin } from './callTypes';

/**
 * Ephemeral call state (Zustand — same pattern as the auth store).
 * Server truth stays in MongoDB (`calls`); SignalR events only patch this.
 * At most one ringing/in-call session is surfaced at a time; a second
 * incoming call waits in the queue behind it.
 */
interface CallState {
  /** Call I started or joined and am currently in (media may still connect). */
  active: CallInfo | null;
  /** Credentials for the LiveKit room of the active call. */
  join: CallJoin | null;
  /** Currently ringing incoming call (not mine, not ended). */
  incoming: CallInfo | null;
  /** Last signaling error to surface (join failures, busy, …). */
  error: string | null;

  setActive: (call: CallInfo | null, join?: CallJoin | null) => void;
  setIncoming: (call: CallInfo | null) => void;
  setError: (message: string | null) => void;
  /** Patches whichever tracked call matches (incoming or active). */
  patchCall: (call: CallInfo) => void;
  reset: () => void;
}

export const useCallStore = create<CallState>((set) => ({
  active: null,
  join: null,
  incoming: null,
  error: null,

  setActive: (call, join = null) =>
    set((s) => ({
      active: call,
      join: call ? (join ?? s.join) : null,
      // An active session dismisses the incoming banner for the same call.
      incoming: call && s.incoming?.id === call.id ? null : s.incoming,
      error: null,
    })),

  setIncoming: (call) => set({ incoming: call }),

  setError: (message) => set({ error: message }),

  patchCall: (call) =>
    set((s) => {
      if (s.active?.id === call.id) {
        return {
          active: call,
          incoming: s.incoming?.id === call.id ? null : s.incoming,
        };
      }
      if (s.incoming?.id === call.id) {
        return call.status === 'Ended' ? { incoming: null } : { incoming: call };
      }
      return {};
    }),

  reset: () => set({ active: null, join: null, incoming: null, error: null }),
}));
