#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
HARNESS="$(mktemp -d)"
trap 'rm -rf "$HARNESS"' EXIT

if "$ROOT/scripts/remediation/storage-emulators/storage-emulators.sh" status --home "$HARNESS" >/tmp/storage-emulator-status.out 2>&1; then
  echo "Expected status to fail before the harness is initialized." >&2
  exit 1
fi

grep -Fq 'not initialized' /tmp/storage-emulator-status.out
