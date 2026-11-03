<div align="center">

<img src="apps/Shipment.Api/wwwroot/icon/favicon.png" width="72" alt="Shipment" />

# Shipment

**The back office of a delivery operator, working like Glovo.**
Partners hand orders over at pickup points, the store picks and packs them (missing stock is refunded, never charged),
a courier on a bike, in a car or in a van takes them to the door along a real OpenStreetMap route, and an append-only
ledger settles the charge, the commission, the payout and the tip.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Blazor Server](https://img.shields.io/badge/Blazor-Server-5C2D91?logo=blazor&logoColor=white)
![EF Core 10](https://img.shields.io/badge/EF%20Core-10-6C3FC5)
![PostgreSQL 18](https://img.shields.io/badge/PostgreSQL-18-4169E1?logo=postgresql&logoColor=white)
![RabbitMQ + Wolverine](https://img.shields.io/badge/RabbitMQ-Wolverine-FF6600?logo=rabbitmq&logoColor=white)
![Leaflet + OSM](https://img.shields.io/badge/Leaflet-OpenStreetMap-199900?logo=leaflet&logoColor=white)
![Tailwind CSS v4](https://img.shields.io/badge/Tailwind%20CSS-v4-06B6D4?logo=tailwindcss&logoColor=white)
![Tests](https://img.shields.io/badge/tests-xUnit%20v3%20%2B%20Testcontainers-2EA44F)
![License MIT](https://img.shields.io/badge/license-MIT-blue)

<br />

<img src="docs/screens/15-live-tracking.jpg" width="90%" alt="Live tracking: every shipment on its way, with routes, couriers and ETA on the map of Kraków" />

<sub>Live tracking of the seeded Kraków demo: routes from OSRM, the path driven, the courier's position, ETA and
on time / late - real screens, all of them in <a href="docs/screens/"><code>docs/screens/</code></a>.</sub>

</div>

---

## Why it is interesting

- **DDD bounded contexts without microservices.** Nine contexts (`crm`, `catalog`, `carriers`, `compliance`,
  `ordering`, `dispatch`, `tracking`, `billing`, `notifications`) are modules of one process and one database, each
  with its own schema, domain and application service, after CodeOpinion's DDD examples (ADR 0008).
- **Dependencies point one way.** A context reads only itself and the contexts below it. When a lower one must react
  to a higher one, the change goes through an **integration event**: an outbox row written in the same `SaveChanges`,
  then a Wolverine runner on RabbitMQ (ADR 0007).
- **Rules live in the domain.** Every write goes domain entity → application service → one `SaveChanges`. A refusal is
  a `Result`/`Error` that maps to 400 / 404 / 409. The Blazor pages and the `/api/v1` Minimal API call the same
  services (ADR 0002, 0013).
- **Money you can audit.** `billing.entries` is append-only: a trigger refuses UPDATE and DELETE, and partial unique
  indexes allow one entry of a kind per order. Handlers are idempotent per event id, so a redelivered message never
  books twice.
- **Real-world edge cases.** Stock that runs out at picking (partial fulfilment with a refund per line), 18+ products
  that need an ID check at handover, a vehicle that must fit the order's size and weight, courier documents that expire.
- **A real map.** Leaflet and OpenStreetMap, routes from OSRM per vehicle profile, fetched by the runner and stored as
  GeoJSON. No PostGIS: distances are haversine in SQL.
- **Correct under concurrency.** PostgreSQL `xmin` is the row version. A stale write, or a race EF catches at save, is
  a 409, never a lost update.
- **Least-privilege database.** The migrator, the host and the runner each have their own role, defined in an EF
  migration, and no credential is in the repository (ADR 0011).

## Screenshots

| | |
|---|---|
| <img src="docs/screens/01-dashboard-city-map.jpg" alt="Dashboard with the city map" /> | <img src="docs/screens/03-partner-detail-pickup-points-and-catalog.jpg" alt="Partner detail" /> |
| **Dashboard**: the city at a glance, with pickup points, customers and couriers on the map. | **Partner**: pickup points with opening hours, the catalog with sizes, weights and 18+ flags. |
| <img src="docs/screens/08-new-order-summary.jpg" alt="New order wizard" /> | <img src="docs/screens/09-picking-with-missing-lines.jpg" alt="Picking with missing lines" /> |
| **New order**: a six-step wizard. Prices and the address are copied, and the fee is set by distance bands. | **Picking**: on a tablet-sized screen, each line is packed or missing. A missing line is never charged. |
| <img src="docs/screens/11-assign-courier-proposals.jpg" alt="Courier proposals" /> | <img src="docs/screens/12-shipment-id-check-refused.jpg" alt="ID check refused" /> |
| **Dispatch**: proposals of couriers who are available, cleared, have a vehicle big enough, sorted nearest first. | **Handover**: an 18+ order needs the ID check. A refusal fails the shipment and refunds the order. |
| <img src="docs/screens/16-tracking-to-customer.jpg" alt="Tracking to the customer" /> | <img src="docs/screens/18-billing.jpg" alt="Billing" /> |
| **Tracking**: both legs of the route, the path driven, distance left and ETA. | **Billing**: the day's charges, refunds, commissions, payouts and tips, per partner and per courier. |
| <img src="docs/screens/05-customer-addresses-dark.jpg" alt="Dark theme" /> | <img src="docs/screens/07-compliance.jpg" alt="Compliance" /> |
| **Dark theme**: Ambit's design tokens on Tailwind CSS v4, with the map tiles inverted by CSS (ADR 0005). | **Compliance**: courier documents with verification and expiry, and clearance with its reasons. |

## Architecture

```mermaid
flowchart TB
    admin["👩‍💼 Operator admin<br/>(plays store, dispatch, courier)"]
    client["🔌 API client<br/>/api/v1 · OpenAPI · Scalar"]

    subgraph host["Shipment.Api (one host)"]
        direction LR
        ui["Blazor interactive server<br/>+ Leaflet map (JS interop)"]
        api["Minimal API<br/>IEndpointGroup per context"]
    end

    subgraph app["Shipment.Application → Shipment.Domain"]
        direction LR
        svc["one service per bounded context<br/>commands through the domain · *Row queries"]
    end

    db[("PostgreSQL 18<br/>a schema per context<br/>+ messaging.outbox_messages")]

    subgraph runner["Shipment.Runner"]
        direction LR
        relay["outbox relay"] --> mq{{"RabbitMQ<br/>Wolverine"}} --> handlers["handlers over the same services<br/>dispatch · ordering · billing · tracking"]
    end

    osrm["🗺️ OSRM<br/>bike / car routes"]

    admin --> ui
    client --> api
    ui & api --> svc
    svc -- "EF Core 10 · one SaveChanges<br/>xmin version · outbox row in the same transaction" --> db
    db -- "committed messages" --> relay
    handlers --> svc
    handlers -. "courier-assigned" .-> osrm

    classDef proc fill:#eef4ff,stroke:#1e4f91,color:#0b1f3a
    class ui,api,svc,relay,handlers proc
```

The Blazor pages inject the services and make no HTTP calls to their own host. The REST API is a thin adapter over the
same calls, so a rule is written once.

### Bounded contexts and who reads whom

```mermaid
flowchart LR
    crm["<b>crm</b><br/>partners · pickup points<br/>customers · addresses"]
    catalog["<b>catalog</b><br/>products · prices<br/>size · weight · 18+"]
    carriers["<b>carriers</b><br/>couriers · vehicles<br/>shifts · last position"]
    compliance["<b>compliance</b><br/>documents · expiry<br/>what a vehicle may carry"]
    ordering["<b>ordering</b><br/>orders · lines<br/>picking"]
    dispatch["<b>dispatch</b><br/>shipments · assignment<br/>lifecycle"]
    tracking["<b>tracking</b><br/>OSRM routes · pings<br/>ETA · timeline"]
    billing["<b>billing</b><br/>append-only ledger<br/>commission · payout · tips"]

    catalog --> crm
    compliance --> carriers & catalog
    ordering --> crm & catalog
    dispatch --> ordering & carriers & compliance
    tracking --> dispatch & carriers & crm
    billing --> ordering & dispatch

    classDef ctx fill:#eef4ff,stroke:#1e4f91,color:#0b1f3a
    classDef money fill:#effaf3,stroke:#1f7a3f,color:#0b2a17
    class crm,catalog,carriers,compliance,ordering,dispatch,tracking ctx
    class billing money
```

An arrow means "reads the data of". The way back, from a higher context to a lower one, goes only through
integration events.

### An order, from the basket to the doorstep

```mermaid
stateDiagram-v2
    direction LR
    [*] --> Placed: placed for a customer
    Placed --> Picking: the store starts picking
    Picking --> Ready: picked (all or some lines)
    Picking --> Cancelled: nothing available
    Ready --> Available: runner creates the shipment
    Available --> CourierAssigned: admin picks a proposal
    CourierAssigned --> ArrivedAtPickup
    ArrivedAtPickup --> PickedUp
    PickedUp --> ArrivedAtCustomer
    ArrivedAtCustomer --> Delivered
    ArrivedAtCustomer --> Failed: customer absent<br/>or ID check refused
    Delivered --> [*]
    Cancelled --> [*]
    Failed --> [*]
    note right of Available
        From here on the status belongs to dispatch.
        Ordering follows it through the runner.
    end note
```

### Missing stock: picking, the outbox and the refund

```mermaid
sequenceDiagram
    autonumber
    actor S as Store (picker)
    participant O as OrderingService
    participant DB as PostgreSQL
    participant R as Runner (Wolverine)
    participant B as BillingService
    participant D as DispatchService

    S->>O: FinishPicking(order, packed / missing per line)
    O->>DB: picked quantities + outbox "order-picked"<br/>(one transaction)
    R->>DB: relay committed messages
    par billing
        R->>B: order-picked (event id)
        B->>DB: capture packed goods + fee + tip<br/>refund per missing line (idempotent)
    and dispatch
        R->>D: order-picked (event id)
        alt something is left
            D->>DB: shipment with the smallest vehicle that fits
        else nothing is left
            D->>DB: order cancelled → full refund
        end
    end
```

### Who gets what: the ledger

```mermaid
flowchart LR
    subgraph placed["order placed"]
        auth["authorization"]
    end
    subgraph picked["order picked"]
        goods["capture goods<br/>(packed only)"]
        fee["capture fee<br/>(distance band)"]
        tipc["capture tip<br/>0 / 5 / 10 %"]
        refund["refund<br/>per missing line"]
    end
    subgraph delivered["shipment delivered"]
        commission["partner commission<br/>(packed goods)"]
        payout["courier payout<br/>base + per km by vehicle"]
        tip["tip → courier<br/>(never the margin)"]
    end
    auth --> goods & fee & tipc
    goods -.-> refund
    goods --> commission
    picked ~~~ delivered
    tipc --> tip

    classDef money fill:#effaf3,stroke:#1f7a3f,color:#0b2a17
    class auth,goods,fee,tipc,refund,commission,payout,tip money
```

A cancelled or failed order gets a full refund. Every entry is booked once per event id and is never updated.

## Stack

| Area | Choice |
|---|---|
| Host & UI | .NET 10, ASP.NET Core, Blazor interactive server, Tailwind CSS v4 on Ambit's tokens (output committed), Lucide icons, light/dark |
| API | Minimal API `/api/v1`, `IEndpointGroup` per context, OpenAPI `/openapi/v1.json`, Scalar, ProblemDetails |
| Data | EF Core 10 + Npgsql, PostgreSQL 18, a schema per context, snake_case, enums as strings, `xmin` concurrency, CHECK constraints, roles in a migration |
| Messaging | Transactional outbox → `Shipment.Runner` on Wolverine + RabbitMQ, idempotent handlers |
| Map | Leaflet 1.9.4 (vendored) + OpenStreetMap tiles, OSRM routes, haversine distances, GeoJSON in `jsonb` |
| Observability | Serilog, OpenTelemetry (ASP.NET Core, HTTP, Npgsql), `/health` |
| Quality | xUnit v3 + Testcontainers on PostgreSQL 18 (schema from the migrations), CSharpier, JetBrains InspectCode (0 findings), Husky.Net, `TreatWarningsAsErrors` |

## Repository

```text
apps/Shipment.Api/          the host: Blazor (Components/), REST API (Endpoints/), /auth, OpenAPI + Scalar, health
apps/Shipment.Runner/       outbox relay, Wolverine on RabbitMQ, handlers over the application services
apps/Shipment.DbMigrator/   database, roles, EF Core migrations, Kraków demo seed
libs/Shipment.Domain/       entities and rules per bounded context, Result/Error
libs/Shipment.Persistence/  ShipmentDbContext, configurations, Migrations/ (the only source of the schema)
libs/Shipment.Application/  one service per bounded context: commands and *Row queries
libs/                       Geo, Messaging.Contracts, Observability, Emails
tests/Shipment.Tests/       service and HTTP tests on a real database (./verify <group>)
```

## Documentation

| Document | What is in it |
|---|---|
| [`PRODUCT.md`](PRODUCT.md) | goals with "Done when", non-goals, decisions, state |
| [`docs/domain.md`](docs/domain.md) | bounded contexts, lifecycle, missing stock, integration events, courier choice, map |
| [`docs/design.md`](docs/design.md) | repository, request flow, database roles, schema, refusals |
| [`docs/adr/`](docs/adr/) | decisions 0002-0013 (0013: EF Core and application services) |
| [`CLAUDE.md`](CLAUDE.md) | commands and rules for working in the repository |

## Run locally

Needs the .NET SDK 10, Docker and the shared Swarm stack `hr-local` (PostgreSQL, RabbitMQ and MailPit on the network
`lnet`, a gateway on 127.0.0.1). For local use only: no production in 2026 (ADR 0003).

```bash
cp .env.sample .env              # SHIPMENT_PASSWORD - the password of every account
./deploy/deploy.sh               # database, roles, vhost, images; http://127.0.0.1:5400 and shipment-runner
./deploy/deploy.sh --db-only     # database and migrations only (with dotnet run)
./deploy/remove.sh               # stop the containers (--purge: database, roles, vhost too)
dotnet run --project apps/Shipment.Api
dotnet run --project apps/Shipment.Runner
```

Sign in as `k.lopuch` with the password from `.env`, or use "Sign in as the default admin" (demo only). The seed is
Kraków: 6 partners, 8 pickup points, 98 products, 12 customers, 10 couriers with documents.

## Tests

```bash
./verify <schema|platform|crm|catalog|carriers|compliance|ordering|dispatch|tracking|billing|web>
                                 # one group; needs Docker (PostgreSQL 18 in a container, schema from the migrations)
dotnet csharpier check .
./scripts/inspect-code.sh        # JetBrains InspectCode, 0 findings
```

Each context is tested on the real database for success, every validation, 409 (stale version) and 404.

## Migrations

```bash
dotnet ef migrations add <Name> --project libs/Shipment.Persistence --output-dir Migrations
./deploy/deploy.sh --db-only
```

## Status

Specs 001-006 are done: partners, the catalog, the map, couriers, compliance, orders and picking, dispatch, tracking,
billing and tips. Next is **007, e-mails** to customers (MailPit) for the `notifications` context.

## License

MIT - see [LICENSE](LICENSE).
