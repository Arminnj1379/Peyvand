# Database Design — MongoDB

## 0. Conventions

- **Driver:** `MongoDB.Driver` directly in `Infrastructure/Persistence/Mongo`. No ODM beyond
  the driver's serialization; no generic repository.
- **Keys:** `_id: ObjectId` for every doc; exposed as string in APIs. Client idempotency keys
  (`clientMessageId: Guid-string`) are separate unique fields, not primary keys.
- **Time:** `DateTime` UTC always (`BsonDateTimeOptions(Kind=Utc)`); field names `*At`
  (`createdAt, editedAt, lastSeenAt, ...`).
- **Naming:** collections `snake_case` plural (`users, chats, messages, files, calls, devices`);
  fields `camelCase`. Enums stored as `string` (readable, forward-compatible).
- **Soft delete:** messages/chats use `isDeleted + deletedAt (+ deletedBy)`; hard purge is an
  ops task per retention policy, not app code in MVP.
- **Auditing:** `createdBy/updatedBy` user-id strings where meaningful; full audit trail = P1.
- **Multi-doc consistency:** single-doc atomicity preferred (embed where read-together);
  multi-doc flows use ordered writes + reconciliation, not distributed transactions
  (transactions reserved for assignment + seq allocation edge cases on replica set).
- **Pagination:** cursor on `(createdAt, _id)` or per-chat `seq`; never `skip/limit` for feeds.

## 1. Collections Overview

| Collection | Purpose | Approx. growth | Notes |
|---|---|---|---|
| `users` | identities + profiles + presence | slow (users) | one doc per user |
| `chats` | DM/group/channel/support header + membership embedded | slow-medium | one doc per conversation |
| `messages` | all chat messages (incl. system/call/support events) | fast (dominant) | one doc per message |
| `files` | uploaded asset metadata (S3 object ref) | medium | one doc per upload; messages link to it |
| `calls` | call sessions + participants + outcome | slow | one doc per call |
| `devices` | push tokens + sessions per user-device | slow | one doc per device |
| `counters` | per-chat seq allocators (alternative: in-chat counter — see §3) | tiny | one doc per chat if used |

No separate `participants`, `read_receipts`, `notifications` collections in MVP —
they are embedded (see below) to keep reads to 1–2 queries per screen.

## 2. Document Schemas (illustrative BSON/JSON)

### 2.1 `users`

```jsonc
{
  "_id": "ObjectId",
  "username": "a.najafi",            // unique, lowercase, sparse (customers may lack one initially)
  "externalId": null,                // LDAP/HR hook (P1)
  "userType": "employee | customer",
  "isAdmin": false,
  "isSupportAgent": true,
  "isSupportLead": false,
  "displayName": "Ali Najafi",
  "avatarFileId": "ObjectId | null",
  "bio": "...",
  "theme": "system | light | dark",
  "notificationsEnabled": true,
  "phone": "+98...",
  "phoneVerified": true,
  "passwordHash": "argon2id/phc-string",
  "isActive": true,
  "blockedUserIds": ["ObjectId"],
  "lastSeenAt": "UTC",
  "createdAt": "UTC", "updatedAt": "UTC"
}
```

### 2.2 `chats` (single collection, `type` discriminator)

```jsonc
{
  "_id": "ObjectId",
  "type": "dm | group | channel | support",
  "title": "Group/channel title | null for dm",
  "avatarFileId": "ObjectId | null",
  "description": "...",
  "publicUsername": "channel-handle | null",   // unique sparse, channels only
  "inviteCode": "opaque-string | null",        // revocable, groups/channels
  "ownerId": "ObjectId",
  // membership EMBEDDED (MVP cap: 500; larger channels store subscriber count + paged membership P1)
  "members": [
    {
      "userId": "ObjectId",
      "role": "owner | admin | member | subscriber",
      "joinedAt": "UTC",
      "lastReadMessageId": "ObjectId | null",
      "lastReadSeq": 1234,
      "lastReadAt": "UTC | null",
      "unreadCount": 7,
      "isMuted": false,
      "muteUntil": "UTC | null"
    }
  ],
  "memberCount": 42,                 // denormalized, maintained on add/remove
  "pinnedMessageIds": ["ObjectId"],  // ordered, cap e.g. 5–10
  "lastMessageId": "ObjectId | null",
  "lastMessageAt": "UTC | null",
  "lastMessagePreview": "truncated text",
  "seqCounter": 9876,                // OPTION A: in-doc atomic counter (preferred MVP)
  "support": null,                   // | { state, department, assigneeId, queuePosition,
                                     //     openedAt, assignedAt, resolvedAt, closedAt, slaDueAt }
  "isDeleted": false,
  "createdBy": "ObjectId", "createdAt": "UTC", "updatedAt": "UTC"
}
```

Seq allocation OPTION A (recommended): `findOneAndUpdate({_id}, {$inc: {seqCounter:1}})`
returns the new seq atomically with the chat doc — no extra collection, works on replica set.
OPTION B (`counters` collection) only if hot-chat contention is measured (not expected at
this scale).

### 2.3 `messages` (largest collection)

```jsonc
{
  "_id": "ObjectId",
  "chatId": "ObjectId",
  "seq": 9877,                       // per-chat monotonic, allocated at send
  "clientMessageId": "guid-string",  // idempotency; unique per sender
  "senderId": "ObjectId | null",     // null for system messages
  "kind": "text | image | file | audio | video | voice | reply | forward | system | callEvent",
  "body": "text | caption | null",   // PII: never log full body by default
  "replyToMessageId": "ObjectId | null",
  "forwardFrom": null,               // | { chatId, messageId, senderId, sentAt }
  "fileId": "ObjectId | null",       // → files._id
  "fileIds": ["ObjectId"],           // P1 albums; MVP single fileId, keep array reserved
  "system": null,                    // | { event: "member_joined | call_missed | support_state | ..." }
  "isEdited": false, "editedAt": null,
  "isDeleted": false, "deletedAt": null, "deletedBy": null,
  "createdAt": "UTC"
}
```

Read state is **not** per-message-per-user (avoids write explosion). Read = membership
watermark (`members.lastReadSeq`) — "read by N" derived at read time for small chats;
exact reader lists for groups are P1.

### 2.4 `files`

```jsonc
{
  "_id": "ObjectId",
  "ownerId": "ObjectId",
  "chatId": "ObjectId | null",       // null until attached on send
  "messageId": "ObjectId | null",    // set once, on first attach
  "fileName": "report.pdf",          // server-sanitized
  "contentType": "application/pdf",  // validated against magic bytes
  "sizeBytes": 123456,
  "sha256": "...",                   // integrity + dedupe hook
  "kind": "Image | Video | Audio | Document",
  "status": "Pending | Ready | Rejected",
  "objectKey": "files/{fileId}/{safeName}",  // server-generated, unguessable
  "createdAt": "UTC", "attachedAt": "UTC | null"
}
```

Bytes live in MinIO/S3; Mongo stores metadata only. As-built upload flow
(upload-then-attach, no presigned URLs in MVP — the API proxies bytes so
authorization stays in code and MinIO is never client-reachable):
`POST /files (multipart) → validate + scan + PUT object + files.ready →
POST /messages {fileId} → files.messageId backfill`.
Limits: image 10 MB, video 100 MB, audio 20 MB, document 25 MB.
Downloads: `GET /files/{id}` (members, or uploader while pending) with HTTP
range support; `?download=1` forces save-as. Deletes: pending uploads by owner
only; attached files follow message retention. Malware scanning is an
`IFileScanner` seam (NoOp in MVP; ClamAV/ICAP later).

### 2.5 `calls`

```jsonc
{
  "_id": "ObjectId",
  "chatId": "ObjectId",              // calls always belong to a conversation
  "roomName": "call-{ObjectId}",     // LiveKit room (server-generated)
  "initiatorId": "ObjectId",
  "mediaType": "Audio | Video",
  "status": "Ringing | Active | Ended",
  "participants": [
    { "userId": "ObjectId", "status": "Invited | Accepted | Rejected",
      "isJoined": false, "invitedAt": "UTC", "joinedAt": "UTC | null" }
  ],
  "startedAt": "UTC | null",         // first media join
  "endedAt": "UTC | null", "endedBy": "ObjectId | null",
  "endReason": "Completed | Declined | Cancelled | null"
}
```

As-built notes: one live (Ringing/Active) call per chat (409 otherwise);
initiator pre-accepted; DM reject ends the ring (Declined); last-leave ends
(Completed); group/channel end needs caller-or-admin. Duration derives from
`startedAt→endedAt`. Index `{ chatId: 1, createdAt: -1 }` serves history +
live-call lookup.

### 2.6 `tickets` (support inbox)

```jsonc
{
  "_id": "ObjectId",
  "customerId": "ObjectId",
  "subject": "≤128 chars",
  "description": "≤2000 chars",
  "status": "Open | Assigned | InProgress | Resolved | Closed",
  "assigneeId": "ObjectId | null",   // support staff only
  "chatId": "ObjectId | null",       // support conversation, created on first assignment
  "createdAt": "UTC", "updatedAt": "UTC", "closedAt": "UTC | null"
}
```

As-built: customers open + track own; agents see queue (status/assignee filters),
assign (spins up customer+agent group chat on first assignment), advance
Open→Assigned→InProgress→Resolved→Closed (Resolved↔InProgress reopen); anyone
involved may close; Closed terminal. Mutations audit-logged.

### 2.7 `devices` (sessions + push)

```jsonc
{
  "_id": "ObjectId",
  "userId": "ObjectId",
  "platform": "web | android | ios",
  "pushToken": "fcm-token | null",
  "userAgent": "...",
  "lastActiveAt": "UTC",
  "refreshTokenHash": "...",         // rotating refresh per device; revoke per device
  "createdAt": "UTC"
}
```

## 3. Relationships

```
users 1───* chats.members[]        (embedded membership; memberCount denormalized)
chats  1───* messages              (messages.chatId; ordered by seq)
messages *───0..1 files            (messages.fileId → files._id)
users  1───* files                 (ownerId)
chats  1───* calls                 (calls.chatId)
users  1───* devices               (devices.userId)
users  1───* tickets               (tickets.customerId; support staff via assigneeId)
tickets 0..1───1 chats             (tickets.chatId, created on assignment)
```

No cross-collection joins at query time except `fileId → files` lookup on demand.
Chat list screen = **one query** (`chats` by `members.userId`, sorted by `lastMessageAt`).
Message history = **one range query** on `messages` by `(chatId, seq)`.

## 4. Indexes (create at startup, idempotent)

| Collection | Index | Options | Serves |
|---|---|---|---|
| users | `{ username: 1 }` | unique, sparse | login, directory |
| users | `{ phone: 1 }` | unique, sparse | OTP login |
| users | `{ displayName: "text" }` | text | directory search (MVP) |
| users | `{ isSupportAgent: 1, lastSeenAt: -1 }` | — | agent availability |
| chats | `{ "members.userId": 1, lastMessageAt: -1 }` | — | chat list per user |
| chats | `{ type: 1, lastMessageAt: -1 }` | — | support queue / channel browse |
| chats | `{ publicUsername: 1 }` | unique, sparse | public channels |
| chats | `{ inviteCode: 1 }` | sparse | invite join |
| chats | `{ "support.state": 1, "support.assigneeId": 1 }` | sparse/partial | agent queue |
| chats | `{ dmKey: 1 }` | unique, partial (`isDeleted:false` + key exists) | DM pair dedup + lookup (`ux_dm_key`) |
| messages | `{ chatId: 1, seq: -1 }` | — | history pages (primary) |
| messages | `{ chatId: 1, _id: -1 }` | — | cursor fallback |
| messages | `{ senderId: 1, clientMessageId: 1 }` | unique | idempotent resend |
| messages | `{ chatId: 1, createdAt: -1 }` | TTL optional (P1 retention) | TTL hook |
| messages | `{ body: "text" }` | text | MVP message search |
| files | `{ messageId: 1 }` | sparse | attachment lookup |
| files | `{ ownerId: 1, createdAt: -1 }` | — | user uploads |
| calls | `{ chatId: 1, createdAt: -1 }` | — | call history |
| calls | `{ chatId: 1 }` | unique, partial (status in Ringing/Active) | one-live-call guard (`ux_live_call`) |
| tickets | `{ status: 1, createdAt: -1 }` | — | agent queue triage |
| tickets | `{ customerId: 1, createdAt: -1 }` | — | customer "my tickets" |
| tickets | `{ assigneeId: 1, updatedAt: -1 }` | — | agent workload |
| devices | `{ userId: 1 }` | — | session list, push fan-out |
| devices | `{ pushToken: 1 }` | sparse | token dedupe |

Text indexes: one per collection max (Mongo limit) — `users.displayName`, `messages.body`;
 chats title search uses regex+prefix index `{ title: 1 }` (add if dir search needs it).

Index health notes (verified live, see `docs/performance.md`):
- `chats ix_type_list {type: 1, lastMessageAt: -1}` currently serves **no** query
  (pair lookup moved to `ux_dm_key`). Kept for now — removal candidate, needs approval.
- `avatars` carries `ux_object_key` (unique): avatar reads/deletes are by
  `objectKey`; previously every view was a collection scan.
- New partial-unique definitions roll out via drop+recreate guarded by
  error 85/86, restricted to indexes introduced with that mechanism
  (`MongoIndex` helper) — pre-existing indexes are never auto-recreated.

## 5. Query Patterns (reference)

- Chat list: `find({ "members.userId": me, isDeleted: false }).sort({ lastMessageAt: -1 }).limit(50)`.
  Connection setup uses the same filter/sort with an `_id`-only projection.
- History: `find({ chatId, seq: { $lt: cursor } }).sort({ seq: -1 }).limit(30)` then reverse.
- Send: `findOneAndUpdate(chat → $inc seqCounter)` → `insertOne(message)` →
  `updateOne(chat set lastMessage*, inc unread for others via arrayFilters)`.
- MarkRead: single conditional pipeline `updateOne` (ElemMatch on stale watermark;
  `$map`/`$mergeObjects` advance one member, `unreadCount` recomputed from live
  `seqCounter`). No separate read; monotonic under concurrency.
- FindDm: `find({ dmKey, isDeleted: false })` via `ux_dm_key`; legacy docs fall
  back to the `$all` pair scan.
- Refresh rotation: conditional `updateOne` on current-hash match (CAS); loser
  gets `InvalidRefreshToken`, never a clobbered retired-hash list.
- Search (MVP): `$text` on messages + `$regex` fallback for Persian substring gaps; scope by
  participant chatIds first (`chatId: { $in: myChatIds }`).

## 6. Growth & Capacity (rough)

Dominant cost = `messages`. At 50 staff + 500 active customers × ~50 msgs/day ≈ 27k msgs/day
≈ 10M/yr — trivial for MongoDB (single replica set, tens of GB with files in S3, not Mongo).
No sharding in MVP; shard key reserved (`{ chatId: 1, seq: 1 }`) if ever needed.

## 7. Migrations & Ops

- Schema version field reserved (`schemaV: 1`) on docs touched by breaking changes; additive
  fields default-friendly (nullable). No migration framework in MVP — startup index bootstrap
  + small idempotent fix-up scripts under `Infrastructure/Persistence/Mongo/Migrations/`.
- Backups: replica set + `mongodump` daily + MinIO snapshots; restore drill in Phase 0.
- PII: message bodies excluded from logs/dumps by default; export endpoint role-gated (admin).

## 8. Advanced Slice Deltas (implemented)

- `chats.members[].isArchived` (bool, default false) — per-member archive folder.
  `muteUntil` from §2.2 is reserved; muting uses `isMuted` only.
- `chats`: `isPublic` (channels), `username` (lowercase, unique sparse —
  implements `publicUsername`), `inviteCode` (opaque 128-bit hex, unique sparse),
  `avatarObjectKey`/`avatarUrl` (same `avatars` bucket as users, keyed
  `chats/{chatId}`), `membersCanInvite` (groups).
- `messages`: `reactions[]` (`{emoji, userId, at}`, cap 100/msg, 20 distinct
  emojis), `replyCount` (denormalized direct-reply counter, `$inc` on reply send).
- New indexes: `chats ux_channel_username`, `chats ux_invite_code`,
  `messages ix_thread {chatId: 1, replyToMessageId: 1, seq: 1}`.
- Link previews are client-side only (first URL → domain card, no server fetch —
  avoids SSRF and keeps `messages` writes unchanged).
