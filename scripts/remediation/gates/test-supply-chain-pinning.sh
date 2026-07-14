#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"

bash "$ROOT/scripts/remediation/gates/supply-chain-pinning.sh"

fixture_root="$(mktemp -d)"
trap 'rm -rf "$fixture_root"' EXIT

mkdir -p "$fixture_root"/{docs/security-exceptions,scripts/remediation/gates,TerraformRegistry/web-src,.github}
cp "$ROOT/Dockerfile" "$ROOT/Dockerfile.dev" "$fixture_root/"
cp "$ROOT/docs/build-inputs.md" "$fixture_root/docs/"
cp "$ROOT/docs/security-exceptions/SUP-003-nuxt-ui.md" "$fixture_root/docs/security-exceptions/"
cp -a "$ROOT/.github/workflows" "$fixture_root/.github/"
mkdir -p "$fixture_root/scripts/remediation"
cp -a "$ROOT/scripts/remediation/storage-emulators" "$fixture_root/scripts/remediation/"
cp "$ROOT/docker-compose.dev.yml" "$ROOT/docker-compose.psql.yml" "$fixture_root/"

expect_failure() {
  local name="$1"
  if SUPPLY_CHAIN_ROOT="$fixture_root" bash "$ROOT/scripts/remediation/gates/supply-chain-pinning.sh"; then
    echo "Expected supply-chain gate to reject $name." >&2
    exit 1
  fi
}

printf '<template><UAuthForm /></template>\n' > "$fixture_root/TerraformRegistry/web-src/affected.vue"
expect_failure 'UAuthForm'
rm "$fixture_root/TerraformRegistry/web-src/affected.vue"

printf '<template><UForm /></template>\n' > "$fixture_root/TerraformRegistry/web-src/affected.vue"
expect_failure 'UForm'
rm "$fixture_root/TerraformRegistry/web-src/affected.vue"

sed -i 's/@sha256:[0-9a-f]*/:latest/' "$fixture_root/docker-compose.dev.yml"
expect_failure 'a mutable Compose image'
cp "$ROOT/docker-compose.dev.yml" "$fixture_root/docker-compose.dev.yml"

printf '\nRUN apk upgrade --no-cache\n' >> "$fixture_root/Dockerfile"
expect_failure 'an unpinned Alpine package upgrade'
