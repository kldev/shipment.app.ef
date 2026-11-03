# libs

Code shared by Shipment's processes (the host `apps/Shipment.Api` and the runner `apps/Shipment.Runner`). The
libraries are added to projects by `ProjectReference`, not NuGet. Package versions are in `Directory.Packages.props`.

| Project | What is in it |
|---|---|
| `Shipment.Domain/` | entities and rules per bounded context, `Result`/`Error`, the operator's time, distances (ADR 0013) |
| `Shipment.Persistence/` | `ShipmentDbContext`, entity configurations, the EF Core migrations - the only source of the schema |
| `Shipment.Application/` | one service per bounded context: commands through the domain, queries as `*Row` records |
| `Shipment.Messaging.Contracts/` | outbox messages (`messaging.outbox_messages`) and the runner's queue (ADR 0007) |
| `Shipment.Geo/` | geometry helpers for routes on the map |
| `Shipment.Emails/` | SMTP sending (MailKit) and the customer's order e-mails as Liquid templates (Fluid), embedded |
| `Shipment.Observability/` | Serilog and OpenTelemetry (OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set), Npgsql and HTTP instrumentation |
| `Shipment.Observability.AspNetCore/` | the same for ASP.NET Core: request instrumentation, one log line per request |

`Shipment.Emails` and the two `Shipment.Observability*` projects are general-purpose infrastructure;
`scripts/inspect-code.sh` skips them. MimeKit pulls in a vulnerable `System.Security.Cryptography.Pkcs` 10.0.0, which
is pinned higher in `Directory.Packages.props` (`CentralPackageTransitivePinningEnabled`).
