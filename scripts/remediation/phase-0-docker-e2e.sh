#!/usr/bin/env bash
set -euo pipefail

# Runs a disposable PostgreSQL-backed registry, uploads fabricated module data,
# then proves pg_dump/pg_restore preserve public-table counts and DbUp state.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
IMAGE="${IMAGE:-terraform-registry:local-audit}"
SUFFIX="$(date +%s)"
NETWORK="terraform-registry-e2e-net-$SUFFIX"
POSTGRES="terraform-registry-e2e-pg-$SUFFIX"
APP="terraform-registry-e2e-app-$SUFFIX"
EVIDENCE="${EVIDENCE_DIR:-$(mktemp -d "${TMPDIR:-/tmp}/terraform-registry-pg-e2e.XXXXXX")}" 

cleanup() {
  docker rm -f "$APP" "$POSTGRES" >/dev/null 2>&1 || true
  docker network rm "$NETWORK" >/dev/null 2>&1 || true
}
trap cleanup EXIT

if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
  docker build --tag "$IMAGE" "$ROOT"
fi

docker network create "$NETWORK" >/dev/null
docker run -d --name "$POSTGRES" --network "$NETWORK" \
  -e POSTGRES_USER=e2e -e POSTGRES_PASSWORD=e2e-password -e POSTGRES_DB=registry \
  postgres:18 >/dev/null
for _ in $(seq 1 30); do
  docker exec "$POSTGRES" pg_isready -U e2e -d registry >/dev/null 2>&1 && break
  sleep 1
done
docker exec "$POSTGRES" pg_isready -U e2e -d registry >/dev/null

docker run -d --name "$APP" --network "$NETWORK" -p 127.0.0.1::5131 \
  -e TF_REG_AuthorizationToken=e2e-test-token \
  -e TF_REG_Oidc__JwtSecretKey='e2e-disposable-jwt-secret-key-0123456789' \
  -e TF_REG_DatabaseProvider=postgres \
  -e "TF_REG_PostgreSQL__ConnectionString=Host=$POSTGRES;Port=5432;Database=registry;Username=e2e;Password=e2e-password" \
  "$IMAGE" >/dev/null
for _ in $(seq 1 45); do
  port="$(docker port "$APP" 5131/tcp 2>/dev/null | sed 's/.*://' || true)"
  [[ -n "$port" ]] && curl --fail --silent "http://127.0.0.1:$port/health" >/dev/null && break || true
  sleep 1
done
port="$(docker port "$APP" 5131/tcp | sed 's/.*://')"
base="http://127.0.0.1:$port"
auth='Authorization: Bearer e2e-test-token'
test "$(curl --silent --output /dev/null --write-out '%{http_code}' -H "$auth" "$base/ready")" = 200
test "$(curl --silent --output /dev/null --write-out '%{http_code}' -H "$auth" \
  -F "moduleFile=@$ROOT/TerraformRegistry.Tests/TestData/test-module.zip;type=application/gzip" \
  "$base/v1/modules/e2epg/sample/aws/1.0.0")" = 201

mkdir -p "$EVIDENCE"
docker exec "$POSTGRES" pg_dump -U e2e -d registry --format=custom >"$EVIDENCE/registry.dump"
docker cp "$EVIDENCE/registry.dump" "$POSTGRES:/tmp/registry.dump"
docker exec "$POSTGRES" createdb -U e2e registry_restore
docker exec "$POSTGRES" pg_restore -U e2e -d registry_restore --clean --if-exists --no-owner /tmp/registry.dump
counts() {
  docker exec -i "$POSTGRES" psql -U e2e -d "$1" -X -At <<'SQL'
SELECT format('SELECT %L || ''|'' || count(*) FROM %I.%I;', schemaname || '.' || tablename, schemaname, tablename)
FROM pg_catalog.pg_tables WHERE schemaname = 'public' ORDER BY tablename;
\gexec
SQL
}
counts registry >"$EVIDENCE/source-counts.txt"
counts registry_restore >"$EVIDENCE/restored-counts.txt"
diff -u "$EVIDENCE/source-counts.txt" "$EVIDENCE/restored-counts.txt"
docker exec "$POSTGRES" psql -U e2e -d registry -X -Atc 'SELECT ScriptName FROM SchemaVersions ORDER BY ScriptName' >"$EVIDENCE/source-journal.txt"
docker exec "$POSTGRES" psql -U e2e -d registry_restore -X -Atc 'SELECT ScriptName FROM SchemaVersions ORDER BY ScriptName' >"$EVIDENCE/restored-journal.txt"
diff -u "$EVIDENCE/source-journal.txt" "$EVIDENCE/restored-journal.txt"
printf 'PostgreSQL Docker E2E passed. Evidence: %s\n' "$EVIDENCE"
