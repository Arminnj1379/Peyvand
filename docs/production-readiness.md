# Production-Readiness Report — Messenger

Date: 2026-11-06. Scope: full pass over product features, UI/UX, security,
deployment, and testing. Nothing below is claimed complete without the
verification evidence cited in §6.

## 1. Product features (this pass)

| Feature | State | Evidence |
|---|---|---|
| Global message search | Done | `GET /api/v1/messages/search` (text index, scoped to caller's chats, deleted excluded); app + API tests |
| Conversation search | Done | `GET /api/v1/chats/search` (title/username/DM-peer); app + API tests |
| User search | Existed, kept | `GET /api/v1/users/search` (employee/customer scoping, phone never exposed) |
| Notifications inbox | Done | `GET /api/v1/notifications` derived from read watermarks; bell + dropdown + toasts in UI; API test |
| Unread counters | Existed, verified | Per-member watermarks; badges in list, bell total; muted styling |
| Notification preferences | Done | `GET/PATCH /api/v1/users/me/settings` (master switch gates toasts/badges note); per-chat mute kept |
| User settings | Done | Settings = theme (system/light/dark) + notifications switch; Profile page section; domain + app + API tests |
| Support inbox | Done | `tickets` aggregate + `/api/v1/support/*`; agent queue, self-assign, status workflow, assignment chat; Support page; domain + app + API tests |
| Conversation status | Done | Ticket workflow Open→Assigned→InProgress→Resolved→Closed (+reopen, cancel, terminal Closed) |

## 2. UI/UX

- Removed all placeholder/fake UI: design-preview mode, mock conversations,
  dead menu buttons, `Phase N` affordances (6 files deleted; persisted
  `preview` sessions migrate to anonymous).
- Every dialog/menu/drawer closes on Escape (`useEscape`) and focuses its
  primary input; pre-existing labels/roles kept; `aria-pressed`/`aria-expanded`
  on toggles; `role=alert`/`status`/`menu`/`dialog` where appropriate.
- Loading/empty/error states on: chat list, history, pins, calls, tickets,
  search (per-tab), notifications, sessions, avatar/profile/settings forms.
- Themes: explicit System/Light/Dark setting (`data-theme`, `color-scheme`
  set); OS preference remains the default. Call overlay stays intentionally
  dark (media surface, both themes).
- Responsive: 3-col → 2-col (≤1100px, details overlay) → single-column
  (≤820px, list/chat toggle); call grid collapses; dialogs/toasts/bell
  viewport-capped. `prefers-reduced-motion` disables animation.
- Message interactions unchanged (optimistic send, retry, realtime patch).

## 3. Security

- AuthN/Z: JWT bearer (issuer/audience/skew/role-claim verified), PBKDF2-SHA256
  210k hasher, rotating refresh with reuse detection, per-operation membership
  checks, neutral 404s for outsiders, phone scoped to self, admin/support
  role gates + handler re-checks.
- NEW: rate limiting (global per-user/IP 600/min + strict auth 20/min/IP,
  RFC 9457 `rate.limited` 429s). Verified live (429s observed under burst).
- NEW: upload hardening — `/files` exempted from Kestrel's 30 MB cap so the
  100 MB handler validation actually governs (was silently rejecting videos).
- File security re-verified: magic-byte sniffing, extension↔type consistency,
  per-kind caps, sanitized server keys, SHA-256, authorized proxy downloads.
- Secrets: Production fails fast on default MinIO creds and on
  `SeedAdmin:Enabled` (verified: process exits with actionable error);
  `Jwt:SigningKey` required at boot; compose requires secrets via env.
- Audit logging: admin create/active/role changes + support assign/status
  (actor + ids, no PII bodies); structured JSON logs in Production.
- Multi-tenancy: N/A by design (single organization; roles are the boundary).

## 4. Deployment

- `src/Messenger.Api/Dockerfile` (multi-stage, non-root `app` user, curl
  healthcheck) → builds: `messenger-api:local` 250 MB.
- `frontend/Dockerfile` + `nginx.conf` (SPA fallback, `/api` + `/hubs`
  proxy with WebSocket upgrade, 110 MB client cap, security headers) →
  builds: `messenger-web:local` 49 MB.
- `docker-compose.prod.yml`: api/web/mongo/minio (+ optional livekit
  `media` profile), health-gated startup, named volumes, restart policies,
  secrets-by-env with fail-fast interpolation. Syntax validated.
- `docs/deployment.md`: on-prem vs internet matrix, full env table, TLS
  guidance, backup (`mongodump` + `mc mirror`) and restore runbook, health
  endpoints, first-boot admin checklist.
- Health: `/healthz` (liveness), `/readyz` (Mongo readiness gate), NEW
  `/health/storage` (MinIO reachability, informational only — API degrades
  to 503s rather than failing readiness).

## 5. Testing

- Backend: 216/216 green — Domain 99, Application 76, API 41 (incl. 8 new
  domain, 6 new handler, 4 new API tests for this pass).
- Frontend: `npm run build` + `npm run lint` clean (codesplit warning only).
- Live smoke (dev API, real Mongo): message/chat search 1/1, settings
  round-trip, notifications 1/1, ticket open + non-agent 403, 429s under
  burst, health endpoints, production guard rejection — all as expected.
- Incidental fixes: test DB cleanup now uses `deleteMany` (drops were wiping
  the text index); `bool` query params given defaults (binding 500s);
  rate-limit config deferred to options-resolution (factory overrides apply).

## 6. Known limitations & remaining risks

1. **Push is a stub** (`NoOpPushSender`): notifications are in-app only.
   Offline devices get nothing. Needs FCM/APNs sender + device tokens.
2. **Single-node MongoDB** (replica set of one): no failover. Data is
   durable via volumes + backups, but availability is single-host. A 3-node
   overlay is still to do.
3. **No per-account login throttling**: rate limiting is per-IP + global;
   targeted password spraying across many IPs is only slowed, not locked.
   No CAPTCHA/MFA. Password floor is 8 chars (no breach screening).
4. **Persian full-text quality**: `$text` uses the default analyzer; Persian
   stemming/stop-words are unconfigured — search recall for Persian text is
   unverified. Consider language-specific text index or dedicated search later.
5. **Upload memory buffering**: 100 MB files pass through API memory
   (`IFormFile` → byte array). Concurrent video uploads can spike RAM.
   Presigned direct-to-MinIO upload is the follow-up.
6. **Malware scanning is a seam** (`NoOpFileScanner`): no real AV in the path.
7. **No message encryption at rest / E2E**: bodies sit in Mongo + backups
   (PII — encrypt backup storage and restrict DB access).
8. **Single-instance realtime**: presence is in-memory; scale-out needs sticky
   sessions (hook documented) — SignalR events would otherwise miss instances.
9. **No metrics/tracing**: logs + health endpoints only; add Prometheus/OTel
   before SLO-based operation.
10. **Backups are documented, not automated**: cron + off-host copy still need
    site wiring; restore runbook untested against a real dump.
11. **LiveKit single node, no recording**; TURN secret shares API secret env.
12. **Frontend has no automated UI tests**: new screens verified by build,
    lint, and API contracts only; visual/RTL spot-check on device still due.
13. **Admin user list capped at 100** with no search/pagination.
14. **Rate-limit 429 path** is verified live but not in the automated suite
    (suite runs with raised limits; see `MessengerFactory`).
15. **MinIO health is TCP reachability**, not an S3 API check — credential
    rot/deletion without port loss would read healthy until an upload 503s.
