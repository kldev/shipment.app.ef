#!/usr/bin/env bash
# JetBrains code inspection (the same engine as Rider) over the whole solution. Fails when it reports anything
# at SUGGESTION level or above - fix the code; switch a rule off only in Shipment.slnx.DotSettings, with a reason.
#
# Requires the global tool: dotnet tool install -g JetBrains.ReSharper.GlobalTools
#   ./scripts/inspect-code.sh             build + inspect
#   ./scripts/inspect-code.sh --no-build  inspect the last build (hooks run it after their own build)
set -euo pipefail

cd "$(dirname "$0")/.."

if ! command -v jb >/dev/null 2>&1; then
  echo "jb not found - install: dotnet tool install -g JetBrains.ReSharper.GlobalTools" >&2
  echo "(and make sure ~/.dotnet/tools is on PATH)" >&2
  exit 1
fi

if [[ "${1:-}" != "--no-build" ]]; then
  dotnet build Shipment.slnx --nologo -v q
fi

report="$(mktemp)"
trap 'rm -f "$report"' EXIT

# Generated code (EF Core migrations, Lucide icons) and the shared libraries copied from other projects -
# observability, monitoring, the RabbitMQ management client and e-mails (Ambit, libs/README.md).
# They are inspected where they change first; here it would only report the parts Shipment does not use (yet).
excluded="apps/Shipment.Api/Components/Ui/LucideIcons.g.cs;libs/Shipment.Persistence/Migrations/**"
excluded+=";libs/Shipment.Observability/**;libs/Shipment.Observability.AspNetCore/**;libs/Shipment.Monitoring/**"
excluded+=";libs/Shipment.RabbitMq.Management/**;libs/Shipment.Emails/**"

jb inspectcode Shipment.slnx --no-build --format=Text --severity=SUGGESTION --exclude="$excluded" \
  --output="$report" >/dev/null

issues="$(grep -cE '\.(cs|razor):[0-9]+ ' "$report" || true)"
if (( issues > 0 )); then
  cat "$report"
  echo
  echo "$issues code inspection issue(s) - fix them (Rider: Code | Inspect Code shows the same list)." >&2
  exit 1
fi

echo "Code inspection: no issues."
