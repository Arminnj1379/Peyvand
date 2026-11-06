# Messenger — Enterprise Messaging Platform (foundation)

Single-backend monolith (no microservices): ASP.NET Core API + React SPA,
MongoDB source of truth, SignalR real-time, LiveKit media, MinIO/S3 storage.
Design docs live in [`docs/`](docs/) and are normative.

## Status

**Auth + Users module implemented** (roadmap Phase 1 slice): registration/login,
JWT + rotating refresh tokens, sessions, profiles, avatars, roles, admin
management — with 57 automated tests. Messaging (chats, composer, calls)
arrives in later phases. Preview conversation data in the UI is static layout
data, clearly badged, and is replaced by the real API in the messaging slice.

Local dev login: `admin` / `ChangeMe123!` (seeded by `SeedAdmin` in Development;
change or disable via config in shared environments).

## Backend (`src/`, .NET 10)

| Project | Depends on |
|---|---|
| `Messenger.Domain` | nothing |
| `Messenger.Application` | Domain (+ MediatR, FluentValidation) |
| `Messenger.Infrastructure` | Application, Domain (+ MongoDB.Driver) |
| `Messenger.Api` | Application, Infrastructure |

```powershell
dotnet build Messenger.slnx
dotnet run --project src/Messenger.Api          # http://localhost:5094
# Swagger:  http://localhost:5094/swagger       (Development)
# Liveness: http://localhost:5094/healthz
# Readiness (incl. Mongo ping): http://localhost:5094/readyz
```

Config: `src/Messenger.Api/appsettings.json` (`Mongo`, `Cors`, Serilog).
Options are validated on startup (fail fast).

## Frontend (`frontend/`, React 19 + TS + Vite)

```powershell
cd frontend
npm install
npm run dev     # http://localhost:5173 (proxies nothing; calls API directly)
npm run build
```

`frontend/.env.development` sets `VITE_API_BASE_URL=http://localhost:5094`
(must match the API port / CORS origins). The sidebar status pill calls the
real `GET /api/v1/system/info` — green means the API is up.

## Local dependencies

```powershell
docker compose up -d mongo minio
```
