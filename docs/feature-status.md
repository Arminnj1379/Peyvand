# Feature Status

Evidence-based status of every checklist item. Status values: **Done** (backend +
frontend wired to real APIs), **Partial** (works end-to-end but with a defined gap),
**Missing** (no implementation on one or both sides). File references were verified
against source; no mocked functionality exists (`frontend/src/mocks/` is empty and
unimported, zero TODO/FIXME/"coming soon" in `frontend/src`).

## Messaging

| Feature | Status | Backend evidence | Frontend evidence |
|---|---|---|---|
| Reply (quote) | Done | `SendMessageCommand.ReplyToMessageId`, reply preview enrichment, `GetRepliesQuery` | `ChatWindow.tsx` reply bar/menu/quote render, send with `replyToMessageId` |
| Reply threads view | Done | `ListRepliesAsync` scoped to `(chatId, replyToMessageId)` via `ix_thread` | Thread drawer + "N replies" link, `messageApi.replies` |
| Forward | Done | `ForwardMessageCommand` with provenance, bytes shared (no re-upload) | Forward picker + forward-many from selection |
| Edit | Done | Sender-only, `message:edited` fan-out | Edit bar, "edited" flag, own+non-deleted gating |
| Delete | Done (= delete for everyone) | `Message.Delete` sets global `IsDeleted`; own-or-admin; per-message `message:deleted` | Delete menu, bulk delete from selection, tombstone render |
| Delete for me only | Missing | No per-user deleted set; `IsDeleted` is global | No "for me" option; `remove`/`bulkDelete` take ids only |
| Emoji reactions | Done | Toggle, caps 100/msg + 20 emojis, `message:reacted` | Quick-react popup + chips, toggle on click |
| Pin messages | Done | Cap 10, admin (DM: members), `chat:updated` | Pin bar, pins query, details pinned list + unpin |
| Read receipts | Done | Monotonic `lastReadSeq` watermark, `message:read`, auto-mark via REST | Single/double tick, `peerMaxRead`, auto `markRead` on open chat |
| Delivery status | Done | Monotonic `lastDeliveredSeq`, hub `Delivered` (no REST by design) | "Delivered" tick state, `peerMaxDelivered`, auto-ack |
| Typing indicators | Done | Ephemeral hub `Typing`, member-only, no DB write | Typing row + names, 5 s client expiry |
| Message selection | Done | `BulkDeleteMessages` (1–50, authorize-all-before-mutate) + forward-many | Checkboxes + toolbar (Forward/Delete/Cancel) |
| Voice messages | Partial | Generic audio attach works (`.ogg/.m4a/.weba`, ≤20 MB, magic-byte checked); **no duration/waveform metadata** (`AttachmentDto` has no such fields) | Audio **playback** only (`MediaViewer`); **no recorder** (zero `MediaRecorder`/`getUserMedia` hits) |

## Conversations

| Feature | Status | Backend evidence | Frontend evidence |
|---|---|---|---|
| Archive | Done | `SetArchiveCommand` (caller-relative) | Menu in list + details, folder tab |
| Mute | Done | `SetMuteCommand` (caller-relative) | Menu, mute dot/badge |
| Pin conversation (to top) | Missing | No `IsPinned` on member projection (`ChatDto`: only `IsMuted`/`IsArchived`) | No pin-chat UI; pins are message-level only |
| Mark as unread | Done | `MarkUnreadCommand` from seq | List menu + "from here" in window |
| Draft messages | Missing | No draft storage/API | Plain `useState` cleared on chat switch; no `localStorage` persistence |
| Block users | Partial | Toggle + enforcement both directions (DM create + send, neutral errors); **no list endpoint** | DM-only Block/Unblock button calling raw `/users/{id}/block`; initial state unfetched; **no blocked-list UI** |

## Groups

| Feature | Status | Notes |
|---|---|---|
| Create / edit (+ avatar) | Done | Dialog + details edit; `createGroup`/`updateProfile`/`uploadAvatar` |
| Member add / remove | Done | Search + add, remove button; `addMember`/`removeMember` |
| Admin roles | Done | Per-member role select; `setRole` |
| Permissions | Done | `MembersCanInvite` checkbox (admin-only); server+gating |
| Invite links | Done | Show/rotate/revoke + join-by-code dialog |
| Group info panel | Done | Hero + members + pins + call history + settings |

## Channels

| Feature | Status | Notes |
|---|---|---|
| Public / private + username | Done | Create checkbox + validation; make public/private; `setVisibility` |
| Subscribe / unsubscribe | Done | Lookup + subscribe dialog; unsubscribe for non-admins |
| Channel admins | Done | Same role select with Subscriber/Member/Admin/Owner |
| Publishing permissions | Done | Server owner/admin-only post; client composer replaced by "Only admins can post" + upload/drag gated |

## Media

| Feature | Status | Notes |
|---|---|---|
| Image/video/audio/document sharing | Done | Per-kind caps (10/100/20/25 MB), extension+magic validation, progress upload, staged preview, drag-drop |
| Media previews | Done | Fullscreen lightbox (images) + download; video/audio inline in bubble; authorized fetch |
| Shared media gallery / files / links tabs | Missing | No list-attachments endpoint; no tabs UI; link cards are inline client-side only |

## Search

| Feature | Status | Notes |
|---|---|---|
| Global search (Ctrl+K dialog) | Done | `SearchDialog` with Messages/Chats/People tabs |
| Message search | Done | `$text` index, scoped to own chats, limit ≤50 |
| User search | Done | Directory search + DM open; also used in new-chat/add-member |
| Search filters (sender/date/kind) | Missing | Both sides: API takes `(q, limit)` only; no filter controls |

## Calls

| Feature | Status | Notes |
|---|---|---|
| Voice / video 1-1 | Done | Signaling state machine + LiveKit tokens + incoming/accept/reject/join/leave/end; mute/camera toggles |
| Group calls | Done | Same flow works in group/channel chats + roster UI (no dedicated dialog) |
| Call history | Partial | Backend list + frontend read-only list; **no redial button** |
| Screen sharing | Missing (frontend) | Zero `getDisplayMedia` hits; backend N/A (pure LiveKit client track) |
| Media readiness caveat | Partial (deploy) | `LiveKit:` config empty by default, compose `media` profile off → `join` 503s fail-closed until a LiveKit server is wired |

## Settings

| Feature | Status | Notes |
|---|---|---|
| Profile (name/bio/avatar) | Done | Form + upload/remove; `updateMe` |
| Notification preferences | Done | Global in-app switch via `PATCH /me/settings` + bell/toast gating (per-chat mute separate, done) |
| Active sessions | Done | List + per-session revoke + sign-out-all |
| Themes (system/light/dark) | Done | Select + `data-theme` + persisted setting |
| Privacy settings page | Missing | No route; backend has only blocks (no last-seen/avatar visibility, no receipt opt-out, no blocked-list endpoint) |

## Bonus: implemented outside the checklist

- Support tickets (open/list/assign/status + support chat spin-up) — both sides (`SupportPage`, `SupportEndpoints`).
- Admin user management (create/list/active/roles + audit) — both sides (`AdminPage`, `AdminEndpoints`).
- Notifications derived inbox (unread-conversations bell dropdown) — both sides.
- Auth hardening (rotating refresh + reuse revoke, dummy-hash login, rate limits) — backend.

## Placeholders / dead UI

None found. All disabled buttons are legitimate guards (loading, empty, busy, deleted,
permission). No component renders without a backing API call.
