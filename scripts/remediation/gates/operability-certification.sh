#!/usr/bin/env bash
set -euo pipefail

# Portable acceptance evidence for durable side effects and delivery policy. The
# selected fixtures are intentionally explicit so a requirement cannot silently
# disappear behind a broad test filter.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

if grep -R -n -E --include='*.cs' 'Task\.Run|Task\.Factory\.StartNew|async void' TerraformRegistry; then
  echo 'Detached background work is not permitted in the application source.' >&2
  exit 1
fi

bash scripts/remediation/gates/test-supply-chain-pinning.sh

ASPNETCORE_ENVIRONMENT=Test dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj \
  --configuration Release \
  --filter 'FullyQualifiedName~DurableAuditServiceTests|FullyQualifiedName~DurableOutboxProcessorTests|FullyQualifiedName~SqliteOutboxEventRepositoryTests|FullyQualifiedName~ModuleExtractionQueueRuntimeTests|FullyQualifiedName~SqliteMirrorRepositoryTests|FullyQualifiedName~ModuleDownloadAnalyticsQueueTests|FullyQualifiedName~ArchiveWorkspaceFactoryTests|FullyQualifiedName~ModuleExtractionServiceTests|FullyQualifiedName~TerraformConfigInspectRunnerTests|FullyQualifiedName~LocalModuleServiceTests|FullyQualifiedName~AzureBlobModuleServiceUploadTests|FullyQualifiedName~AzureBlobModuleServiceDelegationTests|FullyQualifiedName~S3ModuleServiceDelegationTests|FullyQualifiedName~S3ModuleServiceDownloadTests|FullyQualifiedName~LlmHandlersCancellationTests|FullyQualifiedName~ModuleHandlersPaginationTests|FullyQualifiedName~MirrorHttpClientTests|FullyQualifiedName~HttpDeliveryPolicyTests|FullyQualifiedName~BrowserSecurityHeaderTests|FullyQualifiedName~ApiKeyServiceSecurityTests' \
  --blame-hang \
  --blame-hang-timeout 5m

printf 'Operability certification gate passed.\n'
