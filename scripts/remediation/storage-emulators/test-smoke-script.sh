#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
SMOKE="$ROOT/scripts/remediation/phase-1-storage-emulator-terraform-smoke.sh"

if grep -Eq 'docker logs .*\| grep -q' "$SMOKE"; then
  echo 'Readiness checks must not pipe docker logs into grep under pipefail.' >&2
  exit 1
fi

grep -Fq '[[ "$(docker logs "$app" 2>&1)" == *"Application started"* ]]' "$SMOKE"
grep -Fq 'run_s3_provider_sidecar_contract' "$SMOKE"
grep -Fq 'providers/${provider_namespace}/example/1.0.0/${sidecar}' "$SMOKE"
