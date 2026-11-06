# Architecture — Enterprise Messenger

## 0. Repository State (2026-11-06)

- Workspace `E:\Najafi\Messenger` is **empty** (not a git repo, 0 entries).
- This design therefore covers **greenfield scaffolding**; no existing behavior to preserve.
- This phase creates **documentation only** — no application code is generated here.

## 1. Goals & Constraints

- Single backend deployable (monolith, **no microservices**).
- Clean Architecture; Domain has zero infrastructure dependencies.
- Feature-based Application layer (vertical slices), not layered-by-kind.
- MongoDB via `MongoDB.Driver` directly in Infrastructure; **no generic repositories**,
  no EF, no extra ORM abstractions.
- React + TypeScript + Vite frontend; SignalR real-time; LiveKit + WebRTC media;
  MinIO/S3 file storage; Docker for on-prem + internet.
- Practical for ~50 employees + external customers; boring technology first.

## 2. System Overview

```
                ┌─────────────┐     HTTPS/WSS     ┌──────────────────────┐
                │ React SPA   ├──────────────────►│  ASP.NET Core API    │
                │ (Vite+TS)   │   REST + SignalR  │  (single backend)    │
                └──────┬──────┘                   └──┬─────┬──────┬──────┘
                       │                             │     │      │
                       │ LiveKit SDK (UDP/TLS)       │     │      │ S3 API
                       ▼                             │     │      ▼
                ┌─────────────┐   token (via API)    │     │   ┌─────────┐
                │ LiveKit SFU ├◄─────────────────────┘     │   │ MinIO/S3│
                └─────────────┘                           │   └─────────┘
                                                          │
                                                          ▼
                                                   ┌─────────────┐
                                                   │ MongoDB     │
                                                   │ (replicaset)│
                                                   └─────────────┘
Push (P0): API → Firebase (FCM) → devices. On air-gapped on-prem: in-app only.
```

Single backend owns: REST, SignalR hubs, JWT issuance/validation, LiveKit token minting,
S3 presigning, push fan-out, support assignment. LiveKit/MinIO/Mongo are the only
infrastructure processes (plus optional reverse proxy).

## 3. Backend — Solution Layout

```
Messenger.sln
└── src/
    ├── Domain/                 # net10.0, no external deps
    │   ├── Entities/           # User, Chat, Message, FileAsset, CallSession, Device, ...
    │   ├── ValueObjects/       # ChatId, MessageId, PhoneNumber, ...
    │   ├── Enums/              # ChatType, MessageType, CallState, SupportState, ...
    │   ├── Events/             # IDomainEvent + MessageSent, ChatAssigned, ...
    │   └── Errors/             # DomainError, Result<T> (or in Application.Common)
    ├── Application/            # depends on Domain only
    │   ├── Common/             # Result, PagedResult, ICurrentUser, IClock, IIdGenerator, pagination
    │   └── Features/
    │       ├── Auth/           # Login, Refresh, Revoke, OTP-request/verify
    │       ├── Users/          # GetProfile, UpdateProfile, SearchDirectory, Block
    │       ├── Chats/          # CreateDm/Group/Channel, AddMember, Mute, Pin, ...
    │       ├── Messages/       # Send, Edit, Delete, History query, MarkRead, Search
    │       ├── Media/          # RequestUpload, ConfirmUpload, GetDownloadUrl
    │       ├── Presence/       # Heartbeat, typing is transport-only (no handler)
    │       ├── Calls/          # StartCall, JoinCall, EndCall, LiveKit token query
    │       ├── Notifications/  # RegisterDevice, unread-count query
    │       └── Support/        # OpenTicket, Assign, Transfer, Resolve, Close
    │       └── (each feature: *Command/*Query + Handler + Validator + DTO, no cross-feature calls
    │            except via shared Common or Domain services)
    ├── Infrastructure/         # depends on Application + Domain
    │   ├── Persistence/Mongo/  # MongoDbContext, per-aggregate stores (e.g. MongoChatStore),
    │   │                       # collection setup + index creation, no IRepository<T>
    │   ├── Auth/               # JwtTokenService, RefreshTokenStore, PasswordHasher (PBKDF2/Argon2id),
    │   │                       # OtpService, PhoneSender abstraction (SMS gateway stub)
    │   ├── Storage/            # S3/MinIO presigner (AWS SDK or Minio client), thumbnail hook
    │   ├── Realtime/           # ISignalRPublisher, Hub group helpers, presence tracker (in-memory + Mongo lastSeen)
    │   ├── Calls/              # LiveKitTokenService (LiveKit server SDK)
    │   ├── Push/               # FcmPushSender : IPushSender (no-op sender for air-gap)
    │   └── Time/Ids/           # SystemClock, ObjectIdGenerator
    └── WebApi/                 # depends on Application + Infrastructure (+ Domain for enums only)
        ├── Endpoints/          # Minimal APIs or Controllers per feature (one file per feature)
        ├── Hubs/               # MessengerHub (chat+presence+call signaling in one hub, methods namespaced)
        ├── Auth/               # JWT bearer setup, policies (EmployeeOnly, SupportAgentOnly...), participant guards
        ├── Middleware/         # Exception→ProblemDetails, correlation-id, request logging
        └── Composition/        # DI wiring, Mongo/index bootstrap, config validation on startup
└── tests/ (P0: Domain + Application handler tests; API integration tests for Messages/Chats/Auth)
```

### Dependency rule

```
WebApi → Infrastructure → Application → Domain
WebApi → Application (to dispatch; never bypass to Infrastructure directly from handlers)
Frontend → WebApi only (+ LiveKit/MinIO direct with server-issued tokens/URLs)
```

`Domain` references nothing. `Application` references only `Domain` (+ FluentValidation,
MediatR-orikte — see ADR-02). `Infrastructure` implements `Application` ports
(`IChatStore, IMessageStore, IUserStore, IPushSender, ...` — one interface per aggregate,
not generic).

## 4. Key Backend Conventions

- **Dispatch:** MediatR (or lightweight `IHandler<TReq,TRes>` if team prefers fewer deps —
  decision at scaffolding; document whichever chosen). Handlers are the transaction boundary.
- **Result pattern:** `Result<T>` / `Error(code, message)`; WebApi maps to ProblemDetails.
  No exceptions for domain failures.
- **Validation:** FluentValidation per command/query; fail fast before handler body.
- **AuthN/Z:** JWT bearer; `ICurrentUser` from claims. Resource authorization inside handlers
  (e.g. `EnsureParticipant(chatId, userId)`), plus hub group membership check on subscribe.
  Never trust client-supplied `senderId`.
- **Idempotency:** `clientMessageId` (GUID from client) unique per sender; resend returns
  existing message instead of duplicating.
- **Ordering:** per-chat monotonic `seq` allocated server-side (atomic counter on chat doc
  or dedicated counters collection — see database-design.md). Clients sort by `(seq)`,
  tie-break `(_id)`.
- **Time/IDs:** UTC everywhere; `ObjectId` primary keys; ULID/GUID only for client idempotency keys.
- **Persistence:** one concrete store class per aggregate (`MongoMessageStore : IMessageStore`)
  using `IMongoCollection<T>` + LINQ/driver directly. Index creation at startup
  (`CreateOneAsync` with `CreateIndexOptions { Background = true }` where supported).
- **Domain events:** raised in entities, dispatched in-process after commit
  (e.g. `MessageSent → SignalR fan-out + push`). No message bus in MVP.
- **Logging:** structured (`ILogger`, correlation id), PII-redacted message bodies in logs
  (log ids/lengths, not content, by default).

## 5. Real-Time Design (SignalR)

One hub `MessengerHub` (single connection per client; simpler auth/scale story than 3 hubs).
As-built in `src/Messenger.Api/Hubs/MessengerHub.cs`:

- Groups: `chat:{chatId}` for members (auto-joined on connect; explicit
  `Subscribe`/`Unsubscribe` for chats created mid-session). No `user:{id}` groups yet.
- Server→client events — persistent (replayable via REST): `message:new`,
  `message:edited`, `message:deleted`, `message:reacted`, `message:read`,
  `message:delivered`, `chat:updated`. Transient (never stored, client-expired):
  `typing:update`, `presence:update`.
- Client→server invokes: `Subscribe(chatIds[])`, `Unsubscribe`,
  `Typing(chatId, isTyping)`, `Delivered(chatId, seq)`. Reads stay REST
  (`POST /chats/{id}/read`); delivery acks ride the hub (chatty, persisted as a
  monotonic watermark either way).
- Delivery workflow (authoritative): `REST POST /messages → persist → hub fan-out to
  chat group → push for offline members`. SignalR never persists; reconnect = REST re-sync
  from last `seq`/`_id` (`GET messages/sync` + chat-list/pins refresh). Detailed workflows in §6.
- Sender identity always comes from the JWT (`CallerId`); hub arguments carry no
  user ids. Membership is re-verified per call; outsiders are silently skipped.
- MVP scale-out: single API instance with sticky connections (reverse-proxy affinity).
  Redis backplane is a documented one-line swap in `Program.cs`
  (`AddStackExchangeRedis`, package referenced only when scaling) — no code fork needed.
  Presence counts stay per-instance (`InMemoryPresenceTracker`); last-seen is durable.

## 6. Message Delivery Workflows

### 6.1 Send (happy path)

1. Client generates `clientMessageId`, renders optimistically as `sending`.
2. `POST /api/v1/chats/{id}/messages` (JWT, participant-checked) → handler validates,
   allocates `seq`, inserts `messages` doc (idempotent on sender+clientMessageId).
3. Endpoint fans out `message:new` to `chat:{id}` only after the commit;
   increments `unreadCount` for other members (embedded watermark); enqueues push for offline members.
4. Viewing recipients ack via hub `Delivered(chatId, seq)` → persisted
   `lastDeliveredSeq` watermark → `message:delivered`; sender sees ✓✓ delivered.
   `POST /chats/{id}/read` advances the read watermark → `message:read` (✓✓ read).

### 6.2 Offline / reconnect

1. Client cache holds pages per chat; highest cached `seq` is the resume point.
2. On reconnect: invalidate `chats`/`pins`, then per chat
   `GET /api/v1/chats/{id}/messages/sync?afterSeq={maxSeq}` → gap-fill merge
   (idempotent by message id / clientMessageId, so replays never duplicate).
3. Push notification is a ping only (no body for privacy in MVP option) → client pulls.

### 6.3 Typing / presence / read / delivered

- Typing: client→hub `Typing`, server rebroadcasts to chat group except sender; no DB write;
  clients expire it locally (~5 s).
- Presence: connect (0→1 transition) broadcasts `presence:update {userId, isOnline:true}`;
  disconnect (1→0) persists `users.lastSeenAt` and broadcasts
  `presence:update {userId, isOnline:false, lastSeenAt}` to the user's chat groups.
  Concurrent devices share one online state (no duplicate events). Tracker is
  `IPresenceTracker` (`InMemoryPresenceTracker` singleton).
- Read: `POST /chats/{id}/read` advances the membership watermark (compare-and-set);
  `message:read` carries `(chatId, userId, seq)`; reading implies delivery.
- Delivered: client→hub `Delivered(chatId, seq)` advances the persisted
  `lastDeliveredSeq` watermark (monotonic — stale/duplicate acks ignored);
  `message:delivered` carries `(chatId, userId, seq)`. Sender ticks:
  ✓ sent, ✓✓ delivered, ✓✓ read.

### 6.4 Calls

As-built (`Calls` slice):

1. Caller: `POST /api/v1/chats/{id}/calls {mediaType}` → creates `calls` doc
   (`Ringing`, one live call per chat enforced) → `call:incoming` to the chat group.
2. Callee: `POST /api/v1/calls/{id}/accept` → `call:accepted`; joins media via
   `POST /api/v1/calls/{id}/join` → one-shot LiveKit URL + JWT (6 h TTL, room +
   publish/subscribe grants). First join flips the call `Active`.
3. Decline: `POST …/reject` → `call:rejected` (+ `call:ended` when a 1-1 ring dies).
   Leave keeps the call up for others (`call:left`); last one out ends it.
   End (`POST …/end`) hangs up for everyone: DMs either party, groups/channels the
   caller or a chat admin — `call:ended`.
4. Media never traverses the API (client↔LiveKit SFU via `livekit-client`).
   The API secret never leaves the server; unconfigured LiveKit fails closed
   (`503 call.livekit_not_configured`) while signaling keeps working.
5. History: `GET /api/v1/chats/{id}/calls` (status, participants, duration).
   LiveKit's embedded TURN (5349/udp, `livekit.yaml`) covers restrictive NATs —
   no separate coturn, no custom media server.

### 6.5 Support assignment

1. `POST /api/support/tickets` creates `Chat(type=support, support.state=queued)` + first message.
2. Assignment service (in-process domain service, no hangfire in MVP — runs inline +
   periodic reconciliation endpoint/timer): picks least-loaded online agent
   (`supportAgents` availability + active-ticket count), sets `assigneeId`, state `assigned`.
3. `support:assigned` to agent + customer; agent's chat list surfaces queue.
4. Transfer/close via `Support` handlers with role checks (`SupportAgentOnly` + assignee-or-lead).

## 7. Frontend Architecture

- **Stack:** Vite + React 18 + TypeScript (strict), React Router, TanStack Query (server state),
  Zustand (client/ephemeral state: active chat, typing, presence cache), SignalR client,
  LiveKit client SDK, i18next (en + fa, RTL), Tailwind or MUI (team pick at scaffolding; RTL-first).
- **Structure (feature-sliced):** `src/app/ (shell, router, providers)`, `src/features/{auth,chats,
  messages,calls,support,search,settings}/ (api, hooks, components)`, `src/entities/ (types, dtos)`,
  `src/shared/ (ui, lib, signalr-client, livekit-client)`, `src/widgets/ (chat-list, message-view, call-bar)`.
- **Data strategy:** Query for history (`useInfiniteQuery` cursor), SignalR invalidates/appends
  to cache (no full refetch on each message); optimistic send with `clientMessageId` reconcile.
- **Components:** `ChatList, MessageList (virtualized), Composer, MessageBubble (+ variants),
  PresenceDot, TypingIndicator, ReadTicks, FileUploader, CallOverlay, SupportQueuePanel`.
  Virtualize with `virtua`/`react-virtuoso` from day one (message lists grow fast).
- **PWA hooks:** service worker + manifest reserved; full offline outbox is P1.

## 8. Infrastructure & Deployment

- `docker-compose.yml` services: `api, mongo (replicaset single-node for dev; 3-node profile
  for prod), minio, livekit (+ coturn when internet calls need NAT traversal), web (nginx
  serving SPA + reverse proxy), setup scripts for bucket + replicaset init`.
- Config via env (`appsettings` + `ASPNETCORE_*` overrides); startup validates required keys
  (JWT, Mongo, S3, LiveKit, FCM) and fails fast with actionable errors.
- TLS terminated at reverse proxy (on-prem: customer certs; internet: Let's Encrypt profile).
- Backups: `mongodump` cron + MinIO versioning/snapshot; restore runbook in roadmap phase 0.
- Observability MVP: structured logs + `/healthz` (liveness) + `/readyz` (mongo/minio/livekit
  checks); metrics (Prometheus endpoint) + tracing are P1.

## 9. ADRs (summary)

| ID | Decision | Rationale | If wrong |
|---|---|---|---|
| ADR-01 | Monolith, no bus/microservices | Team scale, ops simplicity, perf headroom sufficient | Extract read/search service at P1 without API break |
| ADR-02 | MediatR + FluentValidation in Application | Standard slices, testability; alternatives documented | Swap to minimal dispatchers, handlers unchanged |
| ADR-03 | MongoDB as source of truth, no join DB | Document model fits chat workload; single writer | Add Redis cache/backplane (no schema change) |
| ADR-04 | No generic repository; per-aggregate stores | Avoids leaky abstraction, keeps queries explicit | — (convention enforced in review) |
| ADR-05 | SignalR single hub + REST persistence | Offline-safe, simple reconnect; no message loss | Add Redis backplane for scale-out |
| ADR-06 | LiveKit SFU, backend mints tokens | No media through API; proven WebRTC SFU | Replace SFU without changing call API |
| ADR-07 | MinIO/S3 via API-proxy downloads | Authz in code, MinIO never client-reachable | Presigned URLs/CDN later, same routes |
| ADR-08 | Defer Elastic/Redis/Hangfire | Unneeded at this scale; in-process + Mongo suffice | Each has a defined insertion point (see roadmap) |
