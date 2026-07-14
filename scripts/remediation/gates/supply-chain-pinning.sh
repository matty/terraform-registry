#!/usr/bin/env bash
set -euo pipefail

ROOT="${SUPPLY_CHAIN_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)}"
DOCKERFILE="$ROOT/Dockerfile"
DEV_DOCKERFILE="$ROOT/Dockerfile.dev"
MANIFEST="$ROOT/docs/build-inputs.md"
EXCEPTION="$ROOT/docs/security-exceptions/SUP-003-nuxt-ui.md"

test -f "$MANIFEST"
test -f "$EXCEPTION"

grep -Eq '^ARG TERRAFORM_CONFIG_INSPECT_VERSION=[0-9a-f]{40}$' "$DOCKERFILE"
! grep -Eq '^FROM .*(latest|:[^@[:space:]]+([[:space:]]|$))' "$DOCKERFILE"
! grep -Eq '^FROM .*(latest|:[^@[:space:]]+([[:space:]]|$))' "$DEV_DOCKERFILE"
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
  "$ROOT/scripts/remediation/storage-emulators/compose.yaml")

contains_affected_nuxt_form() {
  local file
  while IFS= read -r file; do
    if grep -Eiq '<u(auth)?form\b|\bU(Auth)?Form\b|u-auth-form|u-form' "$file"; then
      printf '%s\n' "$file"
      return 0
    fi
  done < <(find "$ROOT/TerraformRegistry/web-src" -type f \
    ! -name package-lock.json \
    ! -name pnpm-lock.yaml \
    ! -path '*/node_modules/*' -print)

  return 1
}

if contains_affected_nuxt_form; then
  echo 'The SUP-003 exception is no longer valid: an affected Nuxt UI form is reachable.' >&2
  exit 1
fi

grep -Fq 'Owner: `matty`' "$EXCEPTION"
grep -Eq '^[-] Expiry: 20[0-9]{2}-[0-9]{2}-[0-9]{2}$' "$EXCEPTION"
grep -Fq 'Compensating control' "$EXCEPTION"
expiry="$(sed -n 's/^- Expiry: //p' "$EXCEPTION")"
if [[ "$expiry" < "$(date -u +%F)" || "$expiry" == "$(date -u +%F)" ]]; then
  echo "The SUP-003 exception has expired; upgrade or re-assess @nuxt/ui." >&2
  exit 1
fi

while IFS= read -r action; do
  if [[ ! "$action" =~ @[0-9a-f]{40}([[:space:]]|$) ]]; then
    echo "Mutable GitHub Action reference: $action" >&2
    exit 1
  fi
done < <(find "$ROOT/.github/workflows" -type f -exec grep -hE '^[[:space:]]*uses: [^[:space:]]+@' {} +)
