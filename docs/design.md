# Design

## Repository

```text
apps/Shipment.Api/          host: Blazor (Components/) on the application services, REST API (Endpoints/), /auth,
                            OpenAPI + Scalar, health
apps/Shipment.Runner/       runner: outbox relay, Wolverine on RabbitMQ, handlers over the application services
apps/Shipment.DbMigrator/   prepare | migrate [--seed] | finish (migrator role, database, EF Core migrations, demo
                            data in Seed/, logins, the accounts' password)
libs/Shipment.Domain/       entities and rules per bounded context, Result/Error, operator time, distance
libs/Shipment.Persistence/  ShipmentDbContext, configurations, Migrations/, save helpers
libs/Shipment.Application/  one service per bounded context: commands and queries
libs/                       Geo, Messaging.Contracts, Observability(.AspNetCore), Emails
tests/Shipment.Tests/       HTTP and service tests of the host on a real database (groups: ./verify <group>)
deploy/                     deploy.sh / remove.sh on the shared stack hr-local
docs/                       domain, design, ADRs, screens
```

## A request (ADR 0013)

1. The browser holds the session cookie `shipment.auth` (8 h, `Session__Lifetime`).
2. A Blazor page calls an application service in process (`CrmService.UpdatePartnerAsync(...)`). The circuit's user is
   the actor (`CurrentActor`, set by a `CircuitHandler`); an API request's user is the request's.
3. The service makes a short `ShipmentDbContext` (scoped factory), loads the aggregate, checks the row version the
   client read, lets the domain change it and saves once - with the outbox messages of the change.
4. A refusal is a `Result` with an `Error`: `Validation` → 400, `NotFound` → 404, `Conflict` → 409 (stale row version,
   id used by something else, wrong state). The page shows the message; the API returns ProblemDetails with it as
   `detail`. A race that EF catches at save (xmin moved, unique key taken) is a 409 too.
5. The REST API (`/api/v1/...`, constants in `Endpoints/ApiRoutes.cs`, one `IEndpointGroup` per context) is the same
   services behind Minimal API endpoints; `/openapi/v1.json` describes it.

## Database roles

| Role | What it does |
|---|---|
| `shipment_migrator` | runs the EF Core migrations; owner of the database and every object; CREATEROLE, never a superuser |
| `shipment_api` | the host: DML on the modules' tables; reads only the reference data; no UPDATE/DELETE on the ledger |
| `shipment_runner` | the runner: the same rights, and owner of the schema `wolverine` (Wolverine creates its tables) |

Both process roles are created NOLOGIN by the migration `AccessRoles`; `Shipment.DbMigrator finish` gives them LOGIN
and a password, so no credential is in the repository.

## Schema

The EF model uses one schema per module with snake_case tables and columns, so the demo data
(`apps/Shipment.DbMigrator/Seed/*.sql`) is plain SQL. Enums are stored as snake_case names (`courier_assigned`),
sizes as `S`/`M`/`L`/`XL`, money as bigint grosze, coordinates `numeric(9,6)`, `xmin` as the row version. Codes come
from sequences (`ORD-104001`, `SHP-204001`). The ledger (`billing.entries`) is append-only: a trigger refuses UPDATE
and DELETE, and partial unique indexes keep one entry of a kind per order (one refund per missing line). CHECK constraints
(`CheckConstraints` migration, `Mapping.Check`) hold the enum values, ranges and state consistency.

New column or table: change the entity and its configuration, `dotnet ef migrations add <Name> --project
libs/Shipment.Persistence --output-dir Migrations`, review the generated SQL, `./deploy/deploy.sh --db-only`.

## Refusals

Messages are English sentences for people, written once in the domain or the service and shown as they are. The kind
decides the status; there are no numeric codes.

## Map

`Components/Ui/Map.razor` + `wwwroot/js/map.js`: Leaflet 1.9.4 vendored in `wwwroot/lib/leaflet` (`npm run
vendor:leaflet`), OpenStreetMap tiles with attribution, circle markers by kind (pickup, customer, courier, picked,
muted), labels as text nodes, a picker for forms (click → coordinates). In the dark theme the tiles are inverted by
CSS. Distances are haversine (`Shipment.Domain.Common.Distance`, `Shipment.Geo.GeoLine`).
