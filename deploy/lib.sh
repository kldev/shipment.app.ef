#!/usr/bin/env bash
# Shared by deploy.sh and remove.sh. Sourced, never run.
# Local development logins only (as in hr-agency's shared-infra.sh) - the stack is reachable from 127.0.0.1 only.

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_PROJECT="$REPO_DIR/apps/Shipment.Api"

STACK="${SHARED_STACK:-hr-local}"
NETWORK="${SHARED_NETWORK:-lnet}"

# The developer's own settings (.env at the repository's root, never committed; .env.sample shows them): the
# accounts' password. Read as plain KEY=value lines, never executed.
if [[ -f "$REPO_DIR/.env" ]]; then
    SHIPMENT_PASSWORD="${SHIPMENT_PASSWORD:-$(sed -n 's/^SHIPMENT_PASSWORD=//p' "$REPO_DIR/.env" | tail -n 1)}"
fi

# SHIPMENT_DB: another database on the same server (e.g. to try a branch next to the main one).
DB="${SHIPMENT_DB:-shipment}"
MIGRATOR=shipment_migrator
MIGRATOR_PASSWORD=shipment_migrator
API_ROLE=shipment_api
API_PASSWORD=shipment_api
RUNNER_ROLE=shipment_runner
RUNNER_PASSWORD=shipment_runner
VHOST=shipment
MQ_USER=shipment
MQ_PASSWORD=shipment

IMAGE=shipment:dev
CONTAINER=shipment
RUNNER_IMAGE=shipment-runner:dev
RUNNER_CONTAINER=shipment-runner
PORT="${SHIPMENT_PORT:-5400}"
KEYS_VOLUME=shipment-keys

task_container() {
    docker ps -q --filter "label=com.docker.swarm.service.name=${STACK}_$1" --filter status=running | head -n1
}

check_stack() {
    local missing=() service
    docker network inspect "$NETWORK" >/dev/null 2>&1 || missing+=("network $NETWORK")
    for service in postgres rabbitmq; do
        [[ -n "$(task_container "$service")" ]] || missing+=("${STACK}_$service")
    done
    if ((${#missing[@]} > 0)); then
        echo "The shared infrastructure is not up:" >&2
        printf '  - %s\n' "${missing[@]}" >&2
        echo "Start it: cd ~/infa.swarm.tech && ./install.sh" >&2
        exit 1
    fi
}

pg() {
    local container user
    container=$(task_container postgres)
    user=$(docker exec "$container" printenv POSTGRES_USER)
    docker exec -i -e PGOPTIONS='-c client_min_messages=warning' "$container" psql -q -v ON_ERROR_STOP=1 -U "$user" "$@"
}

mq() { docker exec "$(task_container rabbitmq)" rabbitmqctl -q "$@"; }

secret_get() {
    dotnet user-secrets list --project "$API_PROJECT" 2>/dev/null | sed -n "s/^$1 = //p"
}

secret_set() { dotnet user-secrets set "$1" "$2" --project "$API_PROJECT" >/dev/null; }
