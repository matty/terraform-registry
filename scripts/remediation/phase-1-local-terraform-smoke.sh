#!/usr/bin/env bash
set -euo pipefail

# Proves that Terraform can install a fabricated module through the Local
# registry backend. The test uses an ephemeral TLS proxy because Terraform
# registry discovery requires HTTPS.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TERRAFORM_IMAGE="${TF_REGISTRY_TERRAFORM_IMAGE:-hashicorp/terraform:1.14.2@sha256:eee2f7d5725bfcfd734dfc9fe5a3df4b58b00eb8cc874993458108d8943265cf}"
SUFFIX="$(date +%s)"
PROJECT="p1local${SUFFIX}"
NETWORK="${PROJECT}_default"
CADDY="${PROJECT}-caddy"
SMOKE_VOLUME="${PROJECT}-terraform-smoke"
CADDY_CONFIG="${PROJECT}-caddy-config"
CADDY_DATA="${PROJECT}-caddy-data"
COMPOSE_OVERRIDE="$(mktemp)"

cat > "$COMPOSE_OVERRIDE" <<'EOF'
services:
  app:
    container_name: !reset null
    ports: !reset []
  postgres:
    ports: !reset []
EOF

cleanup() {
  docker rm -f "$CADDY" >/dev/null 2>&1 || true
  docker compose -f "$ROOT/docker-compose.dev.yml" -f "$COMPOSE_OVERRIDE" -p "$PROJECT" down -v >/dev/null 2>&1 || true
  docker volume rm "$SMOKE_VOLUME" "$CADDY_CONFIG" "$CADDY_DATA" >/dev/null 2>&1 || true
  rm -f "$COMPOSE_OVERRIDE"
}
trap cleanup EXIT

TF_REG_DevAuthBypass=true TF_REG_AdminEmails=dev@localhost \
  docker compose -f "$ROOT/docker-compose.dev.yml" -f "$COMPOSE_OVERRIDE" -p "$PROJECT" up -d --build app postgres

APP="$(docker compose -f "$ROOT/docker-compose.dev.yml" -f "$COMPOSE_OVERRIDE" -p "$PROJECT" ps -q app)"

for _ in $(seq 1 45); do
  if docker logs "$APP" 2>&1 | grep -q 'Application started'; then break; fi
  sleep 1
done
docker logs "$APP" 2>&1 | grep -q 'Application started'

docker volume create "$CADDY_CONFIG" >/dev/null
docker volume create "$CADDY_DATA" >/dev/null
docker volume create "$SMOKE_VOLUME" >/dev/null
docker run --rm -v "$CADDY_CONFIG:/config" alpine:3.21 sh -c \
  'printf "%s\n" "registry.local {" "  tls internal" "  reverse_proxy app:5131" "}" > /config/Caddyfile'
docker run -d --name "$CADDY" --network "$NETWORK" \
  -v "$CADDY_CONFIG:/etc/caddy" -v "$CADDY_DATA:/data" \
  caddy:2.10-alpine caddy run --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null

docker run --rm --network "$NETWORK" \
  -v "$ROOT/TerraformRegistry.Tests/TestData/test-module.zip:/fixture/module.zip:ro" \
  curlimages/curl:8.16.0 -fsS -o /dev/null -w '%{http_code}' \
  -H 'Authorization: Bearer MY_TOKEN_123' \
  -F 'moduleFile=@/fixture/module.zip;type=application/zip' \
  'http://app:5131/v1/modules/e2elocal/sample/aws/1.0.0' | grep -qx '201'

docker run --rm -v "$SMOKE_VOLUME:/work" alpine:3.21 sh -c \
  'printf "%s\n" "module \"sample\" {" "  source = \"registry.local/e2elocal/sample/aws\"" "  version = \"1.0.0\"" "}" > /work/main.tf; printf "%s\n" "credentials \"registry.local\" {" "  token = \"MY_TOKEN_123\"" "}" > /work/terraform.rc'

caddy_ip="$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$CADDY")"
docker run --rm --network "$NETWORK" --add-host "registry.local:$caddy_ip" \
  -e SSL_CERT_FILE=/certs/caddy/pki/authorities/local/root.crt \
  -e TF_CLI_CONFIG_FILE=/work/terraform.rc \
  -v "$CADDY_DATA:/certs:ro" -v "$SMOKE_VOLUME:/work" -w /work \
  "$TERRAFORM_IMAGE" init -input=false -no-color

printf 'Phase 1 Local Terraform smoke passed.\n'
