# Feature Gap Analysis

Companion to `feature-status.md` (what exists) — this doc is *what is missing or
incomplete, why, and what it takes*. Fully-done features are not repeated here
except where they carry a caveat. Rule respected: nothing below assumes a feature
from UI alone; every gap was verified in backend code or by absence of it.

## G1. Voice messages — backend Partial / frontend Missing

- Current: audio files attach as generic `Audio` (`.ogg/.m4a/.weba`, ≤20 MB) and play
  inline. There is no recording path and no voice-specific metadata: `AttachmentDto`
  carries `(FileId, FileName, Kind, ContentType, SizeBytes, Url)` — no duration,
  no waveform.
- Backend requirements: optional `DurationSeconds` (+ optional waveform peaks) on the
  file/message DTOs; accept client-reported duration at send time (validate sane range,
  e.g. 1 s–15 min) or derive server-side later. No new endpoints needed (`POST /files`
  + `POST /messages {fileId}` already compose).
- Frontend requirements: `MediaRecorder` capture UI (lock/discard/send), duration display,
  player with seek (existing `<audio>` can be restyled; waveform optional v2).
- Database changes: nullable `durationSeconds` (+ `waveform` array, capped length) on the
  file document; additive, no migration (rehydrate default null).
- SignalR events: none new (`message:new` already carries the attachment).
- Dependencies: microphone permission UX; `.webm`/`.ogg` already in `FileValidation`.
- Acceptance criteria: record → send → recipient sees voice bubble with duration; plays
  inline; survives refresh; oversized/invalid audio still rejected by existing validation.
- Complexity: **M** (frontend recorder/player is the bulk; backend is one nullable field).

## G2. Pin conversation — Missing both sides

- Current: pins are message-level only (`pinnedMessageIds`, cap 10). No chat-level pin;
  `ChatDto` has `IsMuted`/`IsArchived` but no `IsPinned`, and list sort is
  `lastMessageAt`-only.
- Backend requirements: `SetPinChatCommand` mirroring `SetMuteCommand` (caller-relative
  bool on the member embed); include in list sort (pinned first, then recency).
- Frontend requirements: pin/unpin menu in `ChatList` + pinned section styling.
- Database changes: `isPinned` on the member embed; no new index (sort over ≤limit docs
  in memory, same as current second-key sort).
- SignalR events: none new (`chat:updated` covers it; add fan-out on the new endpoint).
- Dependencies: none.
- Acceptance criteria: pinned chats stay on top across refresh; unpin restores recency
  order; per-user (A pinning does not affect B).
- Complexity: **S–M**.

## G3. Draft messages — Missing (frontend-transient only)

- Current: composer `draft` is `useState` cleared on chat switch; no persistence.
- Backend requirements: none for v1 (local-first is correct scope; server-synced drafts
  are a later, optional P3).
- Frontend requirements: `localStorage` per-chat draft (+ reply/edit context optionally),
  restored on open, cleared on send.
- Database changes: none.
- SignalR events: none.
- Dependencies: none.
- Acceptance criteria: typed-but-unsent text survives chat switch + reload; sending clears it.
- Complexity: **S**.

## G4. Shared media gallery / files / links — Missing both sides

- Current: attachments render inline only; link cards are client-side (`firstUrl` helper);
  chat details has no tabs. No endpoint lists a chat's attachments.
- Backend requirements: `GET /api/v1/chats/{id}/attachments?kind&limit` — filter
  `{chatId, fileId: {$ne: null}}` (+ `kind` when supplied), sort `seq` desc. Served by
  the `ix_history (chatId, seq)` prefix; membership-checked like history. Links tab can
  reuse message search scoped to URL-bearing bodies (v2; substring scan acceptable at
  current scale, document the limit).
- Frontend requirements: Media/Files/Links tabs in `ChatDetails` reusing `MediaViewer`.
- Database changes: none (no new index for v1; verify with `explain()`).
- SignalR events: none (read path; new arrivals visible on next open/refetch).
- Dependencies: G1 enriches gallery audio rows with durations for free.
- Acceptance criteria: gallery shows all images/videos of a chat newest-first; files tab
  lists documents with download; empty states; non-members get 403/404 as history does.
- Complexity: **M**.

## G5. Delete for me only — Missing both sides

- Current: delete is always for-everyone (`IsDeleted` global; own-or-admin). There is no
  per-user hide.
- Backend requirements: per-user hidden set (e.g. `hiddenForUserIds` on the message or a
  small side collection for large scale); history/sync/search must exclude ids containing
  the caller; keep the global delete path untouched.
- Frontend requirements: "Delete for me / for everyone" choice in the delete menu.
- Database changes: new array field (index selectively if it grows; v1 in-memory filter
  over page-sized results is acceptable, document it).
- SignalR events: `message:deleted` stays global-only; for-me deletes need no event
  (caller-local state + refetch).
- Dependencies: touches history/sync/search filters — regression-test all three.
- Acceptance criteria: "for me" hides only for caller (peer still sees); "for everyone"
  unchanged; search/sync consistent.
- Complexity: **M**.

## G6. Blocked list + privacy settings — backend Partial / frontend Missing

- Current: block toggle + bidirectional enforcement (DM create + send) with neutral errors.
  No list endpoint, no settings page, no other privacy controls (last-seen/avatar
  visibility, read-receipt opt-out do not exist server-side).
- Backend requirements: `GET /users/me/blocks` projecting `BlockedUserIds` with name
  enrichment (reuse `ListByIdsAsync` batching); v1 privacy = blocks list only. Further
  visibility flags are a separate P3 (they touch every read path).
- Frontend requirements: blocked-list UI (unblock buttons) + Privacy section/route.
- Database changes: none for v1 (reads existing `blockedUserIds`).
- SignalR events: none.
- Dependencies: none.
- Acceptance criteria: blocked users listed with display names; unblock from list works;
  toggle's initial state reflects server truth (fix current unfetched `false` default).
- Complexity: **S** (list) / **M** (privacy page shell).

## G7. Search filters — Missing both sides

- Current: `GET /messages/search?q&limit` (full-text, own chats) and user/chat search;
  no sender/date/kind scoping.
- Backend requirements: optional `senderId/from/to/kind/chatId` params composed onto the
  existing scoped query; validate ids (fail-closed like ticket filters); keep default
  path index-optimal and `explain()`-verify additions.
- Frontend requirements: filter controls in `SearchDialog` (sender picker via user search,
  date inputs, kind select).
- Database changes: none expected for sender+chat (`ix_history` prefix); date-range over
  text results sorts in memory at ≤50 rows (document; dedicated index only if limits grow).
- SignalR events: none.
- Dependencies: none.
- Acceptance criteria: combined filters return correct intersections; invalid ids return
  empty (never unfiltered); latency unchanged on the default path.
- Complexity: **M**.

## G8. Screen sharing — frontend Missing (backend N/A)

- Current: zero `getDisplayMedia` hits; call roster/mute/camera exist. Media plane is
  LiveKit client-side; no server change is possible or needed.
- Backend requirements: none.
- Frontend requirements: share button + `getDisplayMedia` track publish/unpublish in
  `useCallRoom`, tile rendering for screen tracks.
- Database changes: none. SignalR events: none (LiveKit room state).
- Dependencies: a working LiveKit server (see L3).
- Acceptance criteria: share starts/stops cleanly; peers see the screen tile; leaving the
  call stops the share track.
- Complexity: **S**.

## G9. Call history redial — backend Done / frontend Partial

- Current: read-only history list in chat details; no call-to-action.
- Requirements: "Call back" button reusing `callApi.start` with the same media type.
- Acceptance criteria: one click starts an audio/video call with the chat; 409 busy path
  handled as the header buttons do.
- Complexity: **S** (frontend-only).

## G10. Notifications/chat-list scale — latent functional gap (P0)

- Current: frontend loads `limit=50` chats once; hub auto-joins at most 200 groups. A user
  with >50 chats never fetches (and may never subscribe to) older chats: invisible
  conversations and silent realtime gaps, with no UI to reach them except search.
- Requirements: real chat-list pagination (cursor on `lastMessageAt`) + subscription sync
  for loaded pages; raise/verify the 200-group ceiling or page it.
- Database changes: none (existing `ix_member_list` serves paging).
- SignalR events: none new (`Subscribe` batching already exists).
- Acceptance criteria: 200-chat user scrolls the full list; incoming messages arrive for
  any loaded chat; no duplicate subscriptions.
- Complexity: **M**.

## Architectural limitations (constrain future work)

- **L1. 500-member embedded cap** (`Chat.MaxMembers`): group/channel growth beyond this
  needs member paging or a member side-collection; chat docs also grow every watermark
  write. Fine today; revisit past ~500/active community channels.
- **L2. Single-instance presence**: `InMemoryPresenceTracker` is exact on one API node;
  multi-node needs the documented Redis-backplane/sticky design (hook only today).
- **L3. Media plane external**: LiveKit off by default (empty config + `media` compose
  profile) — calls 503 fail-closed until deployed; MinIO single-node; whole-file
  in-memory proxy downloads (100 MB video worst case) until presigned URLs land.
- **L4. No E2E encryption**: bodies live in Mongo + backups; access control is
  server-side only. Flagged in `security-review.md`; E2E is research-grade (P3).
- **L5. Search linguistics**: one text index per collection; Persian substring gaps fall
  back to regex scans; user directory regex is COLLSCAN by design (tiny collection).
- **L6. No retention/purge**: messages accumulate (TTL hook exists in design only);
  `device_sessions` churn relies on TTL; avatar/file orphans on message delete need a
  reaper if storage costs bite.
- **L7. No push channel**: FCM/APNs stub — offline users learn nothing until they open
  the app; the bell is in-app only.
- **L8. Auth hardening**: no MFA, no login lockout, no breach-password screening
  (IP rate limits only) — see `security-review.md` remaining risks.
