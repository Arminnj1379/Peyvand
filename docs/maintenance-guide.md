# Maintenance Guide

## Add a new feature (vertical slice)

1. **Domain** (`src/Messenger.Domain/<Area>/`): aggregate + errors + snapshot
   (optional trailing record params keep old constructions compiling).
   No dependencies, no I/O, no `DateTime.UtcNow` (callers pass `now`).
2. **Port** (`Application/Abstractions/I<Area>Store.cs`): one method per
   use-case need. Races need a typed `Duplicate*Exception` in Abstractions
   (Infrastructure throws, Application owns the type). Bulk reads get a
   `ListByIdsAsync`; hot membership checks get an existence probe, not a
   full load.
3. **Handlers** (`Application/Features/<Area>/` or top-level folder for big
   slices): command/query + FluentValidation validator + handler. Authorize
   from `ICurrentUser` first, then resource checks (`ChatAccess` pattern).
   One store round-trip per need; batch enrichment via shared map helpers
   (`UserMapAsync` style); never N+1 loops.
4. **Store** (`Infrastructure/Persistence/Mongo/Stores/`): document +
   mapping + index creation in `EnsureIndexesAsync` (named indexes, options
   justified in a comment). Register the store + port in
   `InfrastructureServiceRegistration`, add to `MongoInitializer`.
5. **Endpoint** (`Api/Endpoints/`): minimal-API group, `RequireAuthorization`
   (+ policy), `ResultMapper.ToHttp`, SignalR fan-out only after commit.
6. **Tests**: domain invariants → `Domain.Tests`; handler authz/lifecycle
   incl. race paths (fake flags like `SimulateDmRaceOnAdd`) →
   `Application.Tests`; happy path + authz + new indexes → `Api.Tests`.

## Add a MongoDB collection + indexes

1. `Documents/<Name>Document.cs` (BSON attrs; `BsonIgnoreIfNull` on every
   optional field — required for sparse/partial unique indexes to work).
2. `Mapping/*Mappers.cs` entry + snapshot field (optional params).
3. Store with `EnsureIndexesAsync`: name every index (`ix_` lookup,
   `ux_` unique); comment the query each index serves; prefer partial over
   sparse when the predicate needs more than presence.
4. Wire into `InfrastructureServiceRegistration` + `MongoInitializer`.
5. Cover with `MongoIndexesTests` (assert name + key JSON).
6. Rule: never drop/rename a shipped index without approval. New (unshipped)
   definitions may use the `MongoIndex.CreateOneHandlingConflictAsync`
   helper, which heals error 85/86 by drop+recreate with a warning log.

## Run migrations / index initialization

- No migration framework: startup `MongoInitializer` creates indexes
  idempotently; additive schema changes must be default-friendly (nullable
  + rehydrate guards). Breaking reshapes need an approved script — there is
  no `Migrations/` runner; do not invent one inline.

## Add tests

- Domain: pure invariants + transitions + rehydrate round-trips.
- Application: fakes in `Fakes.cs`/`MessagingFakes.cs`/`CallFakes.cs`/
  `SupportFakes.cs` must mirror store semantics that handlers depend on
  (uniqueness throws, CAS checks, fail-closed filters) — a fake that is
  more permissive than the store hides race bugs.
- API: full-stack flows against local Mongo (`messenger_auth_tests`,
  sequential `auth` collection). `CleanAsync` uses `deleteMany`, never
  drops (drops wipe indexes). Rate-limit test config lives in
  `MessengerFactory` (raised windows); the 429 path is verified live, not
  in-suite.

## Local development and deployment

- Deps: `docker compose up -d mongo minio` (mongo is a single-node replica
  set; index creation needs no transactions).
- API: `dotnet run --project src/Messenger.Api` (dev JWT key is ephemeral).
  Frontend untouched by backend work (out of scope for backend reviews).
- Prod: `docker-compose.prod.yml` + required env (see `docs/deployment.md`);
  Production boots fail fast on default MinIO creds / SeedAdmin.
- Verify query plans after touching a store query:
  `db.<coll>.find(...).explain("executionStats")` — assert `IXSCAN` on the
  intended index and `totalDocsExamined ≈ limit`, never `COLLSCAN` on hot paths.
