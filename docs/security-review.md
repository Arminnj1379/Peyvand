# Security Review (backend pass)

## Model

- Authentication: JWT bearer (issuer/audience/signing-key validated, 30 s
  clock skew, `RoleClaimType = ClaimTypes.Role`); SignalR accepts the token
  via `?access_token=` on the hub path only. Refresh tokens are opaque,
  single-use, hash-stored, rotating with a bounded retired list; reuse of a
  retired token revokes all of the victim's sessions.
- Authorization: two layers — endpoint policies (`Admin`, `Support`, or plain
  authenticated) PLUS per-operation resource checks inside handlers
  (`ChatAccess.RequireMember/CanPost`, role checks, ticket visibility,
  download authorization). Hub methods re-verify membership per call and take
  identity exclusively from JWT claims.
- Passwords: PBKDF2-SHA256 210k iterations, per-user salt, constant-time
  verify; login failures (unknown user vs wrong password) cost the same via
  a dummy-hash path. Minimum length 8 (no breach screening — see risks).

## Findings fixed in this pass

1. **DM duplicate race** — concurrent creates bypassed the pre-check and
   inserted duplicate conversations. Fixed with deterministic `DmKey` +
   partial-unique index + idempotent return-existing.
2. **Parallel live calls** — same race shape on call start. Fixed with
   partial-unique `ux_live_call` + 409.
3. **Refresh rotation lost-update** — concurrent refreshes clobbered the
   retired-hash list (spurious logouts; weakened theft detection). Fixed
   with atomic compare-and-swap rotation; loser fails closed without revoke.
4. **Fail-open id filters** (`tickets`, user cursor) — malformed filter ids
   silently dropped the predicate (unfiltered results) or restarted
   pagination. Now fail closed (`[]`).
5. **Hub over-read** — `Typing`/`Subscribe`/`Delivered` loaded full chat docs
   (up to 500 members) per invoke; `Delivered` read twice. Replaced with an
   existence probe / dropped the redundant read. Same verdicts, less data.
6. **Registration race shape** — already 409-safe via middleware; unified to
   the standard result path in-handler and moved the exception type to the
   port layer.

## Reviewed, no change (with rationale)

- **Message existence oracles** (message fetched before membership check in
  react/replies/edit/delete): probing requires knowing an unguessable
  ObjectId (prior access); fixing fully is impossible without chat context
  for nonexistent ids. Accepted low risk; membership-first remains the
  convention for all chat-scoped entry points (send/history/forward/pins).
- **`GetPublicChannel` without caller verification**: by design — public
  channel cards are discoverable (route still requires authentication).
- **Avatar serving**: same-visibility-as-profile rule; public-channel avatars
  open by design. Object keys are server-generated, unguessable, never
  client-influenced; downloads proxy through authorized endpoints.
- **File uploads**: magic-byte + extension + per-kind caps enforced before
  storage; malware scanning is a seam (NoOp) — true AV still pending (risk).
- **Rate limiting**: per-IP auth window + per-user global window; login has
  no per-account lockout (risk — listed below).
- **Audit surface**: admin create/active/role + support assign/status log
  actor + ids (no PII). Message bodies never logged. No full audit trail
  (documented P1).

## Remaining risks

1. No MFA / no login lockout / no breach-password screening — targeted
   credential stuffing slowed only by IP rate limits.
2. Malware scanning not implemented (`NoOpFileScanner`).
3. No E2E encryption; bodies in Mongo + backups (PII — restrict DB/backup access).
4. Push sender is a stub (no FCM/APNs) — no device-side security events.
5. Message-ID oracles above (accepted, negligible).
6. Admin surface (list filters) trusts admin role only — correct, but admin
   JWT theft is high-impact (short access TTL + rotation mitigate).
