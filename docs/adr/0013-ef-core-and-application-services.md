# 0013. Logic in C# on EF Core, a hand-written REST API

Status: accepted (2026-11-01)

## Context

Shipment is meant to be built and maintained the way a .NET team usually does it: the business rules in C#, where the
compiler and the tests see them, a designed REST API, and one database engine (PostgreSQL 18).

## Decision

- **Layers**, libraries by `ProjectReference`:
  - `libs/Shipment.Domain` - entities per bounded context with their transitions (`Order.FinishPicking`,
    `Delivery.Advance`, `Courier.EndShift`, ...), `Result`/`Error` for predictable refusals, the operator's time zone,
    distances. No EF, no ASP.NET Core.
  - `libs/Shipment.Persistence` - `ShipmentDbContext`, one `IEntityTypeConfiguration` per entity, snake_case names
    (`EFCore.NamingConventions`), enums stored as their snake_case names, `xmin` as the row version, the audit columns
    stamped by a `SaveChangesInterceptor`, the **EF Core migrations** - the only source of the schema.
  - `libs/Shipment.Application` - one service per bounded context (`CrmService`, `PickingService`, `DispatchService`,
    ...): commands go through the domain and end in one `SaveChanges` that also holds the outbox messages (ADR 0007);
    queries project to `*Row` records. A short `DbContext` per operation from a scoped `IDbContextFactory` (Blazor
    circuits live long).
  - `apps/Shipment.Api` - the Blazor pages call the services **in process**; the REST API (`Endpoints/`, Minimal API,
    `/api/v1`, OpenAPI at `/openapi/v1.json`, Scalar at `/scalar/`) is a thin adapter over the same services. One
    session cookie for both.
  - `apps/Shipment.Runner` - the outbox relay and Wolverine handlers, each a thin adapter over a service.
  - `apps/Shipment.DbMigrator` - `prepare` / `migrate [--seed]` / `finish`: the same code in `deploy.sh` and the tests.
- **Schema**: `identity`, `crm`, `catalog`, `carriers`, `compliance`, `ordering`, `dispatch`, `tracking`, `billing`,
  `messaging`, plus `wolverine` owned by the runner's role. The migration `AccessRoles` creates `shipment_api` and
  `shipment_runner` (NOLOGIN; DML on the modules' tables, read-only reference data, no UPDATE/DELETE on the ledger)
  and the ledger's append-only trigger. The migration `CheckConstraints` adds CHECKs (enum values, ranges, the
  consistency of states), so the database refuses what the domain would. The migrator owns everything.
- **Refusals**: `Error(Kind, Message)` - `Validation` 400, `NotFound` 404, `Conflict` 409 (stale row version,
  conflicting id, wrong state), with plain English messages.
- **Concurrency**: the client sends back `rowVersion` (xmin); the service compares it and EF checks it again at
  UPDATE. A change of a customer's address updates the customer row too, so two changes of the default address at
  once cannot both win.
- **Ids from the client** (ADR 0009) and idempotent creates; shipment, ledger and ping ids are made in C#.
- **Bounded contexts** (ADR 0008) as namespaces and services. A service reads its own tables and lower contexts'
  data; reactions upwards go through the outbox and the runner.

## Consequences

Plus:
- One language for the rules; the compiler and the tests see them. The domain is testable without a database.
- The REST API is designed (names, tags, versions); pages have no HTTP round trip to the host.
- Standard tooling: `dotnet ef migrations add`, EF's change tracking, CSharpier, JetBrains inspection.

Minus:
- The database guards the values (CHECKs, unique keys, the ledger trigger) but not the transitions and the cross-row
  rules for a client that writes SQL directly; only the host and the runner write.
- Module boundaries inside one DbContext are a convention, not a grant.
- Who and when are the `created_by` / `modified_by` columns; there is no separate audit log.
- Some reads (clearance, tracking ETA, billing summaries by day) are computed in C# after a narrow query - fine at the
  demo's scale, a view or a projection table later if it grows.
- Endpoints are written by hand: the price of a designed API.

## Considered

- Pages calling the REST API through a generated client: rejected - an HTTP hop inside one process.
- No broker, the outbox processed by a `BackgroundService` in the host: rejected - the runner, RabbitMQ and their
  retries stay as in ADR 0007.
