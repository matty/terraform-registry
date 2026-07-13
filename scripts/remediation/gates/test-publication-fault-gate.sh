#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
GATE="$ROOT/scripts/remediation/gates/publication-fault-gate.sh"
WORKFLOW="$ROOT/.github/workflows/ci.yaml"

test -x "$GATE"
grep -Fq 'LocalModuleServiceTests' "$GATE"
grep -Fq 'AzureBlobModuleServiceUploadTests' "$GATE"
grep -Fq 'S3ModuleServiceUploadTests' "$GATE"
grep -Fq 'S3ModuleServicePurgeAndHealthTests' "$GATE"
grep -Fq 'ModuleExtractionQueueRuntimeTests' "$GATE"
grep -Fq 'SqliteDatabaseServiceTests' "$GATE"
grep -Fq 'UploadModuleExtractionTests' "$GATE"
grep -Eq '^  publication-fault-gate:' "$WORKFLOW"
grep -Fq 'test/publication-fault-gate' "$WORKFLOW"
grep -Fq 'scripts/remediation/gates/publication-fault-gate.sh' "$WORKFLOW"
