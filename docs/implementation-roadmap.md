# Implementation Roadmap

## Phase 0 — Foundations (1–2 weeks)

- [ ] Scaffold `Messenger.sln` per architecture.md (Domain/Application/Infrastructure/WebApi + tests).
- [ ] Docker Compose: `api, mongo (replicaset init), minio (bucket+policy), livekit (+coturn profile),
      web (nginx), firebase-stub/no-op for air-gap`.
- [ ] Config validation on startup (fail-fast), `/healthz` + `/readyz`, correlation-id + ProblemDetails.
- [ ] Mongo bootstrap: collections + indexes from database-design.md, `schemaV` reservation.
- [ ] CI: build + test + docker build; CD draft for on-prem (compose pull/up) and internet (TLS profile).
- [ ] Auth skeleton: login/refresh/revoke + JWT validation + `ICurrentUser`.
- **Exit:** `docker compose up` → SPA shell loads, login round-trip works, health checks green.
- **Risks:** LiveKit/coturn NAT on customer networks → spike-test early with 2 external clients.

## Phase 1 — Auth, Users, 1:1 Chat (2–3 weeks) — *first demoable slice*

- [ ] Users: profiles, avatars (via Media presign), directory search (privacy-scoped), block.
- [ ] Chats: create DM, chat list (unread/preview), history cursor paging, idempotent send,
      edit/delete windows, reply/forward, pins.
- [ ] Realtime: `MessengerHub` (subscribe, message:new/edited/deleted, typing, presence, markRead).
- [ ] Presence: heartbeat + lastSeen throttle; typing expiry + rate limit.
- [ ] Tests: handler tests (send/read/authorization), API tests (auth + DM incl. non-member 403).
- **Exit:** two browsers can DM with typing/presence/read ticks + offline gap-fill.
- **Risks:** seq contention (mitigated by in-doc `$inc`); SignalR affinity on proxy (document stickiness).

## Phase 2 — Groups, Channels, Media, Search, Notifications (2–3 weeks)

- [ ] Groups (roles, invite codes, add/remove/promote), channels (broadcast + public/private).
- [ ] Media: presigned upload/confirm/download, 100 MB default cap, image preview inline.
- [ ] Search: message `$text` + fallback, directory + file-name search, scoped to membership.
- [ ] Notifications: unread model, mute, in-app toasts + FCM send (no-op path for air-gap).
- [ ] Multi-device: device list + per-device revoke.
- **Exit:** group/channel flows + file share + search + push ping → pull work; demo to pilot team.
- **Risks:** Persian substring search gaps → acceptance-test with real fa data; fallback regex tuned.

## Phase 3 — Calls (1–2 weeks)

- [ ] Call API + hub signaling (invite/accept/decline/timeout) + LiveKit token service.
- [ ] 1:1 audio/video UI (`CallOverlay`), ringing on all callee devices, missed-call system messages.
- [ ] Small group calls (≤10) reusing same room model.
- [ ] Call history + durations; no recording (schema-reserved).
- **Exit:** 1:1 + 3-party calls on LAN and internet (via coturn) with connect success logged.
- **Risks:** firewall/UDP blocked → coturn/TLS fallback mandatory in internet profile; headset/permission UX.

## Phase 4 — Support Workflows (1–2 weeks) — *MVP complete*

- [ ] Support chat subtype + queue states; customer entry; agent least-load auto-assign + manual/transfer.
- [ ] Lead view (queue, reassignment), resolve/close/reopen, transcript view.
- [ ] SLA timestamps recorded (dashboard = P1); canned replies seed.
- [ ] Role policies enforced in handlers + hub; e2e tests for assignment + privacy (agent A can't see
      agent B's tickets; customer can't see internal state).
- **Exit:** customer→queue→assigned→resolved→closed round-trip demo; pilot with real support team.
- **Risks:** assignment fairness under agent flapping → presence-gated + reconciliation timer.

## Phase 5 — Hardening & Pilot (1–2 weeks)

- [ ] Rate limits (auth, send, search), upload validation (type/size), audit log basics.
- [ ] Backup/restore drill, log redaction check, TLS + secret rotation runbook.
- [ ] Load smoke: simulated 500 concurrent hub connections + 50 msg/s; p95 send→deliver measured.
- [ ] RTL/fa pass on shell, a11y basics, empty/error states.
- **Exit:** pilot with ~50 staff + invited customers; go/no-go checklist signed.

## Phase 6 — Post-MVP (P1 backlog, prioritized after pilot)

Reactions → polls/threads → drafts/scheduled → albums/stickers → screen-share UI →
recording+consent → advanced search filters → agent notes/ratings/SLA dashboards →
moderation → Redis backplane → CDN/lifecycle → Prometheus/Grafana + tracing.

## Effort Sketch

MVP (Phases 0–5): ~7–12 weeks with 2–3 devs (1 backend-leaning, 1 frontend-leaning, part-time DevOps).
Assumes boring-tech discipline and no scope creep into P1. Add 30% buffer if Persian/RTL,
on-prem air-gap, or LDAP import turn out complex.

## Top Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| NAT/firewall breaks calls externally | P0 feature fails in prod | coturn+TLS from Phase 0; external-client spike in Phase 0 |
| Air-gapped push impossible | missed notifications | in-app + badge fallback; FCM behind interface, no-op sender |
| Search quality (fa) disappoints | UX complaints | acceptance corpus; regex fallback; Elastic explicitly deferred, not hacked in |
| Hot-group seq contention | send latency | in-doc atomic `$inc` measured in Phase 5 smoke; counters-collection fallback ready |
| Scope creep (bots/E2E/threads) | MVP slips | features.md P0/P1/out enforced in review; ADRs cited |
