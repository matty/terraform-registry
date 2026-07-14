#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"

bash "$ROOT/scripts/remediation/gates/supply-chain-pinning.sh"

portable_bin="$(mktemp -d)"
portable_output="$(mktemp)"
trap 'rm -rf "${fixture_root:-}" "$portable_bin" "$portable_output"' EXIT
for command in bash date find grep sed; do
  ln -s "$(command -v "$command")" "$portable_bin/$command"
done

PATH="$portable_bin" SUPPLY_CHAIN_ROOT="$ROOT" bash "$ROOT/scripts/remediation/gates/supply-chain-pinning.sh" > "$portable_output" 2>&1
if grep -Fq 'command not found' "$portable_output"; then
  echo 'Supply-chain gate must not require tools outside its portable toolset.' >&2
  cat "$portable_output" >&2
  exit 1
fi

fixture_root="$(mktemp -d)"

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

mkdir -p "$fixture_root/TerraformRegistry/web-src/.nuxt"
printf 'declare const UAuthForm: typeof import("@nuxt/ui")["UAuthForm"]\n' > "$fixture_root/TerraformRegistry/web-src/.nuxt/components.d.ts"
if ! SUPPLY_CHAIN_ROOT="$fixture_root" bash "$ROOT/scripts/remediation/gates/supply-chain-pinning.sh"; then
  echo 'Generated Nuxt declarations must not invalidate the SUP-003 exception.' >&2
  exit 1
fi

sed -i '0,/^FROM node:/s|^FROM node:.* AS frontend$|FROM node AS frontend|' "$fixture_root/Dockerfile"
expect_failure 'a bare external Docker base image'
cp "$ROOT/Dockerfile" "$fixture_root/Dockerfile"

if ! SUPPLY_CHAIN_ROOT="$fixture_root" bash "$ROOT/scripts/remediation/gates/supply-chain-pinning.sh"; then
  echo 'A Docker build stage based on an earlier pinned stage must remain valid.' >&2
  exit 1
fi

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
