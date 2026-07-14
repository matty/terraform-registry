#!/usr/bin/env bash
set -euo pipefail

# Portable acceptance evidence for the completed mirror-containment package and
# module-list SQL pagination. The selected fixtures cover the production
# mirror-containment and pagination contracts.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

ASPNETCORE_ENVIRONMENT=Test dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj \
  --configuration Release \
  --filter 'FullyQualifiedName~ModuleMirrorServiceTests|FullyQualifiedName~ProviderMirrorServiceTests|FullyQualifiedName~MirrorLeaseHeartbeatTests|FullyQualifiedName~MirrorDownloadAdmissionTests|FullyQualifiedName~MirrorCacheBudgetServiceTests|FullyQualifiedName~ProviderMirrorEndpointTests|FullyQualifiedName~SqlitePaginationScaleEvidenceTests|FullyQualifiedName~UploadAndListModulesTests'

printf 'Mirror containment gate passed.\n'
