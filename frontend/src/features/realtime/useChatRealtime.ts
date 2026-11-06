import { useCallback, useEffect, useRef, useState } from 'react';
import type { HubConnection } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import type { InfiniteData } from '@tanstack/react-query';
import { createHubConnection } from '../../shared/realtime/connection';
import { chatApi } from '../chats/chatApi';
import { messageApi } from '../messages/messageApi';
import type { CallInfo } from '../calls/callTypes';
import type { Chat } from '../chats/chatTypes';
import type { HistoryPage, Message } from '../messages/messageTypes';

export type RealtimeStatus = 'idle' | 'connecting' | 'connected' | 'disconnected';

interface ReadEvent {
  chatId: string;
  userId: string;
  seq: number;
}

interface PresenceEvent {
  userId: string;
  isOnline: boolean;
  lastSeenAt: string | null;
}

interface TypingEvent {
  chatId: string;
  userId: string;
  isTyping: boolean;
}

function appendToHistory(
  old: InfiniteData<HistoryPage> | undefined,
  m: Message,
): InfiniteData<HistoryPage> | undefined {
  if (!old) return old;
  const dupe = old.pages.some((p) =>
    p.items.some((x) => x.id === m.id || (x.clientMessageId && x.clientMessageId === m.clientMessageId)),
  );
  if (dupe) {
    // Reconcile an optimistic bubble with the persisted message.
    return {
      ...old,
      pages: old.pages.map((p) => ({
        ...p,
        items: p.items.map((x) =>
          x.id === m.id || (x.clientMessageId && x.clientMessageId === m.clientMessageId) ? m : x,
        ),
      })),
    };
  }
  const [first, ...rest] = old.pages;
  if (!first) return { ...old, pages: [{ items: [m], nextCursor: null, hasMore: false }] };
  return { ...old, pages: [{ ...first, items: [...first.items, m] }, ...rest] };
}

function patchInHistory(
  old: InfiniteData<HistoryPage> | undefined,
  m: Message,
): InfiniteData<HistoryPage> | undefined {
  if (!old) return old;
  return {
    ...old,
    pages: old.pages.map((p) => ({
      ...p,
      items: p.items.map((x) => (x.id === m.id ? m : x)),
    })),
  };
}

/**
 * Single SignalR feed for chats. Mutations persist via REST first; hub events
 * only patch the react-query cache. Reconnects gap-fill via `sync`, so the
 * database stays the source of truth and SignalR is never durable storage.
 *
 * Call signaling rides the same connection (chat-group fan-out): `call:*`
 * events are transient (never stored client-side beyond the call store) and
 * forwarded to `onCallEvent` when provided.
 */
export function useChatRealtime(opts: {
  enabled: boolean;
  token: string | null;
  myUserId: string | undefined;
  chatIds: string[];
  activeChatId: string | null;
  onCallEvent?: (type: string, call: CallInfo) => void;
  onMessage?: (message: Message) => void;
}): {
  status: RealtimeStatus;
  typingIn: (chatId: string) => string[];
  sendTyping: (chatId: string, isTyping: boolean) => void;
  ackDelivered: (chatId: string, seq: number) => void;
  isOnline: (userId: string) => boolean;
  lastSeenAt: (userId: string) => string | null;
} {
  const { enabled, token, myUserId, chatIds, activeChatId, onCallEvent, onMessage } = opts;
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<RealtimeStatus>(() => (enabled ? 'connecting' : 'idle'));
  const [typing, setTyping] = useState<Record<string, Record<string, number>>>({});
  const [presence, setPresence] = useState<Record<string, { online: boolean; lastSeenAt: string | null }>>({});
  const connRef = useRef<HubConnection | null>(null);
  const activeRef = useRef(activeChatId);
  activeRef.current = activeChatId;
  const myIdRef = useRef(myUserId);
  myIdRef.current = myUserId;
  const onCallEventRef = useRef(onCallEvent);
  onCallEventRef.current = onCallEvent;
  const onMessageRef = useRef(onMessage);
  onMessageRef.current = onMessage;

  const touchChatsFor = useCallback(
    (m: Message) => {
      queryClient.setQueryData<Chat[]>(['chats'], (old) => {
        if (!old) return old;
        return old
          .map((c) =>
            c.id === m.chatId
              ? {
                  ...c,
                  lastMessageId: m.id,
                  lastMessageAt: m.createdAt,
                  lastMessagePreview: m.isDeleted ? 'Message deleted' : m.body.slice(0, 140),
                  seqCounter: Math.max(c.seqCounter, m.seq),
                  unreadCount:
                    m.chatId === activeRef.current || m.senderId === myIdRef.current
                      ? c.unreadCount
                      : c.unreadCount + 1,
                }
              : c,
          )
          .sort((a, b) => (b.lastMessageAt ?? '').localeCompare(a.lastMessageAt ?? ''));
      });
    },
    [queryClient],
  );

  const mergeMessage = useCallback(
    (m: Message) => {
      queryClient.setQueryData<InfiniteData<HistoryPage>>(['history', m.chatId], (old) =>
        appendToHistory(old, m),
      );
      touchChatsFor(m);
      // Reading what we look at: advance the watermark when the chat is open.
      if (m.chatId === activeRef.current && m.senderId !== myIdRef.current) {
        void chatApi.markRead(m.chatId, m.seq).catch(() => undefined);
      }
    },
    [queryClient, touchChatsFor],
  );

  useEffect(() => {
    if (!enabled) {
      setStatus('idle');
      return;
    }
    const connection = createHubConnection(() => token);
    connRef.current = connection;
    let disposed = false;

    const resync = () => {
      // Conversation list first (membership/pins may have changed while away),
      // then per-chat history gap-fill from the highest cached seq.
      void queryClient.invalidateQueries({ queryKey: ['chats'] });
      const ids = queryClient.getQueryData<Chat[]>(['chats'])?.map((c) => c.id) ?? [];
      for (const id of ids) {
        void queryClient.invalidateQueries({ queryKey: ['pins', id] });
        const cached = queryClient.getQueryData<InfiniteData<HistoryPage>>(['history', id]);
        const maxSeq = cached
          ? Math.max(0, ...cached.pages.flatMap((p) => p.items.map((m) => m.seq)))
          : 0;
        void messageApi
          .sync(id, maxSeq, 100)
          .then((missed) => {
            for (const m of missed) mergeMessage(m);
          })
          .catch(() => undefined);
      }
    };

    connection.on('message:new', (m: Message) => {
      if (disposed) return;
      mergeMessage(m);
      // Live arrivals only (gap-fill resyncs don't toast): other senders.
      if (m.senderId !== myIdRef.current) onMessageRef.current?.(m);
    });
    connection.on('message:edited', (m: Message) => {
      if (disposed) return;
      queryClient.setQueryData<InfiniteData<HistoryPage>>(['history', m.chatId], (old) =>
        patchInHistory(old, m),
      );
      touchChatsFor(m);
    });
    connection.on('message:deleted', (m: Message) => {
      if (disposed) return;
      queryClient.setQueryData<InfiniteData<HistoryPage>>(['history', m.chatId], (old) =>
        patchInHistory(old, m),
      );
      touchChatsFor(m);
    });
    connection.on('message:reacted', (m: Message) => {
      if (disposed) return;
      queryClient.setQueryData<InfiniteData<HistoryPage>>(['history', m.chatId], (old) =>
        patchInHistory(old, m),
      );
    });
    connection.on('message:read', (e: ReadEvent) => {
      if (disposed) return;
      queryClient.setQueryData<Chat[]>(['chats'], (old) => {
        if (!old) return old;
        return old.map((c) => {
          if (c.id !== e.chatId) return c;
          return {
            ...c,
            members: c.members.map((m) =>
              m.userId === e.userId
                ? {
                    ...m,
                    lastReadSeq: Math.max(m.lastReadSeq, e.seq),
                    lastDeliveredSeq: Math.max(m.lastDeliveredSeq, e.seq),
                  }
                : m,
            ),
            unreadCount: e.userId === myIdRef.current ? 0 : c.unreadCount,
            lastReadSeq: e.userId === myIdRef.current ? Math.max(c.lastReadSeq, e.seq) : c.lastReadSeq,
          };
        });
      });
    });
    connection.on('message:delivered', (e: ReadEvent) => {
      if (disposed) return;
      // Monotonic per-member watermark; stale/duplicate acks are no-ops.
      queryClient.setQueryData<Chat[]>(['chats'], (old) => {
        if (!old) return old;
        return old.map((c) => {
          if (c.id !== e.chatId) return c;
          return {
            ...c,
            members: c.members.map((m) =>
              m.userId === e.userId
                ? { ...m, lastDeliveredSeq: Math.max(m.lastDeliveredSeq, e.seq) }
                : m,
            ),
          };
        });
      });
    });
    connection.on('presence:update', (e: PresenceEvent) => {
      if (disposed || e.userId === myIdRef.current) return;
      setPresence((prev) => ({
        ...prev,
        [e.userId]: { online: e.isOnline, lastSeenAt: e.lastSeenAt ?? prev[e.userId]?.lastSeenAt ?? null },
      }));
    });
    connection.on('chat:updated', (e: { chatId: string }) => {
      if (disposed) return;
      void queryClient.invalidateQueries({ queryKey: ['chats'] });
      void queryClient.invalidateQueries({ queryKey: ['pins', e.chatId] });
    });
    connection.on('typing:update', (e: TypingEvent) => {
      if (disposed || e.userId === myIdRef.current) return;
      setTyping((prev) => {
        const chat = { ...(prev[e.chatId] ?? {}) };
        if (e.isTyping) chat[e.userId] = Date.now() + 5000;
        else delete chat[e.userId];
        return { ...prev, [e.chatId]: chat };
      });
    });

    // Call signaling (transient): full call payloads, forwarded to the call store.
    for (const type of [
      'call:incoming',
      'call:accepted',
      'call:rejected',
      'call:joined',
      'call:left',
      'call:ended',
    ]) {
      connection.on(type, (call: CallInfo) => {
        if (!disposed) onCallEventRef.current?.(type, call);
      });
    }

    connection.onreconnecting(() => {
      if (!disposed) setStatus('connecting');
    });
    connection.onreconnected(() => {
      if (disposed) return;
      setStatus('connected');
      const ids = queryClient.getQueryData<Chat[]>(['chats'])?.map((c) => c.id) ?? [];
      if (ids.length > 0) void connection.invoke('Subscribe', ids).catch(() => undefined);
      resync();
    });
    connection.onclose(() => {
      if (!disposed) setStatus('disconnected');
    });

    setStatus('connecting');
    connection
      .start()
      .then(() => {
        if (disposed) return;
        setStatus('connected');
        const ids = queryClient.getQueryData<Chat[]>(['chats'])?.map((c) => c.id) ?? [];
        if (ids.length > 0) void connection.invoke('Subscribe', ids).catch(() => undefined);
      })
      .catch(() => {
        if (!disposed) setStatus('disconnected');
      });

    const prune = window.setInterval(() => {
      setTyping((prev) => {
        const now = Date.now();
        let changed = false;
        const next: typeof prev = {};
        for (const [chatId, users] of Object.entries(prev)) {
          const live: Record<string, number> = {};
          for (const [uid, exp] of Object.entries(users)) {
            if (exp > now) live[uid] = exp;
            else changed = true;
          }
          if (Object.keys(live).length > 0) next[chatId] = live;
          else if ((prev[chatId] ? Object.keys(prev[chatId]).length : 0) > 0) changed = true;
        }
        return changed ? next : prev;
      });
    }, 3000);

    return () => {
      disposed = true;
      window.clearInterval(prune);
      connRef.current = null;
      void connection.stop();
    };
  }, [enabled, token, queryClient, mergeMessage, touchChatsFor]);

  // Keep the server-side group membership in sync with the chat list.
  useEffect(() => {
    const conn = connRef.current;
    if (!conn || status !== 'connected' || !enabled) return;
    if (chatIds.length === 0) return;
    void conn.invoke('Subscribe', chatIds).catch(() => undefined);
  }, [enabled, status, chatIds.join(',')]); // eslint-disable-line react-hooks/exhaustive-deps

  const typingIn = useCallback(
    (chatId: string) => Object.keys(typing[chatId] ?? {}),
    [typing],
  );

  const sendTyping = useCallback((chatId: string, isTyping: boolean) => {
    void connRef.current?.invoke('Typing', chatId, isTyping).catch(() => undefined);
  }, []);

  const ackDelivered = useCallback((chatId: string, seq: number) => {
    void connRef.current?.invoke('Delivered', chatId, seq).catch(() => undefined);
  }, []);

  const isOnline = useCallback((userId: string) => presence[userId]?.online ?? false, [presence]);

  const lastSeenAt = useCallback(
    (userId: string) => presence[userId]?.lastSeenAt ?? null,
    [presence],
  );

  return { status, typingIn, sendTyping, ackDelivered, isOnline, lastSeenAt };
}
