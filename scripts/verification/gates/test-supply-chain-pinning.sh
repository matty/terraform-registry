#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"

bash "$ROOT/scripts/verification/gates/supply-chain-pinning.sh"

portable_bin="$(mktemp -d)"
portable_output="$(mktemp)"
trap 'rm -rf "${fixture_root:-}" "$portable_bin" "$portable_output"' EXIT
for command in bash date find grep sed; do
  ln -s "$(command -v "$command")" "$portable_bin/$command"
done

PATH="$portable_bin" SUPPLY_CHAIN_ROOT="$ROOT" bash "$ROOT/scripts/verification/gates/supply-chain-pinning.sh" > "$portable_output" 2>&1
if grep -Fq 'command not found' "$portable_output"; then
  echo 'Supply-chain gate must not require tools outside its portable toolset.' >&2
  cat "$portable_output" >&2
  exit 1
fi

fixture_root="$(mktemp -d)"

mkdir -p "$fixture_root"/{docs,scripts/verification/gates,TerraformRegistry/web-src,.github}
cp "$ROOT/Dockerfile" "$ROOT/Dockerfile.dev" "$fixture_root/"
cp "$ROOT/docs/build-inputs.md" "$fixture_root/docs/"
cp "$ROOT/TerraformRegistry/web-src/package.json" \
  "$ROOT/TerraformRegistry/web-src/package-lock.json" \
  "$fixture_root/TerraformRegistry/web-src/"
cp -a "$ROOT/.github/workflows" "$fixture_root/.github/"
mkdir -p "$fixture_root/scripts/verification"
cp -a "$ROOT/scripts/verification/storage-emulators" "$fixture_root/scripts/verification/"
cp "$ROOT/docker-compose.dev.yml" "$ROOT/docker-compose.psql.yml" "$fixture_root/"

expect_failure() {
  local name="$1"
  if SUPPLY_CHAIN_ROOT="$fixture_root" bash "$ROOT/scripts/verification/gates/supply-chain-pinning.sh"; then
    echo "Expected supply-chain gate to reject $name." >&2
    exit 1
  fi
}

sed -i '0,/^FROM node:/s|^FROM node:.* AS frontend$|FROM node AS frontend|' "$fixture_root/Dockerfile"
expect_failure 'a bare external Docker base image'
cp "$ROOT/Dockerfile" "$fixture_root/Dockerfile"

if ! SUPPLY_CHAIN_ROOT="$fixture_root" bash "$ROOT/scripts/verification/gates/supply-chain-pinning.sh"; then
  echo 'A Docker build stage based on an earlier pinned stage must remain valid.' >&2
  exit 1
fi

sed -i 's/@sha256:[0-9a-f]*/:latest/' "$fixture_root/docker-compose.dev.yml"
expect_failure 'a mutable Compose image'
cp "$ROOT/docker-compose.dev.yml" "$fixture_root/docker-compose.dev.yml"

printf '\nRUN apk upgrade --no-cache\n' >> "$fixture_root/Dockerfile"
expect_failure 'an unpinned Alpine package upgrade'
cp "$ROOT/Dockerfile" "$fixture_root/Dockerfile"

touch "$fixture_root/TerraformRegistry/web-src/pnpm-lock.yaml"
expect_failure 'a pnpm lockfile in the npm-only frontend'
rm "$fixture_root/TerraformRegistry/web-src/pnpm-lock.yaml"

touch "$fixture_root/TerraformRegistry/web-src/pnpm-workspace.yaml"
expect_failure 'a pnpm workspace file in the npm-only frontend'
rm "$fixture_root/TerraformRegistry/web-src/pnpm-workspace.yaml"

touch "$fixture_root/TerraformRegistry/web-src/yarn.lock"
expect_failure 'a Yarn lockfile in the npm-only frontend'
rm "$fixture_root/TerraformRegistry/web-src/yarn.lock"

touch "$fixture_root/TerraformRegistry/web-src/bun.lock"
expect_failure 'a Bun lockfile in the npm-only frontend'
rm "$fixture_root/TerraformRegistry/web-src/bun.lock"

touch "$fixture_root/TerraformRegistry/web-src/bun.lockb"
expect_failure 'a Bun binary lockfile in the npm-only frontend'
rm "$fixture_root/TerraformRegistry/web-src/bun.lockb"

sed -i 's/"nuxt": "[^"]*"/"nuxt": "^3.21.11"/' \
  "$fixture_root/TerraformRegistry/web-src/package.json"
expect_failure 'a Nuxt 3 frontend manifest'
