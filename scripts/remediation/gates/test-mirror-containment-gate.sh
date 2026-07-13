#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
GATE="$ROOT/scripts/remediation/gates/mirror-containment-gate.sh"

test -x "$GATE"
grep -Fq 'ModuleMirrorServiceTests' "$GATE"
grep -Fq 'ProviderMirrorServiceTests' "$GATE"
grep -Fq 'MirrorLeaseHeartbeatTests' "$GATE"
grep -Fq 'ProviderMirrorEndpointTests' "$GATE"
grep -Fq 'SqlitePaginationScaleEvidenceTests' "$GATE"
grep -Eq '^  mirror-containment-gate:' "$ROOT/.github/workflows/ci.yaml"
grep -Fq 'mirror-containment-gate.sh' "$ROOT/.github/workflows/ci.yaml"
