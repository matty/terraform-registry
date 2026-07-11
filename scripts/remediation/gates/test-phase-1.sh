#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
GATE="$ROOT/scripts/remediation/gates/phase-1.sh"

test -x "$GATE"
grep -Fq 'phase-1-local-terraform-smoke.sh' "$GATE"
grep -Fq 'phase-1-storage-emulator-terraform-smoke.sh' "$GATE"
grep -Fq 'ModuleMirrorServiceTests' "$GATE"
grep -Fq 'ApiKeyExpirationTests' "$GATE"
grep -Fq 'SemVerValidatorTests' "$GATE"
grep -Fq 'TF_REGISTRY_REQUIRE_REAL_AZURE' "$GATE"
grep -Fq 'docker image inspect' "$GATE"
grep -Eq '^  phase-1-deployment-gate:' "$ROOT/.github/workflows/ci.yaml"
grep -Fq 'scripts/remediation/gates/phase-1.sh' "$ROOT/.github/workflows/ci.yaml"
