# 0010. Query composition on the page

Status: accepted (2026-10-29)

## Context

A shipment's detail page (the CodeOpinion example: progress, location on the map, courier, customer, compliance)
needs data owned by several contexts.

## Decision

No query that joins every context. The page calls each context's application service by the shipment id (dispatch,
tracking, carriers, crm, compliance, billing) and composes the cards. Each card belongs to its context and can fail
alone.
