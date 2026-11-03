# libs

Kod współdzielony przez procesy Rostry (host `apps/Shipment.Api`, runner `apps/Shipment.Runner`). Biblioteki trafiają do
projektów przez `ProjectReference`, bez NuGet. Poza `Shipment.Messaging.Contracts` i `Shipment.Languages` są **kopiami** z innych projektów, w
których zmieniono tylko prefiks: zmiana idzie najpierw w źródle, potem kopia tutaj. Inspekcja (`scripts/inspect-code.sh`) je pomija, bo są
sprawdzane w źródle.

| Projekt | Zawartość | Źródło |
|---|---|---|
| `Shipment.Domain/`, `Shipment.Persistence/`, `Shipment.Application/` | własne Shipment: reguły, EF Core z migracjami, serwisy kontekstów (ADR 0013) | — |
| `Shipment.Observability/` | Serilog, OpenTelemetry (OTLP tylko przy `OTEL_EXPORTER_OTLP_ENDPOINT`, Npgsql, HTTP), metryki health checków i limitów procesu | Ambit `libs/Ambit.Observability` |
| `Shipment.Observability.AspNetCore/` | to samo dla ASP.NET Core: instrumentacja żądań, jedna linia logu na żądanie | Ambit `libs/Ambit.Observability.AspNetCore` |
| `Shipment.Monitoring/` | monitor: Baza (cały serwer PostgreSQL, sampler), Kolejki (lista, podgląd, czyszczenie ze śladem), Pliki S3, Aplikacja (minuty pamięci, CPU, żądań); historię zapisuje host przez `IDatabaseSampleSink`, `IAppMetricsSink`, `IQueuePurgeLog` | Ambit `libs/Ambit.Monitoring` (= K-Wiki `KWiki.Monitoring`) |
| `Shipment.RabbitMq.Management/` | klient HTTP API zarządzania RabbitMQ (Kiota w `Generated/`, `./generate.sh`, nie edytować) | Ambit `libs/Ambit.RabbitMq.Management` |
| `Shipment.Messaging.Contracts/` | własna Rostry: wiadomości outboxu (`messaging.outbox_messages`), kolejka runnera, token linku resetu hasła (ADR 0007) | — |
| `Shipment.Languages/` | własna Rostry: języki (kody `pl`/`en`/`de`/`fr`/`nl`, kultury) i ustawienie instancji `Languages__Available` (ADR 0013) | — |
| `Shipment.Emails/` | wysyłka SMTP (MailKit), szablony maili w Liquid (Fluid) osadzone w bibliotece; **szablony są już treścią Rostry** (po polsku: `password-reset`, `header`, `footer`; instancja dla zagranicy, ADR 0013: `password-reset-{en,de,fr,nl}`, część angielska `password-reset-en-body*`, `header-intl`, `footer-intl`) — przy porównaniu z Ambit pomijaj `Templates/` | Ambit `libs/Ambit.Emails` |

Kopie z Ambit: `ambit.project.dev/libs` na commicie `611896a` (2026-10-03), zamiana `Ambit` → `Shipment`,
`ambit` → `shipment` (2026-10-28), potem `dotnet csharpier format` (prefiks zmienia kolejność `using`). Porównanie po zmianie w źródle: ta sama zamiana na nowej wersji, CSharpier i `diff -r`
(bez `bin/`, `obj/`) — pusty wynik = wyrównane.

Pakiety: wersje w `Directory.Packages.props` repozytorium (te same co w Ambit). MimeKit ciągnie podatny
`System.Security.Cryptography.Pkcs` 10.0.0 — przypięty tam wyżej (`CentralPackageTransitivePinningEnabled`).
