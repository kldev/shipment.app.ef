# 0006. Sign-in with a username; admins only

Status: accepted (2026-10-29)

## Decision

- `identity.users` has `username` (signs in, lower case) and `email` (notifications only - MailPit locally). The
  e-mail never signs in.
- One role for now: `admin`. The first admin is `k.lopuch` (seed `apps/Shipment.DbMigrator/Seed/1-dev-seed-first-admin.sql`).
  Couriers, partners and customers are records of the domain, not accounts.
- The password is checked in C# (`PlatformService.VerifyAsync`, ASP.NET Core `PasswordHasher`); the session is a
  cookie of 8 hours.

## Revisit when

Couriers or partners need their own screens (a courier app, a store's picking screen) - a role per audience then.
