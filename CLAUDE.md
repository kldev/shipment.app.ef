# CLAUDE.md

Shipment - a delivery operator's back office like Glovo: a standard .NET 10 project on EF Core and PostgreSQL, Blazor
in one host, with Ambit's look.
What and why: `PRODUCT.md` (mirrors the SDD project "Shipment" in the DC wiki, http://127.0.0.1:9494/wiki/sdd);
the domain: `docs/domain.md`; how it is built: `docs/design.md`, `docs/adr/`. **Everything in English**: UI texts,
docs, ADRs, comments, SQL messages. Agent prompts for the specs: `.instructions/NNN-slug.md` (local, not committed).
Local only - no Kubernetes, no image workflows (ADR 0003).

## Commands

```bash
# once: cp .env.sample .env (SHIPMENT_PASSWORD - every account's password, ADR 0011)
./deploy/deploy.sh --db-only          # database + EF Core migrations + demo data on hr-local (after a new migration)
./deploy/deploy.sh                    # everything: database, roles, vhost, images, containers shipment (:5400) and shipment-runner
./deploy/remove.sh                    # frees port 5400 before dotnet run (the database stays)
dotnet run --project apps/Shipment.Api      # host on :5400 (user-secrets written by deploy.sh)
dotnet run --project apps/Shipment.Runner   # runner (appsettings.Development.json: hr-local through 127.0.0.1)
dotnet ef migrations add <Name> --project libs/Shipment.Persistence --output-dir Migrations   # after a model change

./verify <schema|platform|crm|...|web|all>   # a group per module (trait verify=<module>)
dotnet csharpier check .              # formatting (pre-commit)
./scripts/inspect-code.sh             # jb inspectcode, 0 findings (by hand, no pre-push hook)
```

Tests: xUnit v3 on Microsoft.Testing.Platform, Testcontainers (PostgreSQL 18; the schema from the migrations through
`Shipment.DbMigrator`, as in deploy.sh). The laptop has 16 GB: build once with `-m:2`, then `dotnet build-server
shutdown`, run only the touched group (`./verify <module>` or `-- --filter-class Shipment.Tests.<Class>`),
`DOTNET_gcServer=0`; never the whole suite.

## Architecture (ADR 0013)

- **Layers**: `libs/Shipment.Domain` (entities and their rules, `Result`/`Error`) → `libs/Shipment.Persistence`
  (`ShipmentDbContext`, configurations, migrations - the only source of the schema) → `libs/Shipment.Application`
  (one service per bounded context: commands through the domain, one `SaveChanges` with the outbox rows; queries as
  `*Row` records). Hosts are thin adapters: no rule in a page, an endpoint or a handler.
- **Bounded contexts = modules** (ADR 0008, `docs/domain.md`): a service reads its own tables and lower contexts' data;
  reactions upwards via the outbox (`Outbox.Enqueue`) → runner → the reacting service (ADR 0007).
- **One host** (ADR 0002): Blazor pages inject the services (no HTTP to itself); the REST API in `Endpoints/`
  (`IEndpointGroup` per context, routes in `ApiRoutes`, `/api/v1`, OpenAPI `/openapi/v1.json`) calls the same services.
  Detail pages compose cards per context (ADR 0010); ids for new aggregates come from the page
  (`Guid.CreateVersion7()`, ADR 0009). Refusals: `Error.Validation` 400, `NotFound` 404, `Conflict` 409; the page shows
  `error.Message`.
- **No tenants** (ADR 0004); everyone is an admin, sign-in with a username, e-mail for notifications (ADR 0006).
- **Look** (ADR 0005): Ambit's tokens on Tailwind v4 (`apps/Shipment.Api/Styles/app.css` → `wwwroot/css/app.css`,
  committed; `npm install` once in `apps/Shipment.Api`). Icons: add to `tools/generate-icons.py`, run it.
- **Database**: same schemas and snake_case columns as before (identity, crm, catalog, carriers, compliance, ordering,
  dispatch, tracking, billing, messaging, wolverine); enums as snake_case strings; `xmin` as the row version
  (`Saving.Expect`), `Saving.SaveAsync` turns races into 409; roles and grants in the migration `AccessRoles`; demo
  data in `apps/Shipment.DbMigrator/Seed/`.

## Conventions

- `TreatWarningsAsErrors`, central package versions (`Directory.Packages.props`), libraries by `ProjectReference`.
- Husky: pre-commit only = CSharpier + build. No pre-push hook - run `./scripts/inspect-code.sh` yourself before a push.
- Per module: tests for success, every validation, 409 version, 404; e2e in the browser with screenshots in
  `docs/screens/`; then `PRODUCT.md` state and the SDD spec's criteria with evidence.
