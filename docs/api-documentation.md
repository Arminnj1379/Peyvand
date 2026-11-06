# API Documentation

Base path `/api/v1`. All groups below require JWT bearer auth except where
noted. Errors are RFC 9457 problems with a machine-readable `code`
(`ResultMapper`). Validation failures are 400 with per-field messages.
Rate limits: global per-user/IP 600/min; credential endpoints (`auth`
policy) 20/min/IP → 429 `rate.limited`.

## Auth `/api/v1/auth` (anonymous unless noted)

| Method + route | Purpose | Key validation |
|---|---|---|
| POST `/register` | customer self-register + sign in → 201 | username 3–32, displayName 1–64, password 8–128, optional E.164 phone |
| POST `/login` | username-or-phone login | never reveals whether the login exists (dummy hash path) |
| POST `/refresh` | single-use refresh rotation | reuse of a retired token → full revoke + 401 `reuse_detected` |
| POST `/logout`, `/logout-all` (auth) | revoke current / all sessions | — |
| GET `/sessions` (auth) | own sessions, newest-first, max 100 | — |
| DELETE `/sessions/{id}` (auth) | revoke one own session (404 for others') | id ≤64 chars |
| POST `/change-password` (auth) | rotate password, revoke others | new password 8–128 |

## Users `/api/v1/users` (auth)

`GET/PATCH /me` (profile; bio ≤255), `POST/DELETE /me/avatar` (JPEG/PNG/WebP,
≤2 MB; multipart), `GET /me/settings` + `PATCH /me/settings` (theme
system/light/dark, notifications switch), `GET /search?q&limit` (q 2–64,
limit ≤50; employees see everyone, customers see employees only; phone only
for self), `GET /{id}` (visibility rule: self, employees, or employee
targets), `GET /{id}/avatar` (same visibility; public-channel avatars are
open), `POST/DELETE /{id}/block` (blocks enforced both directions on DM
create + send, neutral errors).

## Admin `/api/v1/admin/users` (`Admin` policy: System/OrganizationAdministrator)

`POST /` create employee (roles validated; only sys-admin grants admin
roles), `GET /` list (userType/role/isActive filters, cursor paging,
limit ≤100), `PATCH /{id}/active` (deactivation revokes sessions),
`PUT /{id}/roles` (base-role protection, grant hierarchy). Mutations are
audit-logged (actor + target + action, no PII).

## Chats `/api/v1/chats` (auth, membership-checked per operation)

`POST /dm` (idempotent pair create; 200 existing), `POST /groups`,
`POST /channels`, `GET /` (limit ≤100), `GET /search` (title/username/DM-peer,
q ≥2), `GET /{id}`, member add/remove/leave/role (admin rules; DM membership
fixed), pins (cap 10), `POST /{id}/read` (monotonic watermark),
mute/archive/unread prefs, invite get/rotate/revoke + `/join` by code,
channel subscribe/unsubscribe + visibility, avatar upload/remove/serve.

## Messages `/api/v1/chats/{chatId}/messages` + `/api/v1/messages` (auth)

Send (idempotent on `clientMessageId` per sender; 409 on cross-chat reuse;
optional caption + single `fileId` attachment), history (cursor `beforeSeq`,
limit ≤100, `hasMore` + `nextCursor`), sync (`afterSeq`, limit ≤200),
pins, edit (sender-only, captions may empty on attachments), delete
(own-or-admin), forward with provenance (bytes shared, never re-attached),
reactions toggle (caps 100/msg, 20 emojis), replies thread (limit ≤100),
bulk delete (1–50, authorize-all-before-mutate, skips stale ids),
`GET /messages/search` (full-text across own chats, limit ≤50, deleted
excluded).

## Files `/api/v1/files` (auth)

`POST /` multipart (per-kind caps image 10 / video 100 / audio 20 / document
25 MB; extension↔type + magic-byte checks; malware-scan seam; request-size
limit disabled so handler validation governs), `GET /{id}` (members, or
uploader while pending; neutral 404 otherwise; range requests; `?download=1`
save-as), `DELETE /{id}` (owner + pending-only; attached files follow message
retention).

## Calls `/api/v1/chats/{chatId}/calls` + `/api/v1/calls` (auth)

`POST` start (one live call per chat → 409 `already_active`; channels need
post rights), `GET` history, `GET` one, `POST /accept|/reject|/join|/leave|end`.
Join returns LiveKit URL + short-lived JWT (6 h); unconfigured LiveKit fails
closed with 503. End rules: DM either party; group/channel caller or admin.

## Notifications `/api/v1/notifications` (auth)

`GET /` inbox rows derived from read watermarks (limit ≤100); muted chats
included but flagged. No separate store (source of truth stays in chats).

## Support `/api/v1/support` (auth)

`POST /tickets` (any signed-in user; subject ≤128, description ≤2000),
`GET /tickets` (agents: full queue + status/assignee filters; customers: own
only), `GET /tickets/{id}` (visibility-checked), `POST /{id}/assign`
(agents; self when omitted; spins up the support chat on first assignment),
`POST /{id}/status` (workflow: agents forward, anyone involved may close;
Closed terminal). Assign/status audit-logged.

## System / health (anonymous)

`GET /api/v1/system/info`, `GET /healthz` (liveness), `GET /readyz` (Mongo
readiness gate), `GET /health/storage` (MinIO reachability, informational).
Swagger UI in Development only.

## Error conventions

Domain/application failures → `{ title, status, code }` with stable
`code` strings (`chat.not_found`, `message.client_id_conflict`,
`call.already_active`, `file.too_large`, `support.invalid_status`,
`rate.limited`, …). 401 unauthenticated/expired, 403 authenticated-but-
forbidden (+ neutral 404s where existence must not leak: downloads,
DM-block paths), 409 conflicts incl. uniqueness-race losses, 503 degraded
dependencies (storage, LiveKit).
