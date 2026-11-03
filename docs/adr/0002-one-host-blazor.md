# 0002. One host: the Blazor front and the REST API on the same services

Status: accepted (2026-10-29)

## Decision

`apps/Shipment.Api` serves the Blazor interactive server front, the REST API (`/api/v1`, OpenAPI at
`/openapi/v1.json`, Scalar at `/scalar/`), `/auth` and `/health`. Pages inject the application services and call them
in process; the endpoints are thin adapters over the same services (ADR 0013). One session cookie serves both.

## Consequences

One deployment, one port (5400), no CORS, no HTTP round trip from a page to its own host.
