#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LIFECYCLE="$ROOT/scripts/remediation/storage-emulators/storage-emulators.sh"
HOME_DIR="$HOME/.terraform-registry-storage-test"
PROVIDER="all"

usage() {
  printf 'Usage: %s [--provider azure|s3|all] [--home ABSOLUTE_PATH]\n' "$0" >&2
  exit 2
}

while (($#)); do
  case "$1" in
    --provider) PROVIDER="${2:-}"; shift ;;
    --home) HOME_DIR="${2:-}"; shift ;;
    *) usage ;;
  esac
  shift
done

[[ "$HOME_DIR" = /* ]] || usage
[[ "$PROVIDER" = azure || "$PROVIDER" = s3 || "$PROVIDER" = all ]] || usage

project_for() {
  printf 'tfregstorage%s' "$(printf '%s' "$1" | sha256sum | cut -c1-12)"
}

run_provider() {
  local storage_provider="$1"
  local project app caddy network fixture workspace caddy_ip module_namespace
  project="$(project_for "$HOME_DIR")"
  fixture="$(mktemp -d)"
  workspace="${HOME_DIR}/terraform-work-${storage_provider}"
  module_namespace="e2e${storage_provider}$(date +%s)"

  cleanup_fixture() {
    rm -rf "$fixture"
    if [[ -d "$workspace" ]]; then
      docker run --rm -v "$workspace:/workspace" alpine:3.21 sh -c \
        'rm -rf /workspace/* /workspace/.[!.]* /workspace/..?*'
      rmdir "$workspace"
    fi
  }
  trap cleanup_fixture RETURN

  "$LIFECYCLE" start --provider "$storage_provider" --home "$HOME_DIR"
  "$LIFECYCLE" status --home "$HOME_DIR"

  app="$(docker compose --project-name "$project" --project-directory "$HOME_DIR" -f "$HOME_DIR/compose.yaml" ps -q app)"
  for _ in $(seq 1 120); do
    if [[ "$(docker logs "$app" 2>&1)" == *"Application started"* ]]; then break; fi
    sleep 1
  done
  [[ "$(docker logs "$app" 2>&1)" == *"Application started"* ]]

  unzip -q "$ROOT/TerraformRegistry.Tests/TestData/test-module.zip" -d "$fixture/module"
  tar -C "$fixture/module" -czf "$fixture/test-module.tar.gz" .

  network="${project}_default"
  for archive in zip tar.gz; do
    local version archive_file content_type
    if [[ "$archive" = zip ]]; then
      version="1.0.0"
      archive_file="$ROOT/TerraformRegistry.Tests/TestData/test-module.zip"
      content_type='application/zip'
    else
      version="1.0.1"
      archive_file="$fixture/test-module.tar.gz"
      content_type='application/gzip'
    fi

    docker run --rm --network "$network" \
      -v "$archive_file:/fixture/module.${archive}:ro" \
      curlimages/curl:8.16.0 -fsS -o /dev/null -w '%{http_code}' \
      -H 'Authorization: Bearer MY_TOKEN_123' \
      -F "moduleFile=@/fixture/module.${archive};type=${content_type}" \
      "http://app:5131/v1/modules/${module_namespace}/sample/aws/${version}" | grep -qx '201'

    mkdir -p "$workspace/$archive"
    cat > "$workspace/$archive/main.tf" <<EOF
module "sample" {
  source  = "registry.local/${module_namespace}/sample/aws"
  version = "${version}"
}
EOF
    cat > "$workspace/$archive/terraform.rc" <<'EOF'
credentials "registry.local" {
  token = "MY_TOKEN_123"
}
EOF

    caddy="$(docker compose --project-name "$project" --project-directory "$HOME_DIR" -f "$HOME_DIR/compose.yaml" ps -q caddy)"
    caddy_ip="$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$caddy")"
    docker run --rm --network "$network" --add-host "registry.local:${caddy_ip}" \
      -e SSL_CERT_FILE=/certs/caddy/pki/authorities/local/root.crt \
      -e TF_CLI_CONFIG_FILE=/work/terraform.rc \
      -v "${project}_caddy-data:/certs:ro" -v "$workspace/$archive:/work" -w /work \
      hashicorp/terraform:1.14.2 init -input=false -no-color
  done

  printf 'Phase 1 %s emulator Terraform smoke passed.\n' "$storage_provider"
}

if [[ "$PROVIDER" = all ]]; then
  run_provider azure
  "$LIFECYCLE" clean --home "$HOME_DIR"
  run_provider s3
  "$LIFECYCLE" clean --home "$HOME_DIR"
else
  run_provider "$PROVIDER"
fi
