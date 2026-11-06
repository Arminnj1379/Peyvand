import { Fragment, useEffect, useMemo, useRef, useState } from 'react';
import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query';
import type { InfiniteData } from '@tanstack/react-query';
import { chatApi } from '../features/chats/chatApi';
import { chatDisplayName, chatPeerId } from '../features/chats/chatTypes';
import type { Chat } from '../features/chats/chatTypes';
import { callApi } from '../features/calls/callApi';
import { useCallStore } from '../features/calls/callStore';
import { fileApi } from '../features/files/fileApi';
import { formatSize, toAttachment } from '../features/files/fileTypes';
import type { Attachment, UploadedFile } from '../features/files/fileTypes';
import { formatVoiceTime, useVoiceRecorder } from '../features/files/useVoiceRecorder';
import { messageApi } from '../features/messages/messageApi';
import { dayKey, firstUrl, formatDayLabel, formatLastSeen, formatTime, linkDomain } from '../features/messages/messageTypes';
import { ApiError } from '../shared/api/client';
import type { HistoryPage, Message } from '../features/messages/messageTypes';
import { UserAvatar } from '../shared/ui/UserAvatar';
import { MessageBody } from '../shared/ui/MessageBody';
import { useUserQuery } from '../features/auth/useUser';
import { EmptyState } from '../shared/ui/EmptyState';
import { Icon } from '../shared/ui/Icon';
import { useEscape } from '../shared/ui/useEscape';
import { AttachmentView, MediaViewer } from './MediaViewer';

const PAGE_SIZE = 30;
const AVATAR_COLORS = ['#2aabee', '#7b61ff', '#31b545', '#e8a13d', '#df3f40', '#009688'];
const QUICK_EMOJIS = ['👍', '❤️', '😂', '😮', '😢', '🙏'];
const MORE_EMOJIS = ['😎', '🤔', '👏', '🎉', '🔥', '💯', '😡', '👎', '🤝', '🤣', '😱', '🥳'];

/** OS picker hint covering every backend-supported extension (server still validates). */
const ACCEPT_ALL =
  '.jpg,.jpeg,.png,.webp,.gif,.mp4,.webm,.mov,.mp3,.wav,.ogg,.oga,.weba,.m4a,.opus,.aac,.flac,.pdf,.txt,.csv,.md,.zip,.doc,.docx,.xls,.xlsx,.ppt,.pptx';

function colorFor(id: string): string {
  let h = 0;
  for (const c of id) h = (h * 31 + c.charCodeAt(0)) % 997;
  return AVATAR_COLORS[h % AVATAR_COLORS.length];
}

function newClientId(): string {
  return typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID().replace(/-/g, '')
    : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`;
}

function replaceByClientId(
  old: InfiniteData<HistoryPage> | undefined,
  clientId: string,
  next: Message,
): InfiniteData<HistoryPage> | undefined {
  if (!old) return old;
  return {
    ...old,
    pages: old.pages.map((p) => ({
      ...p,
      items: p.items.map((x) => (x.clientMessageId === clientId ? next : x)),
    })),
  };
}

function markFailed(
  old: InfiniteData<HistoryPage> | undefined,
  clientId: string,
): InfiniteData<HistoryPage> | undefined {
  if (!old) return old;
  return {
    ...old,
    pages: old.pages.map((p) => ({
      ...p,
      items: p.items.map((x) => (x.clientMessageId === clientId ? { ...x, sending: false, failed: true } : x)),
    })),
  };
}

function patchById(
  old: InfiniteData<HistoryPage> | undefined,
  next: Message,
): InfiniteData<HistoryPage> | undefined {
  if (!old) return old;
  return {
    ...old,
    pages: old.pages.map((p) => ({
      ...p,
      items: p.items.map((x) => (x.id === next.id ? next : x)),
    })),
  };
}

export function ChatWindow({
  chat,
  chats,
  myUserId,
  typingNames,
  sendTyping,
  ackDelivered,
  peerOnline,
  peerLastSeen,
  detailsOpen,
  onToggleDetails,
  onBack,
  onOpenSearch,
}: {
  chat: Chat;
  chats: Chat[];
  myUserId: string | undefined;
  typingNames: string[];
  sendTyping: (chatId: string, isTyping: boolean) => void;
  ackDelivered: (chatId: string, seq: number) => void;
  peerOnline: boolean;
  peerLastSeen: string | null;
  detailsOpen: boolean;
  onToggleDetails: () => void;
  onBack: () => void;
  onOpenSearch: () => void;
}) {
  const queryClient = useQueryClient();
  const listRef = useRef<HTMLDivElement | null>(null);
  const stickToBottom = useRef(true);
  const [draft, setDraft] = useState('');
  const [replyTo, setReplyTo] = useState<Message | null>(null);
  const [editing, setEditing] = useState<Message | null>(null);
  const [menu, setMenu] = useState<{ msg: Message; x: number; y: number } | null>(null);
  const [forwardMsg, setForwardMsg] = useState<Message | null>(null);
  const [forwardMany, setForwardMany] = useState<Message[] | null>(null);
  const [sending, setSending] = useState(false);
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [threadParent, setThreadParent] = useState<Message | null>(null);
  const [pending, setPending] = useState<{ meta: UploadedFile; progress: number; previewUrl: string | null } | null>(null);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [dragging, setDragging] = useState(false);
  const [viewer, setViewer] = useState<Attachment | null>(null);
  const [callBusy, setCallBusy] = useState(false);
  const [callError, setCallError] = useState<string | null>(null);
  const [emojiOpen, setEmojiOpen] = useState(false);
  // Advanced-messaging UI state: delete confirmations, action errors, forward progress.
  const [deleteTarget, setDeleteTarget] = useState<Message | null>(null);
  const [bulkConfirm, setBulkConfirm] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [bulkDeleting, setBulkDeleting] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [forwarding, setForwarding] = useState(false);
  const [forwardError, setForwardError] = useState<string | null>(null);
  const [flashId, setFlashId] = useState<string | null>(null);
  const flashTimer = useRef<number | null>(null);
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const gifInputRef = useRef<HTMLInputElement | null>(null);
  const composerRef = useRef<HTMLTextAreaElement | null>(null);
  const voice = useVoiceRecorder();
  const [showScrollFab, setShowScrollFab] = useState(false);
  const [belowCount, setBelowCount] = useState(0);
  const prevLenRef = useRef(0);
  const lastAckedSeq = useRef(0);
  const typingTimer = useRef<number | null>(null);

  const forwardOpen = forwardMsg !== null || forwardMany !== null;
  useEscape(menu !== null, () => setMenu(null));
  useEscape(deleteTarget !== null, () => { if (!deleting) setDeleteTarget(null); });
  useEscape(bulkConfirm, () => { if (!bulkDeleting) setBulkConfirm(false); });
  useEscape(forwardOpen, () => {
    setForwardMsg(null);
    setForwardMany(null);
  });
  useEscape(threadParent !== null, () => setThreadParent(null));

  const name = chatDisplayName(chat, myUserId);
  const myRole = chat.members.find((m) => m.userId === myUserId)?.role;
  const canPost = chat.type !== 'Channel' || myRole === 'Owner' || myRole === 'Admin';
  const isAdmin = myRole === 'Owner' || myRole === 'Admin';
  const selecting = selected.size > 0;

  const history = useInfiniteQuery({
    queryKey: ['history', chat.id],
    queryFn: ({ pageParam, signal }) =>
      messageApi.history(chat.id, (pageParam as number | undefined) ?? undefined, PAGE_SIZE, signal),
    initialPageParam: undefined as number | undefined,
    getNextPageParam: (last) => (last.hasMore ? (last.nextCursor ?? undefined) : undefined),
    staleTime: 10_000,
  });

  const pinsQuery = useQuery({
    queryKey: ['pins', chat.id],
    queryFn: ({ signal }) => chatApi.pins(chat.id, signal),
    staleTime: 30_000,
  });
  const pins = pinsQuery.data ?? [];

  const threadQuery = useQuery({
    queryKey: ['thread', threadParent?.id],
    queryFn: ({ signal }) => messageApi.replies(threadParent!.id, 50, signal),
    enabled: threadParent !== null,
    staleTime: 10_000,
  });

  const flat: Message[] = useMemo(() => {
    const pages = history.data?.pages ?? [];
    // Pages arrive newest-first; each page is oldest-first. Reverse for chronological view.
    return [...pages].reverse().flatMap((p) => p.items);
  }, [history.data]);

  const byId = useMemo(() => new Map(flat.map((m) => [m.id, m])), [flat]);

  const peerMaxRead = useMemo(() => {
    let max = 0;
    for (const m of chat.members) {
      if (m.userId !== myUserId) max = Math.max(max, m.lastReadSeq);
    }
    return max;
  }, [chat.members, myUserId]);

  const peerMaxDelivered = useMemo(() => {
    let max = 0;
    for (const m of chat.members) {
      if (m.userId !== myUserId) max = Math.max(max, m.lastDeliveredSeq);
    }
    return max;
  }, [chat.members, myUserId]);

  // Stored last-seen fallback for DM peers never observed online this session.
  // Shared profile cache with the list/details panels (one request per user).
  const peerId = chatPeerId(chat, myUserId);
  const peerProfile = useUserQuery(peerId);

  // Reset per-chat transient state on switch.
  useEffect(() => {
    setDraft('');
    setFlashId(null);
    setEmojiOpen(false);
    setReplyTo(null);
    setEditing(null);
    setMenu(null);
    setForwardMsg(null);
    setForwardMany(null);
    setSelected(new Set());
    setThreadParent(null);
    setViewer(null);
    setUploadError(null);
    setCallError(null);
    setActionError(null);
    setDeleteTarget(null);
    setBulkConfirm(false);
    setForwardError(null);
    setForwarding(false);
    setDeleting(false);
    setBulkDeleting(false);
    setUploading(false);
    setDragging(false);
    setShowScrollFab(false);
    setBelowCount(0);
    prevLenRef.current = 0;
    setPending((prev) => {
      if (prev?.previewUrl) URL.revokeObjectURL(prev.previewUrl);
      return null;
    });
    lastAckedSeq.current = 0;
    stickToBottom.current = true;
    // Scroll to bottom on chat change once data lands.
    requestAnimationFrame(() => {
      listRef.current?.scrollTo({ top: listRef.current.scrollHeight });
    });
  }, [chat.id]);

  // Keep pinned to bottom when new messages arrive and the user is near the bottom.
  useEffect(() => {
    const el = listRef.current;
    if (!el || !stickToBottom.current) {
      if (flat.length > prevLenRef.current) {
        setBelowCount((c) => c + (flat.length - prevLenRef.current));
      }
      prevLenRef.current = flat.length;
      return;
    }
    el.scrollTop = el.scrollHeight;
    prevLenRef.current = flat.length;
  }, [flat.length]);

  // Advance the read watermark for what is on screen.
  useEffect(() => {
    const lastPeer = [...flat].reverse().find((m) => m.senderId !== myUserId && !m.isDeleted);
    if (!lastPeer || lastPeer.seq <= chat.lastReadSeq) return;
    const t = window.setTimeout(() => {
      void chatApi.markRead(chat.id, lastPeer.seq).catch(() => undefined);
      queryClient.setQueryData<Chat[]>(['chats'], (old) =>
        old?.map((c) =>
          c.id === chat.id ? { ...c, unreadCount: 0, lastReadSeq: lastPeer.seq } : c,
        ),
      );
    }, 600);
    return () => window.clearTimeout(t);
  }, [flat, chat.id, chat.lastReadSeq, myUserId, queryClient]);

  // Delivery acknowledgement: as soon as peer messages are on screen, confirm
  // receipt so senders see delivered ticks. Monotonic per chat (no duplicates).
  useEffect(() => {
    let maxSeq = 0;
    for (const m of flat) {
      if (m.senderId !== myUserId && !m.isDeleted && m.seq > 0) maxSeq = Math.max(maxSeq, m.seq);
    }
    if (maxSeq > lastAckedSeq.current) {
      lastAckedSeq.current = maxSeq;
      ackDelivered(chat.id, maxSeq);
    }
  }, [flat, chat.id, myUserId, ackDelivered]);

  // Clear the landing highlight timer on unmount.
  useEffect(() => () => {
    if (flashTimer.current) window.clearTimeout(flashTimer.current);
  }, []);

  // Collapse the auto-growing composer once the draft is cleared (send / chat switch).
  useEffect(() => {
    if (draft === '' && composerRef.current) composerRef.current.style.height = 'auto';
  }, [draft]);

  function onScroll(): void {
    const el = listRef.current;
    if (!el) return;
    const nearBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 160;
    stickToBottom.current = nearBottom;
    setShowScrollFab(!nearBottom);
    if (nearBottom) setBelowCount(0);
    if (el.scrollTop < 120 && history.hasNextPage && !history.isFetchingNextPage) {
      const prevHeight = el.scrollHeight;
      void history.fetchNextPage().then(() => {
        const cur = listRef.current;
        if (cur) cur.scrollTop = cur.scrollHeight - prevHeight + cur.scrollTop;
      });
    }
  }

  function scrollToBottom(): void {
    const el = listRef.current;
    if (!el) return;
    el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' });
    stickToBottom.current = true;
    setShowScrollFab(false);
    setBelowCount(0);
  }

  function pushTyping(isTyping: boolean): void {
    sendTyping(chat.id, isTyping);
    if (typingTimer.current) window.clearTimeout(typingTimer.current);
    if (isTyping) {
      typingTimer.current = window.setTimeout(() => sendTyping(chat.id, false), 3000);
    }
  }

  /** Starts a call and joins media immediately (outgoing interface). */
  async function startCall(mediaType: 'Audio' | 'Video'): Promise<void> {
    if (callBusy) return;
    setCallBusy(true);
    setCallError(null);
    try {
      const call = await callApi.start(chat.id, mediaType);
      const join = await callApi.join(call.id);
      useCallStore.getState().setActive(join.call, join);
    } catch (e) {
      // Busy: someone already rings — join the live call instead of failing.
      if (e instanceof ApiError && e.status === 409) {
        try {
          const live = (await callApi.list(chat.id, 5)).find((c) => c.status !== 'Ended');
          if (live) {
            const join = await callApi.join(live.id);
            useCallStore.getState().setActive(join.call, join);
            return;
          }
        } catch {
          // Fall through to the busy message below.
        }
        setCallError('This conversation already has an active call.');
      } else {
        setCallError(e instanceof ApiError ? e.message : 'Could not start the call.');
      }
    } finally {
      setCallBusy(false);
    }
  }

  function autoresizeComposer(): void {
    const ta = composerRef.current;
    if (!ta) return;
    ta.style.height = 'auto';
    ta.style.height = `${Math.min(ta.scrollHeight, 160)}px`;
  }

  function clearPending(): void {
    setPending((prev) => {
      if (prev?.previewUrl) URL.revokeObjectURL(prev.previewUrl);
      return null;
    });
  }

  /** Upload-then-stage: bytes go to object storage first, the id attaches on send. */
  async function uploadAndStage(file: File): Promise<void> {
    if (uploading || !canPost) return;
    setUploadError(null);
    setUploading(true);
    const previewUrl = file.type.startsWith('image/') ? URL.createObjectURL(file) : null;
    setPending({ meta: { id: '', fileName: file.name, kind: 'Document', contentType: file.type, sizeBytes: file.size, sha256: '', createdAt: '' }, progress: 0, previewUrl });
    try {
      const meta = await fileApi.upload(file, (ratio) => {
        setPending((prev) => (prev ? { ...prev, progress: ratio } : prev));
      });
      setPending({ meta, progress: 1, previewUrl });
    } catch (e) {
      if (previewUrl) URL.revokeObjectURL(previewUrl);
      setPending(null);
      setUploadError(e instanceof ApiError ? e.message : 'Upload failed.');
    } finally {
      setUploading(false);
    }
  }

  async function submit(e?: React.FormEvent): Promise<void> {
    e?.preventDefault();
    const text = draft.trim();
    if ((!text && !pending?.meta.id) || sending || !canPost || uploading) return;
    if (editing) {
      setSending(true);
      setActionError(null);
      try {
        const saved = await messageApi.edit(editing.id, text);
        queryClient.setQueryData<InfiniteData<HistoryPage>>(['history', chat.id], (old) =>
          patchById(old, saved),
        );
        setEditing(null);
        setDraft('');
      } catch (e) {
        // Composer stays open with the text intact; surface the reason above it.
        setActionError(e instanceof ApiError ? e.message : 'Could not save the edited message.');
      } finally {
        setSending(false);
      }
      return;
    }
    const clientMessageId = newClientId();
    const staged = pending?.meta.id ? pending.meta : null;
    const optimistic: Message = {
      id: `tmp-${clientMessageId}`,
      chatId: chat.id,
      seq: -Date.now(),
      clientMessageId,
      senderId: myUserId ?? null,
      senderUsername: null,
      senderName: 'You',
      kind: replyTo ? 'Reply' : 'Text',
      body: text,
      replyTo: replyTo
        ? { messageId: replyTo.id, senderName: replyTo.senderName, preview: replyTo.body.slice(0, 140), isDeleted: false }
        : null,
      forwardFrom: null,
      isEdited: false,
      editedAt: null,
      isDeleted: false,
      createdAt: new Date().toISOString(),
      reactions: [],
      replyCount: 0,
      attachment: staged ? toAttachment(staged) : null,
      sending: true,
    };
    queryClient.setQueryData<InfiniteData<HistoryPage>>(['history', chat.id], (old) => {
      if (!old) return { pages: [{ items: [optimistic], nextCursor: null, hasMore: false }], pageParams: [undefined] };
      const [first, ...rest] = old.pages;
      if (!first) return old;
      return { ...old, pages: [{ ...first, items: [...first.items, optimistic] }, ...rest] };
    });
    setDraft('');
    const replyId = replyTo?.id;
    const fileId = staged?.id;
    setReplyTo(null);
    pushTyping(false);
    stickToBottom.current = true;
    setSending(true);
    // The staged upload now belongs to this send (bubble retry reuses its file id).
    clearPending();
    try {
      const saved = await messageApi.send(
        chat.id,
        {
          body: text,
          clientMessageId,
          ...(replyId ? { replyToMessageId: replyId } : {}),
          ...(fileId ? { fileId } : {}),
        },
      );
      queryClient.setQueryData<InfiniteData<HistoryPage>>(['history', chat.id], (old) =>
        replaceByClientId(old, clientMessageId, saved),
      );
      queryClient.setQueryData<Chat[]>(['chats'], (old) =>
        old?.map((c) =>
          c.id === chat.id
            ? { ...c, lastMessageAt: saved.createdAt, lastMessagePreview: saved.body.slice(0, 140), seqCounter: Math.max(c.seqCounter, saved.seq) }
            : c,
        ),
      );
    } catch {
      queryClient.setQueryData<InfiniteData<HistoryPage>>(['history', chat.id], (old) =>
        markFailed(old, clientMessageId),
      );
    } finally {
      setSending(false);
    }
  }

  async function retry(msg: Message): Promise<void> {
    queryClient.setQueryData<InfiniteData<HistoryPage>>(['history', chat.id], (old) => {
      if (!old) return old;
      return {
        ...old,
        pages: old.pages.map((p) => ({
          ...p,
          items: p.items.map((x) =>
            x.clientMessageId === msg.clientMessageId ? { ...x, sending: true, failed: false } : x,
          ),
        })),
      };
    });
    try {
      const saved = await messageApi.send(chat.id, {
        body: msg.body,
        clientMessageId: msg.clientMessageId,
        ...(msg.attachment ? { fileId: msg.attachment.fileId } : {}),
      });
      queryClient.setQueryData<InfiniteData<HistoryPage>>(['history', chat.id], (old) =>
        replaceByClientId(old, msg.clientMessageId, saved),
      );
    } catch {
      queryClient.setQueryData<InfiniteData<HistoryPage>>(['history', chat.id], (old) =>
        markFailed(old, msg.clientMessageId),
      );
    }
  }

  /** Deletes a single message for everyone (server-side global delete). */
  async function confirmDelete(): Promise<void> {
    if (!deleteTarget || deleting) return;
    setDeleting(true);
    setActionError(null);
    try {
      const saved = await messageApi.remove(deleteTarget.id);
      queryClient.setQueryData<InfiniteData<HistoryPage>>(['history', chat.id], (old) =>
        patchById(old, saved),
      );
      setDeleteTarget(null);
    } catch (e) {
      setActionError(e instanceof ApiError ? e.message : 'Could not delete the message.');
    } finally {
      setDeleting(false);
    }
  }

  async function react(msg: Message, emoji: string): Promise<void> {
    try {
      const saved = await messageApi.toggleReaction(msg.id, emoji);
      queryClient.setQueryData<InfiniteData<HistoryPage>>(['history', chat.id], (old) =>
        patchById(old, saved),
      );
    } catch (e) {
      setActionError(e instanceof ApiError ? e.message : 'Could not update the reaction.');
    }
  }

  /** Bulk delete for the selection toolbar. Runs only from the confirm dialog. */
  async function confirmBulkDelete(): Promise<void> {
    const ids = [...selected];
    if (ids.length === 0 || bulkDeleting) return;
    setBulkDeleting(true);
    setActionError(null);
    try {
      const deleted = await messageApi.bulkDelete(ids);
      const byDeletedId = new Map(deleted.map((m) => [m.id, m]));
      queryClient.setQueryData<InfiniteData<HistoryPage>>(['history', chat.id], (old) => {
        if (!old) return old;
        return {
          ...old,
          pages: old.pages.map((p) => ({
            ...p,
            items: p.items.map((x) => byDeletedId.get(x.id) ?? x),
          })),
        };
      });
      setSelected(new Set());
      setBulkConfirm(false);
    } catch (e) {
      // Leave selection intact so the user can retry.
      setActionError(e instanceof ApiError ? e.message : 'Could not delete the selected messages.');
    } finally {
      setBulkDeleting(false);
    }
  }

  async function togglePin(msg: Message): Promise<void> {
    setMenu(null);
    setActionError(null);
    try {
      if (chat.pinnedMessageIds.includes(msg.id)) {
        await chatApi.unpin(chat.id, msg.id);
      } else {
        await chatApi.pin(chat.id, msg.id);
      }
      await queryClient.invalidateQueries({ queryKey: ['chats'] });
      await pinsQuery.refetch();
    } catch (e) {
      setActionError(e instanceof ApiError ? e.message : 'Could not update the pinned message.');
    }
  }

  async function forwardTo(targetId: string): Promise<void> {
    const items = forwardMany ?? (forwardMsg ? [forwardMsg] : []);
    if (items.length === 0 || forwarding) return;
    setForwarding(true);
    setForwardError(null);
    try {
      for (const item of items) {
        await messageApi.forward({
          sourceChatId: item.chatId,
          sourceMessageId: item.id,
          targetChatId: targetId,
          clientMessageId: newClientId(),
        });
      }
      setForwardMsg(null);
      setForwardMany(null);
      setSelected(new Set());
      if (targetId !== chat.id) {
        await queryClient.invalidateQueries({ queryKey: ['chats'] });
      }
    } catch (e) {
      // Leave the picker open so the user can try another target.
      setForwardError(e instanceof ApiError ? e.message : 'Could not forward the message.');
    } finally {
      setForwarding(false);
    }
  }

  function openMenu(e: React.MouseEvent, msg: Message): void {
    e.preventDefault();
    setMenu({ msg, x: e.clientX, y: e.clientY });
  }

  function toggleSelect(id: string): void {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  /** Jump to the original message of a reply (same-chat, already loaded). */
  function jumpToMessage(id: string): void {
    const el = document.getElementById(`msg-${id}`);
    if (!el) return;
    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    setFlashId(id);
    if (flashTimer.current) window.clearTimeout(flashTimer.current);
    flashTimer.current = window.setTimeout(() => setFlashId(null), 1600);
  }

  const dmStatus = peerOnline
    ? 'online'
    : formatLastSeen(peerLastSeen ?? peerProfile.data?.lastSeenAt ?? null);

  const statusLine =
    typingNames.length > 0
      ? `${typingNames.length === 1 ? 'typing' : `${typingNames.length} typing`}…`
      : chat.type === 'Channel'
        ? `${chat.memberCount} subscriber${chat.memberCount === 1 ? '' : 's'}${chat.isPublic && chat.username ? ` · @${chat.username}` : ''}`
        : chat.type === 'Group'
          ? `${chat.memberCount} member${chat.memberCount === 1 ? '' : 's'}`
          : dmStatus;

  const forwardPickerItems = forwardMany ?? (forwardMsg ? [forwardMsg] : null);

  return (
    <main className="chat-area" aria-label={`Chat with ${name}`}>
      <header className="chat-header">
        <button className="icon-btn back-btn" onClick={onBack} aria-label="Back to list">
          <Icon name="back" />
        </button>
        <UserAvatar
          avatarUrl={chat.type === 'Dm' ? (peerProfile.data?.avatarUrl ?? null) : chat.avatarUrl}
          name={name}
          color={colorFor(chat.id)}
          size={42}
          online={chat.type === 'Dm' && peerOnline}
        />
        <div className="chat-peer">
          <div className="chat-peer-name" dir="auto">{name}</div>
          <div className="chat-peer-status">
            {chat.type === 'Dm' && peerOnline ? <span className="online-dot" aria-hidden="true" /> : null}
            {statusLine}
          </div>
        </div>
        <div className="chat-actions">
          <button
            className="icon-btn"
            onClick={onOpenSearch}
            aria-label="Search in conversation"
            title="Search"
          >
            <Icon name="search" />
          </button>
          <button
            className="icon-btn"
            onClick={() => void startCall('Audio')}
            disabled={callBusy}
            aria-label="Start voice call"
            title="Voice call"
          >
            <Icon name="phone" />
          </button>
          <button
            className="icon-btn"
            onClick={() => void startCall('Video')}
            disabled={callBusy}
            aria-label="Start video call"
            title="Video call"
          >
            <Icon name="video" />
          </button>
          <button
            className="icon-btn"
            onClick={onToggleDetails}
            aria-label="Conversation info"
            aria-pressed={detailsOpen}
            title="More"
          >
            <Icon name="dots" />
          </button>
        </div>
      </header>
      {callError ? (
        <div className="call-error" role="alert">
          <span>{callError}</span>
          <button className="icon-btn" aria-label="Dismiss" onClick={() => setCallError(null)}>
            <Icon name="close" size={14} />
          </button>
        </div>
      ) : null}
      {actionError ? (
        <div className="action-error" role="alert">
          <span>{actionError}</span>
          <button className="icon-btn" aria-label="Dismiss" onClick={() => setActionError(null)}>
            <Icon name="close" size={14} />
          </button>
        </div>
      ) : null}

      {pins.length > 0 ? (
        <div className="pin-bar" role="note">
          <Icon name="pin" size={15} />
          <span className="pin-text">{pins[pins.length - 1]?.body.slice(0, 120) ?? 'Pinned message'}</span>
          <span className="pin-count">{pins.length} pinned</span>
        </div>
      ) : null}

      {selecting ? (
        <div className="selection-bar" role="toolbar" aria-label="Message selection">
          <span>{selected.size} selected</span>
          <span className="selection-actions">
            <button
              className="ghost-btn inline"
              onClick={() => {
                const items = flat.filter((m) => selected.has(m.id) && !m.isDeleted);
                if (items.length > 0) setForwardMany(items);
              }}
            >
              Forward
            </button>
            <button className="ghost-btn inline danger-text" onClick={() => setBulkConfirm(true)}>
              Delete
            </button>
            <button className="ghost-btn inline" onClick={() => setSelected(new Set())}>
              Cancel
            </button>
          </span>
        </div>
      ) : null}

      <div
        className={`message-list${dragging ? ' dragging' : ''}`}
        ref={listRef}
        onScroll={onScroll}
        onDragEnter={(e) => {
          e.preventDefault();
          if (canPost && e.dataTransfer.types.includes('Files')) setDragging(true);
        }}
        onDragOver={(e) => {
          e.preventDefault();
          if (canPost && e.dataTransfer.types.includes('Files')) setDragging(true);
        }}
        onDragLeave={(e) => {
          e.preventDefault();
          if (e.currentTarget === e.target) setDragging(false);
        }}
        onDrop={(e) => {
          e.preventDefault();
          setDragging(false);
          if (!canPost) return;
          const file = e.dataTransfer.files?.[0];
          if (file) void uploadAndStage(file);
        }}
      >
        {dragging ? (
          <div className="drop-overlay" aria-hidden="true">
            <span>Drop to attach</span>
          </div>
        ) : null}
        {history.isPending ? (
          <EmptyState title="Loading messages" hint="Fetching history from the server…" />
        ) : null}
        {history.isError ? (
          <div className="history-error">
            <p>Could not load history.</p>
            <button className="ghost-btn inline" onClick={() => void history.refetch()}>
              Retry
            </button>
          </div>
        ) : null}
        {history.hasNextPage ? (
          <button
            className="ghost-btn inline load-older"
            onClick={() => void history.fetchNextPage()}
            disabled={history.isFetchingNextPage}
          >
            {history.isFetchingNextPage ? 'Loading…' : 'Load older messages'}
          </button>
        ) : null}
        {!history.isPending && flat.length === 0 ? (
          <EmptyState
            title="No messages yet"
            hint="Say hello — it will appear here instantly."
            icon="emoji"
          />
        ) : null}
        {flat.map((m, i) => {
          const prev = flat[i - 1];
          const showDay = !prev || dayKey(prev.createdAt) !== dayKey(m.createdAt);
          const mine = m.senderId === myUserId;
          // Group consecutive simple text messages from one sender (<5 min apart).
          const prevGap = prev ? new Date(m.createdAt).getTime() - new Date(prev.createdAt).getTime() : Infinity;
          const simple = !m.isDeleted && !m.replyTo && !m.forwardFrom && !m.attachment && m.reactions.length === 0;
          const prevSimple =
            prev !== undefined &&
            !prev.isDeleted &&
            !prev.replyTo &&
            !prev.forwardFrom &&
            !prev.attachment &&
            prev.reactions.length === 0;
          const grouped =
            !showDay &&
            prev !== undefined &&
            prev.senderId === m.senderId &&
            simple &&
            prevSimple &&
            prevGap >= 0 &&
            prevGap < 5 * 60 * 1000;
          // Tail on the last bubble of a group: the next message starts a new group.
          const next = flat[i + 1];
          const nextGap = next ? new Date(next.createdAt).getTime() - new Date(m.createdAt).getTime() : Infinity;
          const nextSimple =
            next !== undefined &&
            !next.isDeleted &&
            !next.replyTo &&
            !next.forwardFrom &&
            !next.attachment &&
            next.reactions.length === 0;
          const continues =
            next !== undefined &&
            next.senderId === m.senderId &&
            simple &&
            nextSimple &&
            nextGap >= 0 &&
            nextGap < 5 * 60 * 1000 &&
            dayKey(next.createdAt) === dayKey(m.createdAt);
          const isTail = !continues;
          // Image/video-only messages render chromeless; the time rides as a chip overlay.
          const mediaOnly =
            !m.isDeleted &&
            m.attachment !== null &&
            (m.attachment.kind === 'Image' || m.attachment.kind === 'Video') &&
            m.body.trim() === '' &&
            !m.replyTo &&
            !m.forwardFrom &&
            m.reactions.length === 0;
          const showName = !mine && chat.type === 'Group' && !grouped;
          const read = mine && m.seq > 0 && m.seq <= peerMaxRead;
          const delivered = mine && !read && m.seq > 0 && m.seq <= peerMaxDelivered;
          const url = m.isDeleted ? null : firstUrl(m.body);
          return (
            <Fragment key={m.id}>
              {showDay ? <div className="day-divider" role="separator" aria-label={formatDayLabel(m.createdAt)}>{formatDayLabel(m.createdAt)}</div> : null}
              <div id={`msg-${m.id}`} className={`bubble-row${mine ? ' mine' : ' theirs'}${grouped ? ' grouped' : ''}`}>
                {selecting ? (
                  <input
                    type="checkbox"
                    className="select-box"
                    aria-label="Select message"
                    checked={selected.has(m.id)}
                    onChange={() => toggleSelect(m.id)}
                  />
                ) : null}
                <div
                  className={`bubble${m.sending ? ' sending' : ''}${m.failed ? ' failed' : ''}${flashId === m.id ? ' flash' : ''}${isTail ? ' tail' : ''}${mediaOnly ? ' bubble-media' : ''}`}
                  onContextMenu={(e) => openMenu(e, m)}
                  role="article"
                  aria-label={mine ? 'Your message' : `Message from ${m.senderName ?? 'peer'}`}
                >
                  {showName ? <div className="bubble-sender" style={{ color: colorFor(m.senderId ?? '?') }}>{m.senderName ?? m.senderUsername ?? 'Unknown'}</div> : null}
                  {m.forwardFrom ? (
                    <div className="bubble-forward">
                      <Icon name="forward" size={12} />
                      Forwarded message
                    </div>
                  ) : null}
                  {m.replyTo ? (
                    m.replyTo.isDeleted || !byId.has(m.replyTo.messageId) ? (
                      <div
                        className="bubble-reply"
                        title={m.replyTo.isDeleted ? 'Original message was deleted' : 'Original message is not loaded'}
                      >
                        <span className="bubble-reply-name">{m.replyTo.senderName ?? 'Message'}</span>
                        <span className="bubble-reply-text" dir="auto">
                          {m.replyTo.isDeleted ? 'Message deleted' : m.replyTo.preview}
                        </span>
                      </div>
                    ) : (
                      <button
                        type="button"
                        className="bubble-reply"
                        title="Go to original message"
                        onClick={() => jumpToMessage(m.replyTo!.messageId)}
                      >
                        <span className="bubble-reply-name">{m.replyTo.senderName ?? 'Message'}</span>
                        <span className="bubble-reply-text" dir="auto">{m.replyTo.preview}</span>
                      </button>
                    )
                  ) : null}
                  <span dir="auto">{m.isDeleted ? <i>Message deleted</i> : <MessageBody body={m.body} />}</span>
                  {!m.isDeleted && m.attachment ? (
                    <AttachmentView message={m} onOpenImage={setViewer} />
                  ) : null}
                  {url ? (
                    <a
                      className="link-card"
                      href={url}
                      target="_blank"
                      rel="noreferrer"
                      onClick={(e) => e.stopPropagation()}
                    >
                      <span className="link-domain">{linkDomain(url)}</span>
                      <span className="link-url">{url.length > 60 ? `${url.slice(0, 60)}…` : url}</span>
                    </a>
                  ) : null}
                  {m.replyCount > 0 ? (
                    <button className="thread-link" onClick={() => setThreadParent(m)}>
                      {m.replyCount} {m.replyCount === 1 ? 'reply' : 'replies'}
                      <Icon name="chevron" size={12} />
                    </button>
                  ) : null}
                  {m.reactions.length > 0 ? (
                    <div className="reaction-row">
                      {m.reactions.map((r) => (
                        <button
                          key={r.emoji}
                          className={`reaction-chip${r.mine ? ' mine' : ''}`}
                          onClick={() => void react(m, r.emoji)}
                          title={r.mine ? 'Remove your reaction' : 'React too'}
                          disabled={m.isDeleted}
                        >
                          {r.emoji} {r.count}
                        </button>
                      ))}
                    </div>
                  ) : null}
                  <div className={`bubble-meta${mediaOnly ? ' overlay' : ''}`}>
                    {m.isEdited && !m.isDeleted ? <span>edited</span> : null}
                    {!m.isDeleted && chat.pinnedMessageIds.includes(m.id) ? (
                      <span className="pin-badge" title="Pinned message">
                        <Icon name="pin" size={11} />
                        Pinned
                      </span>
                    ) : null}
                    <span>{m.seq > 0 ? formatTime(m.createdAt) : 'sending…'}</span>
                    {mine && m.seq > 0 ? (
                      <span
                        className={`ticks${delivered ? ' delivered' : ''}`}
                        aria-label={read ? 'Read' : delivered ? 'Delivered' : 'Sent'}
                      >
                        <Icon name={read || delivered ? 'checks' : 'check'} size={14} />
                      </span>
                    ) : null}
                  </div>
                  {m.failed ? (
                    <button className="ghost-btn inline retry-btn" onClick={() => void retry(m)}>
                      Retry send
                    </button>
                  ) : null}
                  <button
                    className="bubble-menu-btn"
                    aria-label="Message actions"
                    onClick={(e) => {
                      const r = (e.target as HTMLElement).getBoundingClientRect();
                      setMenu({ msg: m, x: r.left, y: r.bottom });
                    }}
                  >
                    <Icon name="dots" size={14} />
                  </button>
                </div>
              </div>
            </Fragment>
          );
        })}
        {typingNames.length > 0 ? (
          <div className="typing-row">
            <span className="typing-dots" aria-hidden="true">
              <i />
              <i />
              <i />
            </span>
            typing…
          </div>
        ) : null}
      </div>

      {showScrollFab ? (
        <button
          className="scroll-fab"
          onClick={scrollToBottom}
          aria-label={belowCount > 0 ? `Scroll to bottom, ${belowCount} new messages` : 'Scroll to bottom'}
          title="Scroll to bottom"
        >
          <Icon name="down" size={20} />
          {belowCount > 0 ? <span className="scroll-count">{belowCount > 99 ? '99+' : belowCount}</span> : null}
        </button>
      ) : null}

      <div className="composer-wrap">
      {replyTo ? (
        <div className="reply-bar">
          <div>
            <div className="reply-title">Replying to {replyTo.senderName ?? 'message'}</div>
            <div className="reply-preview">{replyTo.body.slice(0, 140)}</div>
          </div>
          <button className="icon-btn" aria-label="Cancel reply" onClick={() => setReplyTo(null)}>
            <Icon name="close" size={16} />
          </button>
        </div>
      ) : null}
      {editing ? (
        <div className="reply-bar editing">
          <div>
            <div className="reply-title">Editing message</div>
            <div className="reply-preview">{editing.body.slice(0, 140)}</div>
          </div>
          <button
            className="icon-btn"
            aria-label="Cancel edit"
            onClick={() => {
              setEditing(null);
              setDraft('');
            }}
          >
            <Icon name="close" size={16} />
          </button>
        </div>
      ) : null}

      {pending || uploadError ? (
        <div className="attach-bar">
          {pending ? (
            <div className="attach-preview">
              {pending.previewUrl ? (
                <img src={pending.previewUrl} alt={pending.meta.fileName} />
              ) : (
                <Icon name="file" size={28} />
              )}
              <span className="attach-meta">
                <strong>{pending.meta.fileName}</strong>
                <span className="hint">
                  {pending.meta.id
                    ? formatSize(pending.meta.sizeBytes)
                    : `Uploading… ${Math.round(pending.progress * 100)}%`}
                </span>
              </span>
              {uploading || !pending.meta.id ? (
                <span className="progress-track" aria-label="Upload progress">
                  <span className="progress-fill" style={{ width: `${Math.round(pending.progress * 100)}%` }} />
                </span>
              ) : null}
              <button className="icon-btn" aria-label="Remove attachment" onClick={clearPending}>
                <Icon name="close" size={16} />
              </button>
            </div>
          ) : null}
          {uploadError ? <div className="form-error">{uploadError}</div> : null}
        </div>
      ) : null}

      {voice.state === 'recording' ? (
        <div className="voice-bar" role="status" aria-label="Recording voice message">
          <span className="voice-dot" aria-hidden="true" />
          <span className="voice-time">{formatVoiceTime(voice.seconds)}</span>
          <span className="voice-hint">Recording…</span>
          <button
            type="button"
            className="icon-btn"
            aria-label="Cancel recording"
            title="Cancel"
            onClick={() => void voice.cancel()}
          >
            <Icon name="trash" size={18} />
          </button>
          <button
            type="button"
            className="send-btn"
            aria-label="Stop and attach voice message"
            title="Stop and attach"
            onClick={() =>
              void voice.stop().then((file) => {
                if (file) void uploadAndStage(file);
              })
            }
          >
            <Icon name="stop" size={16} />
          </button>
        </div>
      ) : null}

      {voice.error ? (
        <div className="form-error" role="alert">
          {voice.error}{' '}
          <button type="button" className="ghost-btn inline small" onClick={voice.clearError}>
            Dismiss
          </button>
        </div>
      ) : null}

      {canPost ? (
        <form className="composer" onSubmit={(e) => void submit(e)}>
          <input
            ref={fileInputRef}
            type="file"
            hidden
            aria-hidden="true"
            tabIndex={-1}
            accept={ACCEPT_ALL}
            onChange={(e) => {
              const file = e.target.files?.[0];
              e.target.value = '';
              if (file) void uploadAndStage(file);
            }}
          />
          <input
            ref={gifInputRef}
            type="file"
            hidden
            aria-hidden="true"
            tabIndex={-1}
            accept="image/gif"
            onChange={(e) => {
              const file = e.target.files?.[0];
              e.target.value = '';
              if (file) void uploadAndStage(file);
            }}
          />
          <button
            type="button"
            className="icon-btn"
            aria-label="Attach file"
            title="Attach file"
            disabled={uploading}
            onClick={() => fileInputRef.current?.click()}
          >
            <Icon name="attach" />
          </button>
          <button
            type="button"
            className="icon-btn"
            aria-label="Send GIF"
            title="Send GIF"
            disabled={uploading}
            onClick={() => gifInputRef.current?.click()}
          >
            <Icon name="gif" />
          </button>
          <textarea
            ref={composerRef}
            rows={1}
            className="field-input"
            value={draft}
            onChange={(e) => {
              setDraft(e.target.value);
              pushTyping(e.target.value.length > 0);
              autoresizeComposer();
            }}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
                e.preventDefault();
                void submit();
              }
            }}
            onBlur={() => pushTyping(false)}
            placeholder={editing ? 'Edit message…' : pending ? 'Add a caption…' : 'Message'}
            aria-label="Message composer"
            enterKeyHint="send"
            maxLength={4000}
          />
          <button
            type="button"
            className="icon-btn"
            aria-label="Insert emoji"
            title="Emoji"
            aria-expanded={emojiOpen}
            onClick={() => setEmojiOpen((v) => !v)}
          >
            <Icon name="emoji" />
          </button>
          {(draft.trim() !== '' || editing || pending?.meta.id) && canPost ? (
            <button
              className="send-btn"
              aria-label="Send"
              disabled={uploading}
            >
              <Icon name="send" />
            </button>
          ) : voice.state === 'recording' ? (
            <button
              type="button"
              className="send-btn recording"
              aria-label="Stop recording"
              title="Stop"
              onClick={() =>
                void voice.stop().then((file) => {
                  if (file) void uploadAndStage(file);
                })
              }
            >
              <Icon name="stop" size={16} />
            </button>
          ) : (
            <button
              type="button"
              className="send-btn mic-btn"
              aria-label="Record voice message"
              title="Voice message"
              disabled={uploading}
              onClick={() => void voice.start()}
            >
              <Icon name="mic" />
            </button>
          )}
          {emojiOpen ? (
            <div className="emoji-pop" role="dialog" aria-label="Insert emoji">
              {[...QUICK_EMOJIS, ...MORE_EMOJIS].map((e) => (
                <button
                  key={e}
                  type="button"
                  aria-label={`Insert ${e}`}
                  onClick={() => {
                    setDraft((d) => `${d}${e}`);
                    pushTyping(true);
                    requestAnimationFrame(autoresizeComposer);
                  }}
                >
                  {e}
                </button>
              ))}
            </div>
          ) : null}
        </form>
      ) : (
        <div className="composer readonly">Only admins can post in this channel.</div>
      )}
      </div>

      {viewer ? <MediaViewer attachment={viewer} onClose={() => setViewer(null)} /> : null}

      {menu ? (
        <div className="menu-backdrop" onClick={() => setMenu(null)}>
          <div
            className="context-menu wide"
            role="menu"
            style={{ left: Math.min(menu.x, window.innerWidth - 220), top: Math.min(menu.y, window.innerHeight - 340) }}
            onClick={(e) => e.stopPropagation()}
          >
            {!menu.msg.isDeleted ? (
              <div className="quick-react" role="group" aria-label="Quick reactions">
                {QUICK_EMOJIS.map((e) => (
                  <button
                    key={e}
                    className="quick-emoji"
                    aria-label={`React ${e}`}
                    onClick={() => {
                      setMenu(null);
                      void react(menu.msg, e);
                    }}
                  >
                    {e}
                  </button>
                ))}
              </div>
            ) : null}
            {!menu.msg.isDeleted ? (
              <div className="quick-react more" role="group" aria-label="More reactions">
                {MORE_EMOJIS.map((e) => (
                  <button
                    key={e}
                    className="quick-emoji"
                    aria-label={`React ${e}`}
                    onClick={() => {
                      setMenu(null);
                      void react(menu.msg, e);
                    }}
                  >
                    {e}
                  </button>
                ))}
              </div>
            ) : null}
            <button
              onClick={() => {
                setReplyTo(menu.msg);
                setMenu(null);
              }}
              disabled={menu.msg.isDeleted}
            >
              <Icon name="reply" size={16} />
              Reply
            </button>
            {menu.msg.replyCount > 0 || menu.msg.replyTo ? (
              <button
                onClick={() => {
                  const parent = menu.msg.replyTo ? (byId.get(menu.msg.replyTo.messageId) ?? menu.msg) : menu.msg;
                  setThreadParent(parent);
                  setMenu(null);
                }}
              >
                <Icon name="chevron" size={16} />
                View thread
              </button>
            ) : null}
            <button
              onClick={() => {
                setForwardMsg(menu.msg);
                setMenu(null);
              }}
              disabled={menu.msg.isDeleted}
            >
              <Icon name="forward" size={16} />
              Forward
            </button>
            <button
              onClick={() => {
                toggleSelect(menu.msg.id);
                setMenu(null);
              }}
            >
              <Icon name="checks" size={16} />
              Select
            </button>
            <button
              onClick={() => {
                void navigator.clipboard?.writeText(menu.msg.body).catch(() => undefined);
                setMenu(null);
              }}
              disabled={menu.msg.isDeleted}
            >
              <Icon name="copy" size={16} />
              Copy
            </button>
            {menu.msg.senderId === myUserId && !menu.msg.isDeleted ? (
              <button
                onClick={() => {
                  setEditing(menu.msg);
                  setDraft(menu.msg.body);
                  setMenu(null);
                }}
              >
                <Icon name="edit" size={16} />
                Edit
              </button>
            ) : null}
            {!menu.msg.isDeleted && (menu.msg.senderId === myUserId || isAdmin) ? (
              <button
                className="danger"
                onClick={() => {
                  setDeleteTarget(menu.msg);
                  setMenu(null);
                }}
              >
                <Icon name="trash" size={16} />
                Delete…
              </button>
            ) : null}
            {(isAdmin || chat.type === 'Dm') && !menu.msg.isDeleted ? (
              <button onClick={() => void togglePin(menu.msg)}>
                <Icon name="pin" size={16} />
                {chat.pinnedMessageIds.includes(menu.msg.id) ? 'Unpin' : 'Pin'}
              </button>
            ) : null}
            {menu.msg.seq > 0 ? (
              <>
                <div className="menu-sep" aria-hidden="true" />
                <button
                  onClick={() => {
                    const seq = menu.msg.seq;
                    setMenu(null);
                    void chatApi.markUnread(chat.id, seq)
                      .then(() => queryClient.invalidateQueries({ queryKey: ['chats'] }))
                      .catch(() => undefined);
                  }}
                >
                  <Icon name="checks" size={16} />
                  Mark unread from here
                </button>
              </>
            ) : null}
          </div>
        </div>
      ) : null}

      {forwardPickerItems ? (
        <div className="dialog-backdrop" onClick={() => { setForwardMsg(null); setForwardMany(null); setForwardError(null); }} role="presentation">
          <div className="dialog" role="dialog" aria-modal="true" aria-label="Forward message" onClick={(e) => e.stopPropagation()}>
            <h2>Forward {forwardPickerItems.length > 1 ? `${forwardPickerItems.length} messages` : ''} to…</h2>
            <p className="hint">
              “{forwardPickerItems[0]?.body.slice(0, 120) ||
                forwardPickerItems[0]?.attachment?.fileName ||
                'Attachment'}”
            </p>
            <div className="picker-list">
              {chats
                .filter((c) => !forwardPickerItems.some((m) => m.chatId === c.id && forwardPickerItems.length === 1))
                .map((c) => (
                  <button
                    key={c.id}
                    className="picker-row"
                    disabled={forwarding}
                    onClick={() => void forwardTo(c.id)}
                  >
                    <strong>{chatDisplayName(c, myUserId)}</strong>
                    <span className="hint">{c.type}</span>
                  </button>
                ))}
            </div>
            {forwardError ? <div className="form-error" role="alert">{forwardError}</div> : null}
            {forwarding ? <p className="hint" role="status">Forwarding…</p> : null}
            <div className="row-actions">
              <button className="ghost-btn inline" onClick={() => { setForwardMsg(null); setForwardMany(null); setForwardError(null); }}>
                Cancel
              </button>
            </div>
          </div>
        </div>
      ) : null}

      {deleteTarget ? (
        <div className="dialog-backdrop" onClick={() => { if (!deleting) setDeleteTarget(null); }} role="presentation">
          <div className="dialog" role="dialog" aria-modal="true" aria-label="Delete message" onClick={(e) => e.stopPropagation()}>
            <h2>Delete message?</h2>
            <p className="hint">
              “{deleteTarget.body.slice(0, 120) || deleteTarget.attachment?.fileName || 'Attachment'}”
            </p>
            <p className="hint">
              This deletes the message <strong>for everyone</strong> in this conversation.
              This cannot be undone.
            </p>
            <div className="row-actions">
              <button className="ghost-btn inline" disabled={deleting} onClick={() => setDeleteTarget(null)}>
                Cancel
              </button>
              <button
                className="ghost-btn inline danger-text"
                disabled={deleting}
                onClick={() => void confirmDelete()}
              >
                {deleting ? 'Deleting…' : 'Delete for everyone'}
              </button>
            </div>
          </div>
        </div>
      ) : null}

      {bulkConfirm ? (
        <div className="dialog-backdrop" onClick={() => { if (!bulkDeleting) setBulkConfirm(false); }} role="presentation">
          <div className="dialog" role="dialog" aria-modal="true" aria-label="Delete selected messages" onClick={(e) => e.stopPropagation()}>
            <h2>Delete {selected.size} {selected.size === 1 ? 'message' : 'messages'}?</h2>
            <p className="hint">
              This deletes the selected messages <strong>for everyone</strong> in this conversation.
              Messages you are not allowed to delete will abort the whole batch — nothing is
              deleted partially. This cannot be undone.
            </p>
            <div className="row-actions">
              <button className="ghost-btn inline" disabled={bulkDeleting} onClick={() => setBulkConfirm(false)}>
                Cancel
              </button>
              <button
                className="ghost-btn inline danger-text"
                disabled={bulkDeleting || selected.size === 0}
                onClick={() => void confirmBulkDelete()}
              >
                {bulkDeleting ? 'Deleting…' : 'Delete for everyone'}
              </button>
            </div>
          </div>
        </div>
      ) : null}

      {threadParent ? (
        <aside className="thread-drawer" aria-label="Reply thread">
          <header className="thread-head">
            <strong>Thread</strong>
            <button className="icon-btn" aria-label="Close thread" onClick={() => setThreadParent(null)}>
              <Icon name="close" size={16} />
            </button>
          </header>
          <div className="thread-parent">{threadParent.isDeleted ? 'Message deleted' : threadParent.body.slice(0, 200)}</div>
          <div className="thread-list">
            {threadQuery.isPending ? <p className="hint">Loading replies…</p> : null}
            {(threadQuery.data ?? []).map((r) => (
              <div key={r.id} className="thread-item">
                <span className="thread-author">{r.senderName ?? r.senderUsername ?? 'Unknown'}</span>
                <span>{r.isDeleted ? <i>Message deleted</i> : r.body}</span>
                <span className="thread-time">{formatTime(r.createdAt)}</span>
              </div>
            ))}
            {threadQuery.isSuccess && (threadQuery.data ?? []).length === 0 ? (
              <p className="hint">No replies yet — reply to start the thread.</p>
            ) : null}
          </div>
          <div className="row-actions">
            <button
              className="ghost-btn inline"
              onClick={() => {
                setReplyTo(threadParent);
                setThreadParent(null);
              }}
            >
              Reply in thread
            </button>
          </div>
        </aside>
      ) : null}
    </main>
  );
}
