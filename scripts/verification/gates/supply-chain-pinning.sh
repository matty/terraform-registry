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
  'postgres:18@sha256:bd5dfa494a5a259704365813dbfa5fa6b3e69be7348d43b547f540c730478583' \
  'dpage/pgadmin4:latest@sha256:3591b3d02910dcd817dec6fcaa681c278ba6966945283dd0ea0042731c330c83' \
  'mcr.microsoft.com/azure-storage/azurite:3.33.0@sha256:2628ee10a72833cc344b9d194cd8b245543892b307d16cf26a2cf55a15b816af' \
  'pgsty/minio:RELEASE.2026-08-04T00-00-00Z@sha256:b6bfe7239bfc83fb90d31612d9704d86039dd714f7904b3f1ad68f211e602372' \
  'pgsty/mc:RELEASE.2026-10-18T00-00-00Z@sha256:6ecdaec70bc5e8cf9d0e9a70c190f3beb1c83f11617ff4333f1fcd71ab1daf18' \
  'caddy:2.11-alpine@sha256:003a97808e781aa9f7e75cc2f43d34d461099031faf5e20faefb9f61126836f0'; do
  grep -Fq "\`$image\`" "$MANIFEST"
done

while IFS= read -r image; do
  if [[ ! "$image" =~ ^[[:space:]]*image:[[:space:]]+[^[:space:]]+@sha256:[0-9a-f]{64}[[:space:]]*$ ]]; then
    echo "Mutable Compose image reference: $image" >&2
    exit 1
  fi

  # Every image we actually run must also be documented in the build-inputs
  # manifest. Without this the two drift apart silently: a digest bump lands in
  # Compose, the manifest keeps describing the old digest, and nothing fails.
  reference="${image#*image:}"
  reference="${reference//[[:space:]]/}"
  if ! grep -Fq "\`$reference\`" "$MANIFEST"; then
    echo "Compose image missing from $MANIFEST: $reference" >&2
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
