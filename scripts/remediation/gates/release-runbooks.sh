#!/usr/bin/env bash
set -euo pipefail

ROOT="${RELEASE_RUNBOOK_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)}"
RUNBOOK="$ROOT/docs/release-operations-runbook.md"

test -f "$RUNBOOK"

for heading in \
  'Backup, restore, and migration state' \
  'Digest rollout and rollback' \
  'Signing and secret rotation' \
  'Extraction jobs and mirror cache' \
  'Alerts and incident response' \
  'Compatibility windows'; do
  grep -Fq "## $heading" "$RUNBOOK"
done

for endpoint in \
  '/health' \
  '/ready' \
  '/api/admin/module-docs/summary' \
  '/api/admin/mirror/config' \
  '/api/admin/mirror/leases'; do
  grep -Fq "$endpoint" "$RUNBOOK"
done

grep -Fq 'phase-0-migration-recovery-runbook.md' "$RUNBOOK"
grep -Fq 'supply-chain-pinning.sh' "$RUNBOOK"
grep -Fq 'MapGet("/health"' "$ROOT/TerraformRegistry/Startup/EndpointMappingExtensions.cs"
grep -Fq 'MapGet("/ready"' "$ROOT/TerraformRegistry/Startup/EndpointMappingExtensions.cs"
grep -Fq 'MapGet("/api/admin/module-docs/summary"' "$ROOT/TerraformRegistry/Startup/AdminEndpointMappingExtensions.cs"
grep -Fq 'MapGet("/api/admin/mirror/config"' "$ROOT/TerraformRegistry/Startup/AdminEndpointMappingExtensions.cs"
grep -Fq 'MapGet("/api/admin/mirror/leases"' "$ROOT/TerraformRegistry/Startup/AdminEndpointMappingExtensions.cs"

echo 'Release operations runbook references verified.'
