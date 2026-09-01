#!/usr/bin/env bash
set -euo pipefail

ROOT="${SUPPLY_CHAIN_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)}"
DOCKERFILE="$ROOT/Dockerfile"
DEV_DOCKERFILE="$ROOT/Dockerfile.dev"
MANIFEST="$ROOT/docs/build-inputs.md"
FRONTEND_DIR="$ROOT/TerraformRegistry/web-src"
PACKAGE_MANIFEST="$FRONTEND_DIR/package.json"
PACKAGE_LOCK="$FRONTEND_DIR/package-lock.json"

validate_dockerfile_bases() {
  local dockerfile="$1"
  local line remainder reference stage index
  local -a tokens
  local -A stages=()

  while IFS= read -r line || [[ -n "$line" ]]; do
    if [[ ! "$line" =~ ^[[:space:]]*[Ff][Rr][Oo][Mm][[:space:]]+(.+)$ ]]; then
      continue
    fi

    remainder="${BASH_REMATCH[1]}"
    read -r -a tokens <<< "$remainder"
    index=0
    while [[ "${tokens[$index]:-}" == --* ]]; do
      ((index += 1))
    done

    reference="${tokens[$index]:-}"
    if [[ -z "$reference" ]]; then
      echo "Docker FROM instruction has no base image in $dockerfile: $line" >&2
      return 1
    fi

    if [[ -z "${stages[$reference]:-}" && ! "$reference" =~ @sha256:[0-9a-f]{64}$ ]]; then
      echo "External Docker base image must be pinned by digest in $dockerfile: $reference" >&2
      return 1
    fi

    if [[ "${tokens[$((index + 1))]:-}" =~ ^[Aa][Ss]$ && -n "${tokens[$((index + 2))]:-}" ]]; then
      stage="${tokens[$((index + 2))]}"
      stages["$stage"]=1
    fi
  done < "$dockerfile"
}

test -f "$MANIFEST"
test -f "$PACKAGE_MANIFEST"
test -f "$PACKAGE_LOCK"

for pnpm_input in "$FRONTEND_DIR/pnpm-lock.yaml" "$FRONTEND_DIR/pnpm-workspace.yaml"; do
  if [[ -e "$pnpm_input" ]]; then
    echo "npm-only frontend contains pnpm input: $pnpm_input" >&2
    exit 1
  fi
done

for alternative_lockfile in "$FRONTEND_DIR/yarn.lock" "$FRONTEND_DIR/bun.lock" "$FRONTEND_DIR/bun.lockb"; do
  if [[ -e "$alternative_lockfile" ]]; then
    echo "npm-only frontend contains alternative package-manager input: $alternative_lockfile" >&2
    exit 1
  fi
done

grep -Eq '"nuxt"[[:space:]]*:[[:space:]]*"\^4\.' "$PACKAGE_MANIFEST"
grep -Eq '"@nuxt/ui"[[:space:]]*:[[:space:]]*"\^4\.11\.' "$PACKAGE_MANIFEST"

grep -Eq '^ARG TERRAFORM_CONFIG_INSPECT_VERSION=[0-9a-f]{40}$' "$DOCKERFILE"
validate_dockerfile_bases "$DOCKERFILE"
validate_dockerfile_bases "$DEV_DOCKERFILE"
if grep -Eq '^[[:space:]]*RUN .*apk[[:space:]]+upgrade' "$DOCKERFILE" "$DEV_DOCKERFILE"; then
  echo 'Unpinned Alpine package upgrades are not reproducible.' >&2
  exit 1
fi

grep -Eq '^\| Terraform CLI \| `1\.14\.2`, `hashicorp/terraform@sha256:[0-9a-f]{64}`' "$MANIFEST"
grep -Eq '^\| terraform-config-inspect \| `[0-9a-f]{40}`' "$MANIFEST"
for image in \
  'postgres:18@sha256:48ebba8b80dc3be58b5ae431f47a33535289959cddfe13f5f887298de959fae0' \
  'dpage/pgadmin4:latest@sha256:40fa840c5bb7c8463957f1255b01283732c2d8c9396a956d180f8e6c296753b3' \
  'mcr.microsoft.com/azure-storage/azurite:3.33.0@sha256:2628ee10a72833cc344b9d194cd8b245543892b307d16cf26a2cf55a15b816af' \
  'minio/minio:RELEASE.2025-04-22T22-12-26Z@sha256:a1ea29fa28355559ef137d71fc570e508a214ec84ff8083e39bc5428980b015e' \
  'minio/mc:RELEASE.2025-03-12T17-29-24Z@sha256:470f5546b596e16c7816b9c3fa7a78ce4076bb73c2c73f7faeec0c8043923123' \
  'caddy:2.11-alpine@sha256:5f5c8640aae01df9654968d946d8f1a56c497f1dd5c5cda4cf95ab7c14d58648'; do
  grep -Fq "\`$image\`" "$MANIFEST"
done

while IFS= read -r image; do
  if [[ ! "$image" =~ ^[[:space:]]*image:[[:space:]]+[^[:space:]]+@sha256:[0-9a-f]{64}[[:space:]]*$ ]]; then
    echo "Mutable Compose image reference: $image" >&2
    exit 1
  fi
done < <(grep -hE '^[[:space:]]*image:' \
  "$ROOT/docker-compose.dev.yml" \
  "$ROOT/docker-compose.psql.yml" \
  "$ROOT/scripts/verification/storage-emulators/compose.yaml")

while IFS= read -r action; do
  if [[ ! "$action" =~ @[0-9a-f]{40}([[:space:]]|$) ]]; then
    echo "Mutable GitHub Action reference: $action" >&2
    exit 1
  fi
done < <(find "$ROOT/.github/workflows" -type f -exec grep -hE '^[[:space:]]*uses: [^[:space:]]+@' {} +)
