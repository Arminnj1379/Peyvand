# Backend Architecture

## Overall

Single deployable ASP.NET Core monolith (ADR-01). .NET 10, C# 13 (primary
constructors, collection expressions). MongoDB is the source of truth;
SignalR carries delivery-only events; LiveKit carries call media; MinIO/S3
carries file bytes. No message bus, no cache, no background workers beyond
two hosted services (index bootstrap, admin seed).

## Project structure

```
src/
  Messenger.Domain/          # net10.0, zero dependencies (verified: no PackageReference)
    Chats/                   # Chat aggregate (membership embedded, cap 500)
    Messages/                # Message aggregate (per-chat seq, reactions, reply count)
    Files/                   # ChatFile aggregate + FileValidation (limits, magic bytes)
    Calls/                   # Call aggregate (ringing/active/ended, participants)
    Support/                 # SupportTicket aggregate (workflow state machine)
    Users/ Sessions/         # User aggregate, DeviceSession (rotation, retired hashes)
    Common/                  # Result<T>, Error, Entity, IDomainEvent
  Messenger.Application/     # depends on Domain only (+ MediatR, FluentValidation)
    Abstractions/            # one port per aggregate (IChatStore, IMessageStore,
                             # IUserStore, IDeviceSessionStore, IFileStore,
                             # ICallStore, ISupportTicketStore, ...) + port-owned
                             # exception types (Duplicate*Exception) + ICurrentUser,
                             # IClock, IIdGenerator, token/storage/scanner ports
    Features/                # vertical slices: Auth, Admin, Chats, Messages,
                             # Files, Users, System (+ Calls/, Support/, Notifications/)
    Behaviors/               # MediatR pipeline: ValidationBehavior (fail fast)
    Messaging/ Users/ Auth/  # shared DTOs, mapping, access helpers (ChatAccess)
  Messenger.Infrastructure/  # depends on Application + Domain (+ MongoDB.Driver, MinIO)
    Persistence/Mongo/       # MongoDbContext, per-aggregate Stores/*, Documents/*,
                             # Mapping/* (document<->aggregate only), MongoInitializer,
                             # MongoIndex (conflict-safe bootstrap for NEW indexes)
    Auth/                    # JwtTokenService, PasswordHasher (PBKDF2), options
    Calls/                   # LiveKitOptions, LiveKitTokenService (hand-rolled JWT)
    Storage/                 # MinioFileStorage, MinioOptions/HealthCheck, NoOpFileScanner
    Push/                    # NoOpPushSender (logged stub; FCM/APNs insertion point)
    Time/ Ids/               # SystemClock, MongoObjectIdGenerator
  Messenger.Api/             # depends on Application + Infrastructure
    Endpoints/               # minimal-API groups per feature (persist-then-broadcast)
    Hubs/                    # MessengerHub (single hub) + presence tracker
    Auth/                    # JWT bearer setup, Admin/Support policies, HttpCurrentUser
    Middleware/              # CorrelationId, ExceptionHandling (incl. duplicate catch)
tests/
  Messenger.Domain.Tests/      # aggregate invariants, no I/O
  Messenger.Application.Tests/ # handler tests against in-memory fakes (no Mongo)
  Messenger.Api.Tests/         # full stack vs local Mongo (WebApplicationFactory)
```

## Layer responsibilities

| Layer | Owns | Must never |
|---|---|---|
| Domain | invariants, state transitions, errors; pure, no I/O, no BSON/HTTP | reference infrastructure, do time/ids itself (callers supply) |
| Application | use-case orchestration, authorization (`ChatAccess`, role checks), validation (FluentValidation), DTOs; depends on Domain + ports | touch MongoDB/driver, HTTP, SignalR, clock time (via `IClock`), ids (via `IIdGenerator`) |
| Infrastructure | port implementations (stores, token minting, storage, scanner, push) | contain business rules; bypass ports |
| API | routing, authN/Z enforcement, persist-then-broadcast fan-out, ProblemDetails mapping, rate limiting, hub | contain business logic; trust client-supplied ids |

Dependency rule: `Api → Infrastructure → Application → Domain`, plus
`Api → Application` for dispatch. `Duplicate*Exception` types live in
`Application.Abstractions` (port contract) even though Infrastructure throws
them — the codebase documents this explicitly on each type.

## Request lifecycle

1. Middleware: correlation id → exception handling → Serilog request log →
   CORS → rate limiter → authN → authZ.
2. MediatR dispatch → `ValidationBehavior` (FluentValidation; failures throw
   → middleware maps to 400) → handler (authN check → store reads →
   domain transition → store writes → DTO).
3. Endpoint fans out SignalR events to `chat:{id}` groups **only after** the
   handler commits (hub is delivery-only; reconnects re-sync via REST).
4. Domain failures return `Result.Fail` (mapped to RFC 9457 problems);
   infrastructure faults and uniqueness-race losses surface as typed
   exceptions mapped by the middleware (409) or handlers (409 results).

## Dependency injection strategy

- Stores/adapters: **singleton** (MongoDB.Driver clients/collections are
  thread-safe; no per-request state). `IMongoClient` singleton; collections
  resolved per call from `MongoDbContext` (cheap handle, no connection held).
- `ICurrentUser` (`HttpCurrentUser`): **scoped** (per-request claims).
- Validators/handlers: MediatR defaults (transient).
- `IPresenceTracker`: singleton (in-memory counts; single-instance exact).
- Hosted services: `MongoInitializer` (idempotent index bootstrap), `AdminSeeder`.
- Options validated on start (fail fast); JWT/LiveKit validation params are
  built at resolve time so late config sources (test hosts) are honored.
