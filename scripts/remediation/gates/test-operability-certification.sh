#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
GATE="$ROOT/scripts/remediation/gates/operability-certification.sh"
WORKFLOW="$ROOT/.github/workflows/ci.yaml"

test -x "$GATE"

# The certificate must reject detached background work in the application source.
grep -Fq "Detached background work" "$GATE"
grep -Fq "async void" "$GATE"

# Keep every gate requirement tied to a focused executable fixture.
for fixture in \
  DurableAuditServiceTests \
  DurableOutboxProcessorTests \
  SqliteOutboxEventRepositoryTests \
  ModuleExtractionQueueRuntimeTests \
  SqliteMirrorRepositoryTests \
  ModuleDownloadAnalyticsQueueTests \
  ArchiveWorkspaceFactoryTests \
  ModuleExtractionServiceTests \
  TerraformConfigInspectRunnerTests \
  LocalModuleServiceTests \
  AzureBlobModuleServiceUploadTests \
  AzureBlobModuleServiceDelegationTests \
  S3ModuleServiceDelegationTests \
  S3ModuleServiceDownloadTests \
  LlmHandlersCancellationTests \
  ModuleHandlersPaginationTests \
  MirrorHttpClientTests \
  HttpDeliveryPolicyTests \
  BrowserSecurityHeaderTests \
  ApiKeyServiceSecurityTests; do
  grep -Fq "$fixture" "$GATE"
done

grep -Fq 'test-supply-chain-pinning.sh' "$GATE"
grep -Eq '^  operability-certification:' "$WORKFLOW"
grep -Fq "github.head_ref == 'test/operability-certification'" "$WORKFLOW"
grep -Fq 'scripts/remediation/gates/operability-certification.sh' "$WORKFLOW"
