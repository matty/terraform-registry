#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/ci.yaml"

grep -Eq '^  storage-emulator-contract:' "$WORKFLOW"
grep -Fq 'phase-1-storage-emulator-terraform-smoke.sh --provider all' "$WORKFLOW"
grep -Fq 'if: failure()' "$WORKFLOW"
