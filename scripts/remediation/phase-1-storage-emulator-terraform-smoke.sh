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

run_s3_provider_sidecar_contract() {
  local project="$1" home_dir="$2" network="$3" fixture="$4"
  local provider_namespace shasums_name signature_name
  provider_namespace="e2esidecar$(date +%s)"
  shasums_name='terraform-provider-example_1.0.0_SHA256SUMS'
  signature_name='terraform-provider-example_1.0.0_SHA256SUMS.sig'

  printf '%s  terraform-provider-example_1.0.0_linux_amd64.zip\n' \
    '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef' > "$fixture/$shasums_name"
  printf '%s\n' 'fabricated provider sidecar signature' > "$fixture/$signature_name"

  docker compose --project-name "$project" --project-directory "$home_dir" -f "$home_dir/compose.yaml" \
    exec -T postgres psql -U terraform_reg_user -d terraform_registry -v ON_ERROR_STOP=1 -c \
    "INSERT INTO users (id, email, provider, provider_id) VALUES ('dev-user-001', 'dev@localhost', 'emulator', 'dev-user-001') ON CONFLICT (id) DO NOTHING; INSERT INTO user_roles (user_id, role_id, assigned_by) SELECT 'dev-user-001', id, 'storage-emulator-contract' FROM roles WHERE name = 'admin' ON CONFLICT (user_id, role_id) DO NOTHING;"

  docker run --rm --network "$network" curlimages/curl:8.16.0 -fsS -o /dev/null -w '%{http_code}' \
    -H 'Content-Type: application/json' \
    --data "{\"namespace\":\"${provider_namespace}\",\"type\":\"example\",\"display_name\":\"Example\"}" \
    'http://app:5131/api/providers' | grep -qx '201'
  docker run --rm --network "$network" curlimages/curl:8.16.0 -fsS -o /dev/null -w '%{http_code}' \
    -H 'Content-Type: application/json' \
    --data '{"key_id":"test-key","ascii_armor":"-----BEGIN PGP PUBLIC KEY BLOCK-----\\n\\nmock\\n-----END PGP PUBLIC KEY BLOCK-----","source":"emulator"}' \
    "http://app:5131/api/providers/${provider_namespace}/example/gpg-keys" | grep -qx '201'
  docker run --rm --network "$network" curlimages/curl:8.16.0 -fsS -o /dev/null -w '%{http_code}' \
    -H 'Content-Type: application/json' \
    --data '{"version":"1.0.0","protocols":["5.0"],"key_id":"test-key"}' \
    "http://app:5131/api/providers/${provider_namespace}/example/versions" | grep -qx '201'

  for sidecar in "$shasums_name" "$signature_name"; do
    local destination='shasums'
    [[ "$sidecar" = "$signature_name" ]] && destination='shasums.sig'
    docker run --rm --network "$network" \
      -v "$fixture/$sidecar:/fixture/$sidecar:ro" \
      curlimages/curl:8.16.0 -fsS -o /dev/null -w '%{http_code}' \
      --upload-file "/fixture/$sidecar" \
      "http://app:5131/api/providers/${provider_namespace}/example/versions/1.0.0/${destination}" | grep -qx '204'
  done

  for sidecar in "$shasums_name" "$signature_name"; do
    docker compose --project-name "$project" --project-directory "$home_dir" -f "$home_dir/compose.yaml" \
      run --rm --no-deps --entrypoint /bin/sh minio-init -c \
      "mc alias set local http://minio:9000 minioadmin minioadmin >/dev/null && mc cat local/modules/providers/${provider_namespace}/example/1.0.0/${sidecar}" \
      | cmp - "$fixture/$sidecar"
  done

  printf 'MinIO S3 provider sidecar upload/read contract passed.\n'
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

  network="${project}_default"
  if [[ "$storage_provider" = s3 ]]; then
    run_s3_provider_sidecar_contract "$project" "$HOME_DIR" "$network" "$fixture"
  fi

  unzip -q "$ROOT/TerraformRegistry.Tests/TestData/test-module.zip" -d "$fixture/module"
  tar -C "$fixture/module" -czf "$fixture/test-module.tar.gz" .

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
      hashicorp/terraform:1.14.2@sha256:eee2f7d5725bfcfd734dfc9fe5a3df4b58b00eb8cc874993458108d8943265cf init -input=false -no-color
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
