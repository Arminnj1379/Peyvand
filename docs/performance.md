# Performance

All query-plan claims below were measured with `explain()` against local
MongoDB 8 on seeded data (see `C:\Users\a.najafi\AppData\Local\Temp\opencode\plans*.js`
for the exact scripts), except where marked THEORETICAL (scale reasoning, no
production traffic yet). No invented metrics: where numbers matter, the
verification command is given instead.

## 1. Issues discovered → fixed (this review)

### 1.1 Thread replies scanned the collection (FIXED, measured)

`ListRepliesAsync` filtered on `replyToMessageId` alone, but `ix_thread` leads
with `chatId` — the index could never serve it.
Measured before: `SORT <- COLLSCAN`. After scoping the store call to
`(chatId, replyToMessageId)` (`IMessageStore.ListRepliesAsync` signature
change): `LIMIT <- FETCH <- IXSCAN [ix_thread]`, no sort stage. At thread
scale this is full-scan → indexed-point-query.

### 1.2 Mark-read did a full-doc read + racy write (FIXED, measured via tests)

`MarkReadAsync` loaded the entire chat (up to 500 members) to check the
watermark and compute `unread`, then issued a second write — and the
check-then-act window allowed a stale concurrent write to regress the
watermark. Now a single conditional pipeline update: `ElemMatch` on the
stale watermark (monotonic by construction) + `$map`/`$mergeObjects` to
advance one member + `unreadCount` recomputed from the live `seqCounter`
(`$convert` preserves the int field type). Covered by
`MarkRead_recomputes_unread_server_side` (partial read → 1, stale read →
no-op, full read → 0). Note: pipeline updates support neither `arrayFilters`
nor positional `$` (driver throws `NotSupportedException`) — hence `$map`.

### 1.3 N+1 user enrichment on every list/history page (FIXED, by construction)

Chat lists cost O(chats × members) sequential `GetByIdAsync`; history pages
O(senders + replies) sequential. New `IUserStore.ListByIdsAsync` (`$in` on
`_id`, malformed ids skipped) collapses each enrichment to constant
round-trips: chat list = 1 chat query + 1 user batch; history page = 1 +
senders batch + replies batch + files batch; ticket pages = 1 batch;
group/channel validation = 1 batch; DM block check = 1 batch. Same
projections, same "unknown" fallbacks — verified by the unchanged 225-test
suite (enrichment output is asserted in message/search/support flows).

### 1.4 Hub hot paths loaded full chat docs (FIXED, measured)

- `Typing` (per keystroke burst) and `Subscribe` (per chat, ≤200): full-doc
  `GetByIdAsync` → new `IChatStore.IsMemberAsync` existence probe
  (`_id` + membership + live filter, `_id` projection). Measured: `IXSCAN [_id_]`.
- `Delivered`: dropped the redundant `GetByIdAsync` — `MarkDeliveredAsync`'s
  filter already requires membership + live chat, so verdicts are identical
  with one fewer round-trip.
- Connect/disconnect: `ListForUserAsync` (200 full docs with member arrays)
  → new `ListIdsForUserAsync` (`_id` projection, same filter/sort).

### 1.5 DM pair lookup had no usable index (FIXED, measured)

`FindDmAsync`'s `$all` over `members.userId` couldn't use the available
prefixes. New deterministic `Chat.DmKey` (`min:max` pair string) + sparse
partial-unique `ux_dm_key` (live docs only) makes lookup `IXSCAN [ux_dm_key]`
(measured) and makes concurrent creates collide → idempotent return-existing
instead of duplicate DMs (covered by `Concurrent_dm_create_collapses_to_one`
against real Mongo). Legacy docs without the key fall back to the old pair
scan; re-creation after soft delete is allowed by the partial filter.

### 1.6 Double live calls under concurrency (FIXED)

`StartCallHandler`'s check-then-act had no backstop. New partial-unique
`ux_live_call` (`{chatId}`, only `Ringing`/`Active`) + `DuplicateCallException`
→ `call.already_active` 409. Ended-call history stays unbounded (excluded
from the partial filter). Covered at handler level (`ThrowOnAdd` fake).

### 1.7 Refresh rotation lost-update window (FIXED)

Load → `Rotate` → full-doc replace let concurrent refreshes clobber each
other's retired-hash list (spurious logouts; weakened theft detection). New
`IDeviceSessionStore.TryRotateAsync` is an atomic compare-and-swap on the
current hash (`$push`/`$slice` keeps the retired list bounded exactly like
the domain). Loser gets `InvalidRefreshToken` — explicitly NOT treated as
theft (client replays with its own new token). Covered by
`Refresh_lost_rotation_race_fails_without_revoke` + existing reuse tests.

### 1.8 Avatar reads were collection scans (FIXED, measured index state)

`LoadAsync`/`DeleteAsync` query by `objectKey`, which had no index. New
unique `ux_object_key` (keys are server-generated unique per upload).
Verified present via `MongoIndexesTests` + live `getIndexes()`.

### 1.9 Unbounded session listing (FIXED)

`ListByUserAsync` had no limit while every login inserts a session (TTL is
the only trim). Now capped (100) server-sorted by `LastUsedAt` to match the
handler's order.

### 1.10 Duplicate-registration 500-shape inconsistency (FIXED)

Concurrent same-username/phone registration already returned 409 via the
middleware backstop, but as ad-hoc JSON (different shape from `ResultMapper`
problems). Handlers now catch `DuplicateUserException` in-line → uniform
409 results; middleware catch retained as backstop. The exception type moved
`Infrastructure → Application.Abstractions` per the codebase's own
port-owned-type rule (also removes the Api→Infrastructure using).

## 2. Deliberately NOT changed (measured or reasoned, documented)

- `ListForUserAsync` two-key sort vs `ix_member_list` prefix: measured
  `SORT <- FETCH <- IXSCAN [ix_member_list]` — the second key (`UpdatedAt`)
  sorts ≤200 docs in memory. Trivial; changing the sort would alter tie
  order for message-less chats. Revisit only with `explain()` evidence at
  10k+ chats/user.
- `$text` search + `CreatedAt` sort: measured `SORT <- TEXT_MATCH` — in-memory
  sort of ≤50 rows. Trivial. Verify with `explain("executionStats")` if the
  limit ever grows.
- User directory regex (`SearchAsync`): unanchored case-insensitive regex
  cannot use the text index (COLLSCAN by construction). Kept: substring
  semantics ("naj" → "a.najafi") are the product requirement and the user
  collection is tiny (staff + customers). The `txt_display_username` index
  is retained for a future prefix/text path — flagged, not dropped.
- `ListByIdsAsync` (messages/files) has no `Limit`: every caller is
  validator-bounded (pins ≤10, bulk ≤50, history attachments ≤ page size).
  Documented as caller-bounded; do not add a store cap that would silently
  truncate.
- `TicketStore.ListAsync` empty-filter full scan: legitimate agent-queue use,
  `Limit`-bounded (1..100). Malformed id filters now fail closed (`[]`).
- `ix_type_list {type, lastMessageAt}`: serves NO query after the dmKey move
  (verified by grep + plans). Kept — removal needs approval (write overhead
  is one small key per chat write; deletion is one line when approved).
- Full-document `ReplaceOneAsync` for aggregate updates: kept deliberately.
  Aggregates are small (≤500 embedded members); targeted updates are used
  exactly where hot paths need them (seq alloc, watermarks, delivery,
  reply count, rotation). Do not "optimize" cold-path replaces into
  field-level updates — it would split the aggregate invariant across layers.
- `RevokeAllForUser*` guards added (early return on malformed ids); behavior
  for valid ids unchanged.
- `AllocateSeqAsync` now takes `now` (no hidden `DateTime.UtcNow`) — clock
  consistency + test determinism, same atomicity.

## 3. Memory and CPU considerations

- Largest per-request payloads: chat docs (500 members) on list/history
  auth paths — mitigated by id-projection on connect/presence paths; list
  pages still need full docs for watermarks/previews (by design).
- `MinioFileStorage` buffers whole files in memory (100 MB video worst
  case) — inherent to the API-proxy download design (ADR-07); presigned
  direct-to-MinIO is the documented follow-up, not a bug fix.
- No caching added (no invalidation-safe candidate found); `IMongoClient`
  and stores are singletons, no per-request construction cost.
- Logging: Serilog request logging + targeted warnings; message bodies
  never logged (PII rule); audit logs carry ids only.

## 4. Future bottlenecks to watch (with verification commands)

- `messages` growth (dominant collection): history range query is
  index-optimal; watch `totalDocsExamined == limit+1` via
  `db.messages.find({chatId, seq:{$lt:N}}).sort({seq:-1}).explain("executionStats")`.
- `chats` member-array growth toward the 500 cap inflates every chat load;
  the cap + id-projection paths are the guardrails. If raised, re-measure
  list pages and consider member paging (P1 in design doc).
- `device_sessions` churn (one doc per login): TTL trims; watch collection
  count vs active users.
- `FindDmAsync` legacy `$all` fallback: only hit by pre-`dmKey` docs;
  harmless at DM-lookup frequency. Optional backfill needs an approved
  migration script (pair sort must mirror `Chat.BuildDmKey`); safe to defer
  indefinitely since the fallback is correct, just unindexed.
