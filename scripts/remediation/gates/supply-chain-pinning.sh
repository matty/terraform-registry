#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
DOCKERFILE="$ROOT/Dockerfile"
DEV_DOCKERFILE="$ROOT/Dockerfile.dev"
MANIFEST="$ROOT/docs/build-inputs.md"
EXCEPTION="$ROOT/docs/security-exceptions/SUP-003-nuxt-ui.md"

test -f "$MANIFEST"
test -f "$EXCEPTION"

grep -Eq '^ARG TERRAFORM_CONFIG_INSPECT_VERSION=[0-9a-f]{40}$' "$DOCKERFILE"
! grep -Eq '^FROM .*(latest|:[^@[:space:]]+([[:space:]]|$))' "$DOCKERFILE"
! grep -Eq '^FROM .*(latest|:[^@[:space:]]+([[:space:]]|$))' "$DEV_DOCKERFILE"

grep -Eq '^\| Terraform CLI \| `1\.14\.2`, `hashicorp/terraform@sha256:[0-9a-f]{64}`' "$MANIFEST"
grep -Eq '^\| terraform-config-inspect \| `[0-9a-f]{40}`' "$MANIFEST"
grep -Eq '^\| .* \| `.*@sha256:[0-9a-f]{64}`' "$MANIFEST"

if rg -n -i '<u(auth)?form\\b|\\bU(Auth)?Form\\b|u-auth-form|u-form' \
  "$ROOT/TerraformRegistry/web-src" \
  --glob '!package-lock.json' --glob '!pnpm-lock.yaml' --glob '!node_modules/**'; then
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
done < <(rg -N '^\s*uses: [^[:space:]]+@' "$ROOT/.github/workflows")
