#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
GATE="$ROOT/scripts/remediation/gates/operability-certification.sh"
DETACHED_WORK_CHECK="$ROOT/scripts/remediation/gates/assert-no-detached-work.sh"
WORKFLOW="$ROOT/.github/workflows/ci.yaml"

test -x "$GATE"
test -x "$DETACHED_WORK_CHECK"

# The certificate must reject detached background work in the application source.
grep -Fq 'assert-no-detached-work.sh' "$GATE"
grep -Fq "Detached background work" "$DETACHED_WORK_CHECK"
grep -Fq "async[[:space:]]+void" "$DETACHED_WORK_CHECK"

# The detached-work control is executable, rather than a text-only promise. Each
# prohibited construct must reject a representative mutation in application source.
fixture_root="$(mktemp -d)"
trap 'rm -rf "$fixture_root"' EXIT
for mutation in \
  'Task.Run(() => { });' \
  'Task.Factory.StartNew(() => { });' \
  'async void FireAndForget() { await Task.CompletedTask; }' \
  'ThreadPool.QueueUserWorkItem(_ => { });' \
  'new Thread(() => { }).Start();'; do
  printf '%s\n' "$mutation" > "$fixture_root/DetachedWorkMutation.cs"
  if "$DETACHED_WORK_CHECK" "$fixture_root" >/dev/null 2>&1; then
    echo "Detached-work mutation was accepted: $mutation" >&2
    exit 1
  fi
done

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
  S3ModuleServiceUploadTests \
  ModulePublishCoordinatorTests \
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
