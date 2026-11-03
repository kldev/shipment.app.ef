# 0004. One operator, no tenants

Status: accepted (2026-10-29)

## Context

Shipment models one delivery operator in a city (like Glovo in one market): its partners (stores, restaurants),
customers and couriers. There is no SaaS side - no organizations or companies to keep apart.

## Decision

No organization column, no tenant filters and no row-level security. Every service call runs as a signed-in, active
account (`ICurrentActor`); a row that is not there is `Error.NotFound` (404).

## Revisit when

A second operator (another city as a separate company) has to be kept apart.
