# Implementation Priorities

Roadmap ordered by (user impact × unblocks-others) / risk. Every item reuses the
existing slice pattern (Domain → port → handler → store → endpoint → test) and the
persist-then-broadcast realtime contract. No rewrites proposed; all items are additive.

## P0 — Fix a real functional gap

| # | Item | Why first | Complexity |
|---|---|---|---|
| 1 | Chat-list pagination + subscription sync (G10) | Users with >50 chats silently lose chats and realtime; the only fix that changes correctness, not just parity | M |

Acceptance: 200-chat fixture scrolls fully, live delivery works on any loaded page,
`explain()` shows index-served paging.

## P1 — Telegram parity, core messaging

| # | Item | Depends on | Complexity |
|---|---|---|---|
| 2 | Voice messages: recorder + duration metadata (G1) | Nothing (audio kinds already allowed) | M |
| 3 | Shared media gallery/files tabs (G4) | Nothing; enriched by #2 | M |
| 4 | Pin conversation (G2) | Nothing | S–M |
| 5 | Draft persistence, local-first (G3) | Nothing | S |
| 6 | Delete-for-me choice (G5) | Nothing (keep global path untouched) | M |
| 7 | Blocked list + fix toggle state (G6-list) | Nothing | S |

Suggested order within P1: 5 → 4 → 7 → 2 → 3 → 6 (quick wins first; gallery lands
better once voice durations exist; delete-for-me last — it touches three read paths).

## P2 — Search, calls polish, deploy readiness

| # | Item | Depends on | Complexity |
|---|---|---|---|
| 8 | Search filters: sender/date/kind (G7) | Nothing | M |
| 9 | Screen sharing button (G8) | LiveKit deployed (#11) | S |
| 10 | Call history redial (G9) | Nothing | S |
| 11 | Enable LiveKit server in dev/staging | Ops only | S |
| 12 | Privacy page shell (blocked list + future flags) (G6-page) | #7 | M |

## P3 — Scale, security, platform (research/ops)

| # | Item | Notes |
|---|---|---|
| 13 | Push notifications (FCM/APNs) | Replaces "open app to learn" for offline users; needs device-token registry + sender impl (stub exists) |
| 14 | Server-synced drafts | Only if multi-device drafts matter; local-first (#5) covers 95% |
| 15 | Member paging / side-collection past the 500 cap (L1) | Trigger: real community-size channels |
| 16 | Multi-node presence (Redis backplane/sticky) (L2) | Trigger: second API replica |
| 17 | Retention/TTL purge + file-orphan reaper (L6) | Trigger: storage growth |
| 18 | Presigned direct-to-MinIO uploads (L3) | Removes 100 MB in-memory proxy worst case |
| 19 | E2E encryption research (L4) | Breaking protocol change; do not attempt incrementally |
| 20 | Login hardening: lockout/MFA/breach screening (L8) | Independent; can be pulled into P2 on demand |

## What NOT to do

- No rewrites of messaging/calls/auth slices — verified working with 227 green tests.
- No microservices/queue/cache layers for items above — none needs them.
- No new text indexes (one-per-collection limit) — filters compose on existing ones.
- No breaking DTO changes — all DB additions are nullable/additive (see G1/G2/G5).
