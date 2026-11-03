# 0008. Bounded contexts are modules of one process and one database - a modular monolith

Status: accepted (2026-10-29)

## Context

The design follows Derek Comartin (CodeOpinion): a shipment sits in the middle of several bounded contexts -
dispatch, tracking, carriers, billing, compliance, CRM - each owning its data. His diagrams show a database per
context. This project does not need services: one host, one runner, one PostgreSQL.

## Decision

- No microservices. A bounded context = a module: a schema `<module>` for its tables, the namespace
  `Shipment.Domain.<Module>` for its rules and `Shipment.Application.<Module>` for its service.
- A service writes only its own tables and reads its own and lower contexts' data (dependency order by the module
  number `nn`). Across contexts the link is an id (a foreign key to a primary key is allowed), never a shared table.
- What flows "upwards" (a lower module reacting to a higher one) goes as an integration event (ADR 0007).
- The modules and their order: `docs/domain.md`.

## Consequences

The boundaries inside one `DbContext` are a convention kept by review and the tests, not by database grants.
