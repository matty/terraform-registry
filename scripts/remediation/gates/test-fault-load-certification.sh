#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
GATE="$ROOT/scripts/remediation/gates/fault-load-certification.sh"
WORKFLOW="$ROOT/.github/workflows/ci.yaml"

test -x "$GATE"

# Each selected suite is executable evidence for one release-certification area.
for suite in \
  MigrationAcceptanceMatrixTests \
  DbUpPostgresqlMigrationTests \
  LocalModuleServiceTests \
  AzureBlobModuleServiceUploadTests \
  S3ModuleServiceUploadTests \
  ModuleExtractionQueueRuntimeTests \
  ArchiveWorkspaceFactoryTests \
  ModuleMirrorServiceTests \
  ProviderMirrorServiceTests \
  MirrorLeaseHeartbeatTests \
  NamespaceAuthorizationServiceTests \
  RbacTests \
  ApiKeyExpirationTests \
  ArtifactDownloadTokenServiceTests \
  LlmHandlersCancellationTests \
  SqlitePaginationScaleEvidenceTests; do
  grep -Fq "$suite" "$GATE"
done

grep -Eq '^  fault-load-certification:' "$WORKFLOW"
grep -Fq 'test/fault-load-certification' "$WORKFLOW"
grep -Fq 'scripts/remediation/gates/fault-load-certification.sh' "$WORKFLOW"
