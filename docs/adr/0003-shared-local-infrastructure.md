# 0003. Shared local infrastructure, local only

Status: accepted (2026-10-29)

## Context

Shipment is a learning project. It will not run in production in 2026. The developer's machine already runs a shared
Docker Swarm stack `hr-local` (PostgreSQL 18, RabbitMQ, RustFS, MailPit) used by several projects.

## Decision

- No `docker compose` and no service containers of Shipment's own: database `shipment`, roles `shipment_*`, vhost
  `shipment` on `hr-local`; containers `shipment` (127.0.0.1:5400) and `shipment-runner` on network `lnet`.
- `deploy/deploy.sh` is the only deployment: `Shipment.DbMigrator prepare` (migrator `shipment_migrator`: CREATEROLE,
  never a superuser) → `migrate --seed` (the EF Core migrations and the demo data) → `finish` (LOGIN for the
  application roles, the accounts' password).
- No Kubernetes, Helm, Flux or image workflows.

## Revisit when

The project is meant to be shown outside the laptop.
