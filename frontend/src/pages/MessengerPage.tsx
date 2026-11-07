import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { CSSProperties } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { useAuthStore } from '../features/auth/authStore';
import { useCallStore } from '../features/calls/callStore';
import type { CallInfo } from '../features/calls/callTypes';
import { chatApi } from '../features/chats/chatApi';
import type { Chat } from '../features/chats/chatTypes';
import type { Message } from '../features/messages/messageTypes';
import { useChatRealtime } from '../features/realtime/useChatRealtime';
import { useSettingsStore } from '../features/settings/settingsStore';
import { ChatDetails } from '../widgets/ChatDetails';
import { ChatList } from '../widgets/ChatList';
import type { FolderFilter } from '../widgets/ChatList';
import { ChatWindow } from '../widgets/ChatWindow';
import { JoinChannelDialog } from '../widgets/JoinChannelDialog';
import { NewChatDialog } from '../widgets/NewChatDialog';
import { SearchDialog } from '../widgets/SearchDialog';
import { EmptyState } from '../shared/ui/EmptyState';
import { useT } from '../shared/i18n';
import { Toasts } from '../widgets/Toasts';
import type { Toast } from '../widgets/Toasts';

let toastId = 0;

export function MessengerPage() {
  const t = useT();
  const user = useAuthStore((s) => s.user);
  const accessToken = useAuthStore((s) => s.accessToken);
  const myUserId = user?.id;
  const queryClient = useQueryClient();
  const [params, setParams] = useSearchParams();

  const [activeId, setActiveId] = useState<string | null>(null);
  const [detailsOpen, setDetailsOpen] = useState(false);
  const [newChatOpen, setNewChatOpen] = useState(false);
  const [joinOpen, setJoinOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState<FolderFilter>('all');
  const [toasts, setToasts] = useState<Toast[]>([]);
  // Resizable sidebar (300–640px, persisted). Inline var overrides the CSS default.
  const [sidebarW, setSidebarW] = useState<number>(() => {
    try {
      const saved = Number(localStorage.getItem('messenger:sidebar-w'));
      return Number.isFinite(saved) && saved >= 300 && saved <= 640 ? saved : 400;
    } catch {
      return 400;
    }
  });
  const shellRef = useRef<HTMLDivElement | null>(null);
  const dragRef = useRef<{ startX: number; startW: number } | null>(null);
  const [resizing, setResizing] = useState(false);
  const activeIdRef = useRef(activeId);
  activeIdRef.current = activeId;

  const chatsQuery = useQuery({
    queryKey: ['chats'],
    queryFn: ({ signal }) => chatApi.list(50, signal),
    staleTime: 15_000,
  });
  const chats = useMemo(() => chatsQuery.data ?? [], [chatsQuery.data]);
  const chatIds = useMemo(() => chats.map((c) => c.id), [chats]);

  // Caller preferences (theme applied to <html>, notification switch gates toasts).
  useEffect(() => {
    void useSettingsStore.getState().load();
  }, []);

  // Call signaling → ephemeral store (server stays the source of truth).
  const handleCallEvent = useCallback(
    (type: string, call: CallInfo) => {
      const s = useCallStore.getState();
      if (type === 'call:incoming') {
        if (call.initiatorId !== myUserId && call.status === 'Ringing' && !s.active) {
          s.setIncoming(call);
        }
      } else if (type === 'call:ended') {
        if (s.active?.id === call.id) s.setActive(null);
        else s.patchCall(call);
      } else {
        s.patchCall(call);
      }
      void queryClient.invalidateQueries({ queryKey: ['calls', call.chatId] });
    },
    [myUserId, queryClient],
  );

  const dismissToast = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  // Toasts for messages arriving outside the open chat (live arrivals only).
  const handleMessage = useCallback(
    (m: Message) => {
      if (m.chatId === activeIdRef.current) return;
      if (!useSettingsStore.getState().notificationsEnabled) return;
      const id = ++toastId;
      const title = m.senderName ?? m.senderUsername ?? t('messenger.newMessage');
      setToasts((prev) =>
        [...prev, { id, title, preview: m.body.slice(0, 120), chatId: m.chatId }].slice(-3),
      );
      window.setTimeout(() => dismissToast(id), 8000);
    },
    [dismissToast, t],
  );

  const { status, typingIn, sendTyping, ackDelivered, isOnline, lastSeenAt } = useChatRealtime({
    enabled: true,
    token: accessToken,
    myUserId,
    chatIds,
    activeChatId: activeId,
    onCallEvent: handleCallEvent,
    onMessage: handleMessage,
  });

  // Default to the most recent conversation (desktop only — mobile starts on the list).
  useEffect(() => {
    if (activeId !== null || chats.length === 0) return;
    if (!window.matchMedia('(min-width: 768px)').matches) return;
    setActiveId(chats[0].id);
  }, [chats, activeId]);

  // Deep link (?chat=) from notifications, search, and support tickets.
  useEffect(() => {
    const linked = params.get('chat');
    if (linked && chats.some((c) => c.id === linked) && linked !== activeIdRef.current) {
      setActiveId(linked);
      setDetailsOpen(false);
      setParams({}, { replace: true });
    }
  }, [params, chats, setParams]);

  // Drop selection when the chat disappears (left / deleted).
  // Desktop falls back to the next conversation; mobile returns to the list.
  useEffect(() => {
    if (activeId !== null && chats.length > 0 && !chats.some((c) => c.id === activeId)) {
      if (window.matchMedia('(min-width: 768px)').matches) {
        setActiveId(chats[0]?.id ?? null);
      } else {
        setActiveId(null);
      }
      setDetailsOpen(false);
    }
  }, [chats, activeId]);

  // Global search shortcut.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setSearchOpen(true);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const active = useMemo(() => chats.find((c) => c.id === activeId) ?? null, [chats, activeId]);

  const activePeerId =
    active?.type === 'Dm' ? (active.members.find((m) => m.userId !== myUserId)?.userId ?? null) : null;

  const typingIds = activeId ? typingIn(activeId) : [];
  const typingNames = useMemo(() => {
    if (!active) return [];
    return typingIds
      .map((id) => active.members.find((m) => m.userId === id)?.displayName ?? t('messenger.someone'))
      .slice(0, 3);
  }, [typingIds, active]);

  const openChat = useCallback((id: string) => {
    setSearchOpen(false);
    setActiveId(id);
    setDetailsOpen(false);
  }, []);

  function onResizePointerDown(e: React.PointerEvent): void {
    if (e.button !== 0) return;
    (e.target as HTMLElement).setPointerCapture?.(e.pointerId);
    dragRef.current = { startX: e.clientX, startW: sidebarW };
    setResizing(true);
    e.preventDefault();
  }

  useEffect(() => {
    function onMove(e: PointerEvent): void {
      const drag = dragRef.current;
      if (!drag) return;
      const rtl = document.documentElement.dir === 'rtl';
      const delta = rtl ? drag.startX - e.clientX : e.clientX - drag.startX;
      setSidebarW(Math.round(Math.min(640, Math.max(300, drag.startW + delta))));
    }
    function onUp(): void {
      if (!dragRef.current) return;
      dragRef.current = null;
      setResizing(false);
      try {
        localStorage.setItem('messenger:sidebar-w', String(sidebarW));
      } catch {
        // Private mode: skip persistence.
      }
    }
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);
    return () => {
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
    };
  }, [sidebarW]);

  return (
    <div
      className={`shell${activeId ? ' chat-open' : ''}`}
      ref={shellRef}
      style={{ '--sidebar-w': `${sidebarW}px` } as CSSProperties}
    >
      {status === 'connecting' || status === 'disconnected' ? (
        <div className={`conn-banner${status === 'disconnected' ? ' offline' : ''}`} role="status" aria-live="polite">
          <span className="conn-dot" aria-hidden="true" />
          <span className="conn-text">
            {status === 'connecting' ? t('messenger.connecting') : t('messenger.offline')}
          </span>
          {status === 'disconnected' ? (
            <button
              className="conn-retry"
              onClick={() => void queryClient.invalidateQueries({ queryKey: ['chats'] })}
            >
              {t('common.retry')}
            </button>
          ) : null}
        </div>
      ) : null}
      <ChatList
        chats={chats}
        activeId={activeId}
        onSelect={(id) => {
          setActiveId(id);
          setDetailsOpen(false);
        }}
        myUserId={myUserId}
        isOnline={isOnline}
        query={query}
        onQueryChange={setQuery}
        filter={filter}
        onFilterChange={setFilter}
        onNewChat={() => setNewChatOpen(true)}
        onJoinChannel={() => setJoinOpen(true)}
        onOpenSearch={() => setSearchOpen(true)}
        isLoading={chatsQuery.isPending}
      />
      <div
        className={`resize-handle${resizing ? ' dragging' : ''}`}
        role="separator"
        aria-orientation="vertical"
        aria-label={t('messenger.resizeLabel')}
        onPointerDown={onResizePointerDown}
      />
      {chatsQuery.isError && chats.length === 0 ? (
        <main className="chat-area">
          <EmptyState title={t('messenger.loadFailed')} hint={t('messenger.checkConnection')} />
          <div className="retry-row">
            <button
              className="ghost-btn inline"
              onClick={() => void queryClient.invalidateQueries({ queryKey: ['chats'] })}
            >
              {t('common.retry')}
            </button>
          </div>
        </main>
      ) : active ? (
        <ChatWindow
          key={active.id}
          chat={active}
          chats={chats}
          myUserId={myUserId}
          typingNames={typingNames}
          sendTyping={sendTyping}
          ackDelivered={ackDelivered}
          peerOnline={activePeerId !== null && isOnline(activePeerId)}
          peerLastSeen={activePeerId !== null ? lastSeenAt(activePeerId) : null}
          detailsOpen={detailsOpen}
          onToggleDetails={() => setDetailsOpen((open) => !open)}
          onBack={() => setActiveId(null)}
          onOpenSearch={() => setSearchOpen(true)}
        />
      ) : (
        <main className="chat-area">
          <EmptyState
            title={chatsQuery.isPending ? t('common.loading') : t('messenger.selectConversation')}
            hint={
              chatsQuery.isPending
                ? t('messenger.fetching')
                : t('messenger.chooseHint')
            }
            icon={chatsQuery.isPending ? undefined : 'users'}
          />
        </main>
      )}
      {detailsOpen && active ? (
        <ChatDetails
          chat={active}
          myUserId={myUserId}
          onClose={() => setDetailsOpen(false)}
          onLeft={() => {
            setActiveId(null);
            setDetailsOpen(false);
          }}
        />
      ) : null}
      {newChatOpen ? (
        <NewChatDialog
          myUserId={myUserId}
          onClose={() => setNewChatOpen(false)}
          onCreated={(id) => {
            setNewChatOpen(false);
            setActiveId(id);
          }}
        />
      ) : null}
      {joinOpen ? (
        <JoinChannelDialog
          onClose={() => setJoinOpen(false)}
          onJoined={(id) => {
            setJoinOpen(false);
            setActiveId(id);
          }}
        />
      ) : null}
      {searchOpen ? (
        <SearchDialog myUserId={myUserId} onOpenChat={openChat} onClose={() => setSearchOpen(false)} />
      ) : null}
      <Toasts toasts={toasts} onOpen={openChat} onDismiss={dismissToast} />
      <CallOverlayMount chats={chats} myUserId={myUserId} />
      <span data-hub-status={status} hidden />
    </div>
  );
}

// LiveKit client (~200 KB) loads on first call, not with the initial bundle.
const LazyCallOverlay = lazy(() =>
  import('../widgets/CallOverlay').then((m) => ({ default: m.CallOverlay })),
);

function CallOverlayMount({ chats, myUserId }: { chats: Chat[]; myUserId: string | undefined }) {
  const needed = useCallStore((s) => s.active !== null || s.incoming !== null);
  if (!needed) return null;
  return (
    <Suspense fallback={null}>
      <LazyCallOverlay chats={chats} myUserId={myUserId} />
    </Suspense>
  );
}
