#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
GATE="$ROOT/scripts/remediation/gates/operability-certification.sh"
DETACHED_WORK_CHECK="$ROOT/scripts/remediation/gates/assert-no-detached-work.sh"
WORKFLOW="$ROOT/.github/workflows/ci.yaml"
PROJECT="$ROOT/TerraformRegistry.Tests/TerraformRegistry.Tests.csproj"
PINNED_SDK_VERSION="$(sed -nE 's/^[[:space:]]*"version": "([^"]+)",?$/\1/p' "$ROOT/global.json")"

job_body() {
  local job_name="$1"

  awk -v job_name="$job_name" '
    $0 == "  " job_name ":" { in_job = 1 }
    in_job && $0 ~ /^  [[:alnum:]_-]+:$/ && $0 != "  " job_name ":" { exit }
    in_job { print }
  ' "$WORKFLOW"
}

test -x "$GATE"
test -x "$DETACHED_WORK_CHECK"
if [[ -z "$PINNED_SDK_VERSION" || "$(dotnet --version)" != "$PINNED_SDK_VERSION" ]]; then
  echo "Operability selector validation requires SDK $PINNED_SDK_VERSION." >&2
  exit 1
fi

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

# These are the focused fixtures added for the operability certificate. Their
# exact class names matter: the test runner must discover them before the gate
# can claim they are covered.
for fixture in \
  ModuleDownloadAnalyticsBufferTests \
  SqliteOperationalDatabaseMetricsFacadeTests \
  PostgreSqlOperationalDatabaseMetricsFacadeTests \
  OidcSecurityTests; do
  if ! grep -Fq "FullyQualifiedName~$fixture" "$GATE"; then
    echo "Operability gate is missing required selector: $fixture" >&2
    exit 1
  fi
done

# Extract all selectors from the gate and prove that the pinned SDK discovers
# each one. This prevents a stale class name from turning a certification
# filter into an empty promise.
filter_line="$(grep -E -- '--filter ' "$GATE")"
filter_expression="$(sed -E "s/.*--filter '([^']*)'.*/\\1/" <<<"$filter_line")"
if [[ -z "$filter_expression" || "$filter_expression" == "$filter_line" ]]; then
  echo "Unable to extract the operability test filter." >&2
  exit 1
fi

listed_tests="$(mktemp)"
trap 'rm -rf "$fixture_root" "$listed_tests"' EXIT
dotnet test "$PROJECT" --configuration Release --list-tests >"$listed_tests"

while IFS= read -r selector; do
  selector="${selector#FullyQualifiedName~}"
  if [[ -z "$selector" ]]; then
    echo "Operability gate contains an empty test selector." >&2
    exit 1
  fi

  if ! grep -Fq "$selector" "$listed_tests"; then
    echo "Operability gate selector matches no discovered test: $selector" >&2
    exit 1
  fi
done < <(tr '|' '\n' <<<"$filter_expression")

for fixture in \
  DurableAuditServiceTests \
  DurableOutboxProcessorTests \
  SqliteOutboxEventRepositoryTests \
  ModuleExtractionQueueRuntimeTests \
  SqliteMirrorRepositoryTests \
  ModuleDownloadAnalyticsBufferTests \
  OperationalMetricsStartupTests \
  OperationalMetricsTests \
  SqliteOperationalDatabaseMetricsFacadeTests \
  PostgreSqlOperationalDatabaseMetricsFacadeTests \
  SensitiveDataRedactorTests \
  MirrorCacheBudgetServiceTests \
  MirrorDownloadAdmissionTests \
  MirrorLeaseHeartbeatTests \
  ProviderMirrorServiceTests \
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
  ApiKeyServiceSecurityTests \
  OidcSecurityTests; do
  grep -Fq "$fixture" "$listed_tests"
done

grep -Fq 'test-supply-chain-pinning.sh' "$GATE"
fault_load_job="$(job_body fault-load-certification)"
operability_job="$(job_body operability-certification)"

grep -Fq "github.head_ref == 'test/fault-load-certification'" <<<"$fault_load_job"
grep -Fq 'permissions:' <<<"$fault_load_job"
grep -Fq 'contents: read' <<<"$fault_load_job"
grep -Fq 'steps:' <<<"$fault_load_job"
grep -Fq 'scripts/remediation/gates/test-fault-load-certification.sh' <<<"$fault_load_job"
grep -Fq 'scripts/remediation/gates/fault-load-certification.sh' <<<"$fault_load_job"

grep -Fq "github.head_ref == 'test/operability-certification'" <<<"$operability_job"
grep -Fq 'permissions:' <<<"$operability_job"
grep -Fq 'contents: read' <<<"$operability_job"
grep -Fq 'steps:' <<<"$operability_job"
grep -Fq 'scripts/remediation/gates/test-operability-certification.sh' <<<"$operability_job"
grep -Fq 'scripts/remediation/gates/operability-certification.sh' <<<"$operability_job"
if grep -Fq 'fault-load-certification.sh' <<<"$operability_job"; then
  echo "Fault/load certification must not run inside the operability job." >&2
  exit 1
fi
