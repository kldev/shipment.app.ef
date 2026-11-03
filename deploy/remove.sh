#!/usr/bin/env bash
# Stops Shipment's container; the database, the vhost and the files stay.
#   ./deploy/remove.sh            container only
#   ./deploy/remove.sh --purge    also the database shipment, the roles shipment_*, the vhost and user shipment, the images,
#                                 the sessions' keys and the user-secrets (asks first)
set -euo pipefail
# shellcheck source=deploy/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

docker rm -f "$CONTAINER" >/dev/null 2>&1 && echo "Container $CONTAINER removed." || echo "No container $CONTAINER."
docker rm -f "$RUNNER_CONTAINER" >/dev/null 2>&1 && echo "Container $RUNNER_CONTAINER removed." || true
[[ "${1:-}" == "--purge" ]] || exit 0

read -r -p "Drop the database $DB, roles shipment_*, vhost $VHOST, images and keys? [y/N] " answer
[[ "$answer" == [yY] ]] || exit 0

check_stack
pg -d postgres -v db="$DB" <<'SQL'
SELECT format('DROP DATABASE IF EXISTS %I WITH (FORCE)', :'db') \gexec
SELECT format('DROP ROLE IF EXISTS %I', rolname) FROM pg_roles WHERE rolname LIKE 'shipment\_%' \gexec
SQL
mq delete_vhost "$VHOST" >/dev/null 2>&1 || true
mq delete_user "$MQ_USER" >/dev/null 2>&1 || true
docker volume rm "$KEYS_VOLUME" >/dev/null 2>&1 || true
docker image rm "$IMAGE" "$RUNNER_IMAGE" >/dev/null 2>&1 || true
dotnet user-secrets clear --project "$API_PROJECT" >/dev/null
echo "Purged."
