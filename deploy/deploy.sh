#!/usr/bin/env bash
# Deploys Shipment on the shared local infrastructure: the Docker Swarm stack "hr-local" (overlay network
# lnet, gateway on 127.0.0.1:5432/5672/1025). Shipment has no service containers of its own (ADR 0003):
#   PostgreSQL  apps/Shipment.DbMigrator prepare (role shipment_migrator: LOGIN, CREATEROLE - not a superuser;
#               database shipment owned by it), migrate --seed (the EF Core migrations of libs/Shipment.Persistence as
#               shipment_migrator, the Kraków demo into an empty database), finish (LOGIN for shipment_api and
#               shipment_runner - the roles come from the migration AccessRoles; the accounts' password)
#   RabbitMQ    vhost shipment, user shipment
#   Shipment    image shipment:dev, container shipment on lnet, http://127.0.0.1:5400 (sessions' keys on a volume)
#   Runner      image shipment-runner:dev, container shipment-runner on lnet (outbox -> RabbitMQ -> module handlers)
# The same connection string and keys go to the user-secrets of Shipment.Api, so `dotnet run` works after it too.
# Idempotent: running it again applies only new migrations and recreates the containers (data stays).
# psql and rabbitmqctl run through `docker exec` (local socket, the node's own CLI): no infrastructure password here.
# Local only: Shipment is a learning project, never deployed to a cluster in 2026 (PRODUCT.md).
#
#   ./deploy/deploy.sh                 everything
#   ./deploy/deploy.sh --db-only       database and migrations only (after a new migration, with `dotnet run`)
set -euo pipefail
# shellcheck source=deploy/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

check_stack

if [[ -z "${SHIPMENT_PASSWORD:-}" || "$SHIPMENT_PASSWORD" == "<"*">" ]]; then
    echo "No password: copy .env.sample to .env (SHIPMENT_PASSWORD, ADR 0011)." >&2
    exit 1
fi

# The superuser's part around the migrations: apps/Shipment.DbMigrator (the same code the tests run). The stack's superuser and
# its password come from the postgres task's environment and go in as variables, never as arguments.
pg_task=$(task_container postgres)
superuser=$(docker exec "$pg_task" printenv POSTGRES_USER)
superuser_password=$(docker exec "$pg_task" printenv POSTGRES_PASSWORD)
export ConnectionStrings__postgres="Host=localhost;Port=5432;Database=postgres;Username=$superuser;Password=$superuser_password"
export Database__Name="$DB" Database__Migrator="$MIGRATOR" Database__MigratorPassword="$MIGRATOR_PASSWORD"
export Logins__${API_ROLE}="$API_PASSWORD" Logins__${RUNNER_ROLE}="$RUNNER_PASSWORD"
dotnet build -v q -m:2 --nologo "$REPO_DIR/apps/Shipment.DbMigrator" >/dev/null
migrator() { dotnet "$REPO_DIR/apps/Shipment.DbMigrator/bin/Debug/net10.0/Shipment.DbMigrator.dll" "$@"; }
migrator prepare

migrator migrate --seed
# The roles exist only after the first migration (AccessRoles creates them NOLOGIN): LOGIN and passwords.
Accounts__Password="$SHIPMENT_PASSWORD" migrator finish
unset ConnectionStrings__postgres superuser_password
secret_set "ConnectionStrings:shipment" \
    "Host=localhost;Port=5432;Database=$DB;Username=$API_ROLE;Password=$API_PASSWORD"
echo "PostgreSQL: database $DB up to date, $API_ROLE can sign in."
[[ "${1:-}" == "--db-only" ]] && exit 0

mq list_vhosts --no-table-headers name | grep -qx "$VHOST" || mq add_vhost "$VHOST" >/dev/null
if mq list_users --no-table-headers | awk '{print $1}' | grep -qx "$MQ_USER"; then
    mq change_password "$MQ_USER" "$MQ_PASSWORD" >/dev/null
else
    mq add_user "$MQ_USER" "$MQ_PASSWORD" >/dev/null
fi
mq set_permissions -p "$VHOST" "$MQ_USER" '.*' '.*' '.*' >/dev/null
echo "RabbitMQ: vhost $VHOST, user $MQ_USER."

echo "Building $IMAGE (one build at a time - the laptop has 16 GB)..."
docker build -q -t "$IMAGE" -f "$REPO_DIR/Dockerfile" "$REPO_DIR" >/dev/null
docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
# Secrets go in as `-e NAME` (the value from this process's environment), never as an argument on the command line.
export ConnectionStrings__shipment="Host=postgres;Port=5432;Database=$DB;Username=$API_ROLE;Password=$API_PASSWORD"
export RabbitMq__Password="$MQ_PASSWORD"
docker run -d --name "$CONTAINER" --restart unless-stopped --network "$NETWORK" --memory 512m \
    -p "127.0.0.1:$PORT:8080" -v "$KEYS_VOLUME:/keys" \
    -e ConnectionStrings__shipment \
    -e DataProtection__KeysPath=/keys \
    -e DemoSignIn__Enabled=true \
    "$IMAGE" >/dev/null

for _ in $(seq 1 60); do
    state=$(docker inspect -f '{{.State.Health.Status}}' "$CONTAINER" 2>/dev/null || echo missing)
    [[ "$state" == healthy ]] && break
    sleep 2
done
[[ "$state" == healthy ]] || { echo "$CONTAINER is $state - docker logs $CONTAINER" >&2; exit 1; }
echo "Shipment: http://127.0.0.1:$PORT  (API reference: /scalar/, OpenAPI: /openapi/v1.json, readiness: /health/ready)"
curl -fsS "http://127.0.0.1:$PORT/health/ready" || true
echo

echo "Building $RUNNER_IMAGE..."
docker build -q -t "$RUNNER_IMAGE" -f "$REPO_DIR/Dockerfile.runner" "$REPO_DIR" >/dev/null
docker rm -f "$RUNNER_CONTAINER" >/dev/null 2>&1 || true
export ConnectionStrings__shipment="Host=postgres;Port=5432;Database=$DB;Username=$RUNNER_ROLE;Password=$RUNNER_PASSWORD"
docker run -d --name "$RUNNER_CONTAINER" --restart unless-stopped --network "$NETWORK" --memory 384m \
    -e ConnectionStrings__shipment \
    -e RabbitMq__Host=rabbitmq -e RabbitMq__UserName="$MQ_USER" -e RabbitMq__Password \
    "$RUNNER_IMAGE" >/dev/null
for _ in $(seq 1 60); do
    docker logs "$RUNNER_CONTAINER" 2>&1 | grep -q "Application started" && break
    [[ "$(docker inspect -f '{{.State.Running}}' "$RUNNER_CONTAINER" 2>/dev/null)" == true ]] || break
    sleep 2
done
docker logs "$RUNNER_CONTAINER" 2>&1 | grep -q "Application started" \
    || { echo "$RUNNER_CONTAINER did not start - docker logs $RUNNER_CONTAINER" >&2; exit 1; }
echo "Runner: $RUNNER_CONTAINER."
