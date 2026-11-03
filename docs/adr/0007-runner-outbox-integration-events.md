# 0007. Runner, outbox and integration events between modules

Status: accepted (2026-10-29)

## Context

Bounded contexts are modules of one process and one database (ADR 0008). Some reactions cross them and are not part
of the person's request: billing a delivered order, refunding missing items, e-mailing the customer.

## Decision

- An application service adds an integration event with `db.Enqueue(message, now)` (`Outbox`) to the same `SaveChanges` as
  the change itself, as a row of `messaging.outbox_messages` (the payload carries ids, never secrets).
- `apps/Shipment.Runner` relays the outbox to Wolverine on RabbitMQ (queue `shipment-runner`, vhost `shipment`);
  a handler calls the application service of the context that reacts, idempotently (the event id is the key).
- E-mails go from the runner over SMTP to the stack's MailPit (`mailpit:1025`, inbox on 8025).
- Message types live in `libs/Shipment.Messaging.Contracts` (`OutboxMessageTypes`), added with the spec that needs
  them.

## Consequences

The reaction is eventually consistent (seconds); the page shows the state it reads.
