#!/usr/bin/env bash
set -euo pipefail

# Release-certification evidence for the pinned Terraform CLI support window.
# It deliberately reuses the portable Local/Azurite/MinIO harness, whose test
# data and credentials are fabricated and scoped to disposable containers.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HOME_DIR="${HOME}/.terraform-registry-terraform-matrix"
VERSION="all"

readonly OLDEST_TERRAFORM_IMAGE='hashicorp/terraform:1.12.0@sha256:be40b1de9a0f97b1e859235aca824d1bac4cf5c0dd715074aa45595ea055aa8b'
readonly NEWEST_TERRAFORM_IMAGE='hashicorp/terraform:1.14.2@sha256:eee2f7d5725bfcfd734dfc9fe5a3df4b58b00eb8cc874993458108d8943265cf'

usage() {
  printf 'Usage: %s [--home ABSOLUTE_PATH] [--version oldest|newest|all]\n' "$0" >&2
  exit 2
}

while (($#)); do
  case "$1" in
    --home) HOME_DIR="${2:-}"; shift ;;
    --version) VERSION="${2:-}"; shift ;;
    *) usage ;;
  esac
  shift
done

[[ "$HOME_DIR" = /* ]] || usage
[[ "$VERSION" = oldest || "$VERSION" = newest || "$VERSION" = all ]] || usage
mkdir -p "$HOME_DIR"

run_version() {
  local label="$1" image="$2" terraform_dir container
  terraform_dir="$(mktemp -d)"
  container="$(docker create "$image")"
  trap 'docker rm -f "$container" >/dev/null 2>&1 || true; rm -rf "$terraform_dir"' RETURN
  docker cp "$container:/bin/terraform" "$terraform_dir/terraform"
  chmod +x "$terraform_dir/terraform"

  printf 'Running Terraform %s Local/Azurite/MinIO module matrix.\n' "$label"
  TF_REGISTRY_TERRAFORM_IMAGE="$image" \
    bash "$ROOT/scripts/remediation/phase-1-local-terraform-smoke.sh"
  TF_REGISTRY_TERRAFORM_IMAGE="$image" \
    bash "$ROOT/scripts/remediation/phase-1-storage-emulator-terraform-smoke.sh" \
      --provider all --home "$HOME_DIR/$label"

  # The provider smoke creates a signed fabricated provider and performs a
  # real Terraform installation. It is intentionally local: the emulator
  # module matrix above covers backend protocol behavior, while provider
  # storage implementations are exercised in the dedicated backend tests.
  printf 'Running Terraform %s signed-provider Local registry evidence.\n' "$label"
  docker build -f "$ROOT/scripts/remediation/terraform-provider-smoke.Dockerfile" \
    -t "terraform-registry-provider-smoke:$label" "$ROOT" >/dev/null
  docker run --rm -v "$ROOT:/src" -v "$terraform_dir:/tools:ro" -w /src \
    -e PATH='/tools:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin' \
    -e TF_REG_SMOKE_TERRAFORM_VERSION="${label#terraform-}" \
    "terraform-registry-provider-smoke:$label" \
    bash /src/devutils/provider-registry-terraform-smoke-test.sh
  docker image rm -f "terraform-registry-provider-smoke:$label" >/dev/null

  docker build -t "terraform-registry-matrix:$label" "$ROOT" >/dev/null
  test "$(docker image inspect --format '{{.Config.User}}' "terraform-registry-matrix:$label")" = app
  docker run --rm --entrypoint /bin/sh "terraform-registry-matrix:$label" \
    -c 'test -w /app/modules && test -w /app/providers && test -w /data'
  docker image rm -f "terraform-registry-matrix:$label" >/dev/null
}

if [[ "$VERSION" = oldest || "$VERSION" = all ]]; then
  run_version terraform-1.12.0 "$OLDEST_TERRAFORM_IMAGE"
fi

if [[ "$VERSION" = newest || "$VERSION" = all ]]; then
  run_version terraform-1.14.2 "$NEWEST_TERRAFORM_IMAGE"
fi

printf 'Terraform backend certification matrix passed.\n'
