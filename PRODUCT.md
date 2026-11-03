# Shipment

A delivery operator's back office, working like Glovo: partners (stores, restaurants) hand orders over at pickup
points, the store picks and packs them, a courier on a bike, in a car or in a van takes them to the customer, and the
money is settled. A standard .NET 10 project (EF Core on PostgreSQL, the rules in C#, Blazor and a REST API in one
host - ADR 0013) with Ambit's look, following CodeOpinion's DDD examples - as a **modular monolith**, not services. The SDD project
"Shipment" in the local DC wiki is the source; this file mirrors it.

State: **specs 001-006 done (2026-10-29)** - partners, pickup points, customers, catalog, the OpenStreetMap map,
couriers with vehicles and shifts, compliance, orders and picking with missing stock, dispatch, routes and live
tracking, billing. The domain is planned in `docs/domain.md`; spec 007 next.

## What it proves

1. **An order goes from the basket to the doorstep and is settled.**
   Done when: an admin places an order for a customer, picks it for the store, assigns a courier with a fitting
   vehicle, follows the route on the map to "delivered", and billing shows charge, commission and payout.
   `./verify all`, screenshots in `docs/screens/`.
2. **Missing stock never costs the customer.**
   Done when: a line marked missing at picking is not charged, a refund is recorded and the customer gets an e-mail
   (MailPit) with what was delivered and what was refunded. `./verify ordering`, `./verify billing`
3. **Bounded contexts stay apart inside one process and one database.**
   Done when: each context has its own service and schema, reactions upwards go only through the outbox and the
   runner. `./verify schema`
4. **The rules live in C#, the API is designed.**
   Done when: every write goes through a domain entity and an application service, the pages and the REST API call
   the same services, and the schema comes from the EF Core migrations. `./verify schema`, `./verify web`

## Non-goals

Not ever:
- microservices or a database per context (ADR 0008);
- own service containers - only the shared `hr-local` stack (ADR 0003);
- an API generated instead of designed (ADR 0013).

Not yet:
- production of any kind in 2026, Kubernetes, CI image builds (ADR 0003);
- accounts for couriers, partners or customers - admins act for them (ADR 0006);
- a customer storefront, real payments, automatic courier assignment, batching several orders per courier;
- time zone per city, several cities, languages other than English.

## Decisions

| # | Decision | Why | Revisit when |
|---|---|---|---|
| D1 | Logic in C# on EF Core, a hand-written REST API (ADR 0013) | a standard .NET project | - |
| D2 | One host: REST API + Blazor on the same services (ADR 0002) | one deployment, no CORS | - |
| D3 | Local only, on `hr-local` (ADR 0003) | learning project, 16 GB laptop | shown outside the laptop |
| D4 | One operator, no tenants (ADR 0004) | one city's operator | a second operator |
| D5 | Ambit's look on Tailwind (ADR 0005) | the author's product family | - |
| D6 | Username sign-in, admins only, e-mail for notifications (ADR 0006) | the admin plays every role in the demo | couriers or partners sign in |
| D7 | Outbox → runner → the reacting service for reactions across contexts; e-mails to MailPit (ADR 0007) | a lower module never reads a higher one | - |
| D8 | Bounded contexts = modules of one database (ADR 0008) | CodeOpinion's boundaries without the services | - |
| D9 | Ids from the client (ADR 0009) | retries, one id across contexts | - |
| D10 | Query composition on the page (ADR 0010) | each card owned by its context | - |
| D11 | One shared password from `.env` (ADR 0011) | local learning project | anyone else uses it |
| D12 | Spec-driven development in the DC wiki + `.instructions/` (ADR 0012) | the author's method | - |

## State

Works today:
- sign-in with a username (`k.lopuch`), or "Sign in as the default admin" without a password (local demo only,
  `DemoSignIn__Enabled`, on in `deploy.sh`), 8 h session, shell in Ambit's look (light/dark), Users page;
- `platform` (`me`, `users`), the REST API `/api/v1` with OpenAPI and Scalar (ADR 0013);
- runner (outbox relay to RabbitMQ), `deploy/deploy.sh` on `hr-local`, tests on PostgreSQL 18 (`./verify`);
- **crm**: partners (kind, commission, status), pickup points (address, place on the map, hours, open now), customers
  with delivery addresses (one default); **catalog**: products with price, weight, size class, 18+ flag and "listed";
- **map**: Leaflet + OpenStreetMap on the dashboard (the city), partner and customer pages, a picker in the forms;
- **carriers**: couriers on a bike, in a car or a van (limits per vehicle type), shifts, position moved on the map;
  **compliance**: documents with verification and expiry, clearance with reasons, the 18+ check for products;
- **ordering**: orders placed for a customer (one partner, an open pickup point, prices and the address copied, the
  delivery fee by straight-line distance in bands), a six-step "New order" wizard, order detail with the timeline;
  picking on a tablet-sized screen - packed or missing per line, missing lines not charged, a missing product taken
  off the catalog at once; `order-placed`, `order-picked`, `order-cancelled` in the outbox (the runner logs them
  until 004/006/007 react);
- **dispatch**: the runner turns order-picked into a shipment with the smallest vehicle that carries it; proposals
  (available, cleared, big enough, nearest to the pickup point) with a map; assign makes the courier busy; the admin
  steps the shipment for the courier to delivered (18+ needs the ID check) or failed (ends the order); unassign only
  before pickup; ordering follows (delivered / failed) through the runner; shipment detail composed of each
  context's card;
- **tracking**: on courier-assigned the runner fetches both legs from OSRM (routing.openstreetmap.de, bike or car
  profile; straight lines marked approximate when it does not answer), with the promised time; the shipment page
  draws the legs, the path driven and the courier, with distance left, ETA and on time / late, refreshed by a timer;
  Live tracking shows every shipment on its way; a per-shipment simulator moves the demo's courier and stops at each
  stop for the status button;
- **billing**: an append-only ledger booked by the runner per event id - authorization at placing, capture of the
  packed goods and the fee and a refund per missing line at picking, a full refund when cancelled or failed, the
  partner's commission (packed goods only) and the courier's payout (base + per km by vehicle) at delivery; Billing
  page with today's (or any period's) cards, partners, couriers and days; a Billing card on the order and shipment;
- **tips**: the customer tips the courier 0, 5 or 10 % of the goods when paying (the last step of the New order
  wizard); charged with the order, refunded with it, all of it to the courier at delivery, never the margin;
- dev seed of Kraków: 6 partners, 8 pickup points, 98 products, 12 customers, 10 couriers with documents
  (`docs/screens/01-22`).

Next (specs, in order): 007 e-mails.
