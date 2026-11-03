# 0011. One shared password, kept outside the repository

Status: accepted (2026-10-29)

## Decision

Every account has the password from `SHIPMENT_PASSWORD` in `.env` (never committed; `.env.sample` shows it, default
`ambit909`). `Shipment.DbMigrator finish` hashes it into every account at each deployment; the tests use a random one.

## Revisit when

The project is used by anyone but its author - then a password per person and a reset by e-mail.
