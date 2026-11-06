# Deployment — Messenger

## Environments

| Concern | On-premise (LAN) | Internet |
|---|---|---|
| TLS | Customer certificate on outer reverse proxy (or self-signed CA pushed to clients) | Let's Encrypt on outer proxy / load balancer in front of `web` |
| Compose file | `docker-compose.prod.yml` | Same file; set `Web__Origin`/`Web__ApiBaseUrl` to the public URL |
| LiveKit | `--profile media`; expose 7880 TCP + 5349 UDP on LAN | Same + open 7880/7881/7882 TCP and 5349 UDP; set `rtc.use_external_ip` (already on) |
| Push | In-app only (push sender is a logged NoOp) | Same until an FCM/APNs sender is implemented |

The dev `docker-compose.yml` is infrastructure-only (mongo/minio/livekit); the API
and web run from source. Production uses `docker-compose.prod.yml` (API + web
images, health-gated startup, persistent volumes).

## Configuration (environment variables)

| Variable | Required | Notes |
|---|---|---|
| `Jwt__SigningKey` | yes (min 32 chars) | Generate: `openssl rand -base64 48`. Rotation invalidates all sessions. |
| `Minio__AccessKey` / `Minio__SecretKey` | yes | Must differ from dev defaults or the API refuses to boot in Production. |
| `Mongo__ConnectionString` | no | Default targets the composed `mongo` service. |
| `Mongo__DatabaseName` | no | Default `messenger`. |
| `LiveKit__Url/ApiKey/ApiSecret` | for calls | Empty = calls fail closed (503 on join, signaling works). Must match `livekit.yaml` keys. |
| `Cors__AllowedOrigins__0` | no | Same-origin via nginx by default; set only for split-origin setups. |
| `Web__Origin` / `Web__ApiBaseUrl` / `Web__Port` | no | Public origin, API base baked into the SPA (empty = same origin), host port. |
| `SeedAdmin__*` | never in prod | The API **fails fast** if `SeedAdmin:Enabled=true` in Production. |

`appsettings.Production.json` switches Serilog to compact JSON (log aggregation
friendly). Dev `appsettings.json` keeps the human template.

## Backups

**MongoDB** (scheduled on the mongo host, e.g. cron):
```bash
docker exec messenger-mongo-1 mongodump --out /backup/mongo-$(date +%F)
# keep 7 daily dumps; copy off-host (S3/NAS) — dumps contain PII (message bodies).
```

**MinIO** (object bytes; metadata in MongoDB is useless without them):
```bash
mc mirror --overwrite myminio/messenger-media /backup/minio/messenger-media
# plus `mc version enable` on the bucket for overwrite protection.
```

## Restoration

1. Stop `api` (writes only): `docker compose -f docker-compose.prod.yml stop api web`.
2. Mongo: `mongorestore --drop /backup/mongo-<date>` (replica-set flags stay in compose).
3. MinIO: `mc mirror --overwrite /backup/minio/messenger-media myminio/messenger-media`.
4. Restart: `up -d`; verify `/readyz` (mongo), `/health/storage` (minio), then a
   login + history smoke test. RPO = backup interval; RTO ≈ restore time + verify.

## Health checks & logging

- `GET /healthz` — process liveness (load-balancer ping).
- `GET /readyz` — readiness (Mongo ping; orchestrators gate traffic on this).
- `GET /health/storage` — MinIO reachability (informational; uploads 503 while down).
- Logs: structured (JSON in Production), one line per request (correlation id
  header `X-Correlation-ID`), audit events for admin role/active changes and
  support assign/status transitions (actor + ids, no PII bodies).
- Metrics/tracing: not built in (P1 — Prometheus endpoint + OpenTelemetry).

## First boot checklist

1. `Jwt__SigningKey`, `Minio__*` set (compose errors early otherwise).
2. `up -d mongo minio` → `mongo-init` prints replica-set OK.
3. `up -d api web` → api logs "indexes ensured"; `/readyz` 200.
4. Register first user; promote to admin with a direct Mongo update (seed admin
   is dev-only and refused in Production):
   `db.users.updateOne({username:"..."}, {$set:{roles:["Employee","SystemAdministrator"]}})`.
   Record the site admin in the runbook.
