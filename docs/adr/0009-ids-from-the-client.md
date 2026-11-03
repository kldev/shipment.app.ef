# 0009. Ids come from the client

Status: accepted (2026-10-29)

## Context

CodeOpinion, "Stop letting your database generate IDs": the shipment id must exist before the first write so every
context (dispatch, tracking, billing…) can use the same id, and a retried request must not create a second shipment.

## Decision

Create commands take the id from the caller (the page makes it with `Guid.CreateVersion7()`). The same id with the
same content again is a no-op that returns the row; the same id with other content is `Error.Conflict` (409).
People see a short code next to it (e.g. `SHP-204001`), taken from a database sequence.
