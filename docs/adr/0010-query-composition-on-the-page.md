# 0010. Query composition on the page

Status: accepted (2026-10-29); since 0013 each card calls its context's application service instead of its `_app` view

## Context

A shipment's detail page (the CodeOpinion example: progress, location on the map, courier, customer, compliance)
needs data owned by several contexts.

## Decision

No view that joins every context. The page calls each module's own view by the shipment id (`dispatch_app`,
`tracking_app`, `carriers_app`, `crm_app`, `compliance_app`, `billing_app`) and composes the cards. Each card belongs
to its context and can fail alone.
