# Features & Scope — Enterprise Messenger (Telegram-like)

## 1. Vision

Internal + external messaging platform with Telegram-like UX: fast 1:1 and group messaging,
broadcast channels, file sharing, presence/typing/read receipts, voice/video calls,
search, notifications, profiles, and customer-support workflows with agent assignment.

Target org size: ~50 employees + external customers (hundreds to low thousands of
concurrent users in MVP; design headroom to ~10–20k total users without re-architecture).

## 2. Personas & Roles

| Persona | Description | Needs |
|---|---|---|
| Employee (internal) | Staff member, trusted identity | DM, groups, channels, calls, files, directory search |
| Customer (external) | Verified or self-registered outsider | DM with staff/support, support tickets, limited directory |
| Support Agent | Employee handling support queue | Assigned conversations, canned replies, transfer/close, history |
| Support Lead | Supervises queue | Assignment rules, monitoring, reassignment, reports |
| Admin | Platform operator | User/org management, roles, retention policies, audit |

Roles are orthogonal to chat membership: `Admin, Employee, Customer, SupportAgent, SupportLead`.
A user has one primary type (`Employee | Customer`) plus optional role flags
(`IsAdmin, IsSupportAgent, IsSupportLead`).

## 3. Functional Domains

### 3.1 Authentication, Users & Profiles

- Register/login: username+password (employees); phone-number + OTP or invite-link
  + password for customers (see §7 Assumptions).
- JWT access (short-lived, 15 min) + opaque refresh token (rotating, 30 d, revocable).
- Multi-device sessions with per-device revoke + device list.
- Profile: display name, avatar, bio, phone (visibility-controlled), presence privacy.
- Directory: search employees by name/username; customers discoverable only via
  existing chats or support entry points (privacy).
- Block/unblock users.

### 3.2 Private Conversations (DM)

- Exactly 2 participants, created on first message or explicitly.
- Both sides see shared history (configurable: full shared history for MVP).
- Typing, online/last-seen, delivered/read per message.
- Edit/delete (own messages), delete-for-everyone within time window (e.g. 48 h).
- Reply, forward, pin (per-chat pins), markdown-lite formatting.

### 3.3 Group Chats

- Members 3..N (MVP cap e.g. 500, configurable), title, avatar, description.
- Roles: Owner, Admin, Member. Owner can transfer ownership.
- Admin actions: add/remove, promote/demote, edit info, pin, slow-mode (P1).
- Join via invite link (revocable) or direct add. Leave/kick/ban (ban = P1).
- Per-member notification mute, per-group mention (`@all` restricted to admins in MVP).

### 3.4 Channels

- Broadcast: only admins post; subscribers read/react (reactions = P1).
- Public (username-addressable, searchable) vs Private (invite-only).
- Subscriber counts, silent broadcast option.
- Comments/discussion group link = P1 (out of MVP).

### 3.5 Messages

- Types (MVP): `text, image, file, audio, video, voice, sticker?, reply, forward, system`.
- P1: polls, locations, contacts, code blocks, scheduled messages, drafts sync.
- Out of scope: bots API, stories, payments, games, E2E-secret-chats.
- Per-message: `clientId` (idempotency), server seq per chat, timestamps, edit history
  (last edit only in MVP), replyTo/forwardFrom metadata.
- Delivery states: `sending → sent → delivered → read` (group read = "read by N",
  detail list = P1, count only in MVP).
- Pagination: cursor-based (by `createdAt + _id`), 30–50/page. No offset pagination.

### 3.6 File & Media Sharing

- Upload via presigned S3/MinIO URLs; inline images/video thumbnails generated async (P1: worker in-process).
- Limits (MVP): 100 MB/file on-prem default (configurable), image preview < 10 MB inline.
- Caption, multi-attachment album (P1: album grouping, MVP: separate messages).
- Retention policy per deployment (e.g. keep forever on-prem; configurable TTL = P1).

### 3.7 Presence, Typing, Read Receipts

- Presence: `online | offline + lastSeenAt`. Heartbeat 30 s; timeout 90 s.
- Typing: ephemeral SignalR events, auto-expire 5 s, per-chat + per-thread (thread = P1).
- Read: per-participant `lastReadMessageId / lastReadAt` watermark on chat membership;
  message read counts derived, not stored per reader per message (scale decision).

### 3.8 Voice & Video Calls (1:1 + small group)

- MVP: 1:1 audio/video + group voice/video up to ~10 via LiveKit SFU.
- Signaling: SignalR (invite/accept/decline/cancel) + LiveKit room tokens from backend.
- Call history message (system message: missed/ended + duration).
- P1: screen share (LiveKit supports it; UI work deferred), recording, PSTN.
- Out: SIP federation, large webinars (>50).

### 3.9 Search

- MVP: chat-local + global message search by text substring (MongoDB text index +
  regex fallback), user/chat directory search, file-name search.
- P1: advanced filters (from:, in:, has:, date:), file-content search, server-side FTS tuning.
- Out: Elasticsearch in MVP (explicitly deferred; see ADR).

### 3.10 Notifications

- MVP: in-app unread badges/counts + real-time toasts via SignalR; push via FCM/APNs
  to registered device tokens (single provider abstraction, Firebase first).
- Per-chat mute (forever / 1 h / 8 h), mention-only mode for groups.
- Email/SMS fallback = P1/out.

### 3.11 Customer Support Conversations & Assignment

- Entry: customer opens "Support" → creates/attaches a `Support` chat (a chat subtype).
- Queue states: `Queued → Assigned → Active → Resolved → Closed (+ Reopened)`.
- Assignment (MVP): auto-assign least-loaded online agent in department; manual
  assign/transfer by lead; customer sees agent name + queue position.
- SLA timers (first-response, resolution) recorded; breach surfaced to lead (report = P1).
- Canned replies, internal notes (invisible to customer, P1), chat transcript export,
  post-chat rating (P1).
- Support chats reuse the same message pipeline; access control differs
  (only assigned agent(s) + leads + customer).

## 4. MVP Boundaries

### P0 — MVP must-have

Auth + profiles + directory (privacy-scoped), DM, groups, channels (broadcast read),
text/media/file messages, reply/forward/edit/delete, pins (per chat, 1–N),
presence/typing/read watermarks, unread counts, message + directory search (basic),
upload/download via MinIO, 1:1 + small-group calls via LiveKit,
in-app + push notifications, support queue + auto/manual assignment + transfer/close,
multi-device sessions, Docker Compose on-prem + internet deploy, TLS, audit log (basic).

### P1 — Post-MVP (design hooks now, build later)

Reactions, polls, threads/replies-tree, drafts sync, scheduled messages, albums,
stickers/custom emoji, screen-share UI, call recording, advanced search filters,
internal agent notes, ratings, SLA dashboards, moderation (report/spam), Redis
backplane for multi-API scale-out, S3 lifecycle/CDN, APM dashboards.

### Explicitly out of scope

E2E-encrypted secret chats, bots/platform API, stories, payments/crypto,
federation/bridges, email hosting, full contact-center (IVR, omnichannel).

## 5. Non-Functional Scope (MVP)

- Latency: p95 send→deliver < 500 ms LAN / < 1.5 s internet (text-only, uncongested).
- Availability: single-API + Mongo replica set (1 primary + 2 members or 1 + arbiter);
  backups daily. No HA API in MVP (scale-out hook documented).
- Durability: message write is source of truth in MongoDB; SignalR is delivery only,
  offline clients re-sync via pull (no message loss on disconnect).
- Clients: responsive web (desktop-first, mobile-usable). Native mobile = out (PWA hooks only).

## 6. Ambiguities Found

1. Customer identity proofing: self-register vs invite-only? → Assumption §7.1.
2. Employee source of truth (HR/AD vs local)? → Assumption §7.2.
3. History visibility for new group members? → Assumption §7.3.
4. Retention/legal hold requirements? → Assumption §7.4.
5. Call recording consent rules? → Assumption §7.5.
6. Push provider allowed on-prem (Firebase needs internet)? → Assumption §7.6.
7. Persian/RTL + i18n scope? → Assumption §7.7.

## 7. Assumptions (MVP)

1. **Customers:** invite-link + phone verification; self-registration behind CAPTCHA/rate-limit,
   admin can disable. No national-ID verification in MVP.
2. **Employees:** local accounts seeded by admin/CSV import; LDAP/AD sync is P1 hook
   (username mapping reserved: `externalId` field on user doc).
3. **History:** new group/channel members see full history (simplest, Telegram-like for
   channels); per-message visibility windows are P1.
4. **Retention:** keep everything in MVP; per-chat TTL + legal-hold flags schema-reserved
   but unenforced.
5. **Calls:** no recording in MVP → no consent workflow yet; schema reserves `recordingUrl/consent`.
6. **Push:** Firebase Cloud Messaging for MVP (needs internet from server or client-direct?
   server-side send needs internet; on fully-air-gapped on-prem, push degrades to in-app only —
   documented in deployment doc).
7. **i18n:** UI in English + Persian (RTL) from day one for shell/navigation; full string
   coverage enforced via i18n lint in P1.
8. **Time:** all timestamps UTC; display localized client-side.
9. **IDs:** MongoDB `ObjectId` as primary keys, exposed as strings over API/SignalR.
