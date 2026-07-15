#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
HARNESS="$(mktemp -d)"
cleanup() {
  local status=$?
  rm -rf "$HARNESS"
  exit "$status"
}
trap cleanup EXIT

if "$ROOT/scripts/verification/storage-emulators/storage-emulators.sh" status --home "$HARNESS" >/tmp/storage-emulator-status.out 2>&1; then
  echo "Expected status to fail before the harness is initialized." >&2
  exit 1
fi

grep -Fq 'not initialized' /tmp/storage-emulator-status.out

LIFECYCLE="$ROOT/scripts/verification/storage-emulators/storage-emulators.sh"
grep -Fq 'compose up -d --build --force-recreate azurite minio postgres' "$LIFECYCLE"
grep -Fq 'compose run --rm minio-init' "$LIFECYCLE"
grep -Fq 'compose up -d --build --force-recreate app caddy' "$LIFECYCLE"
