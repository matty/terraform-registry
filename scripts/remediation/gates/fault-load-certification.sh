#!/usr/bin/env bash
set -euo pipefail

# Deterministic, bounded release-certification evidence. The suites cover the
# migration state matrix, publication/storage faults, bounded extraction,
# mirror contention and lease loss, authorization, cancellation propagation,
# and the 100,000-version pagination evidence fixture. They use disposable
# SQLite/Testcontainer fixtures and SDK mocks where a cloud control plane is
# unavailable; the separate emulator and Terraform jobs retain real backend
# coverage without making this gate depend on cloud credentials.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

ASPNETCORE_ENVIRONMENT=Test dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj \
  --configuration Release \
  --filter 'FullyQualifiedName~MigrationAcceptanceMatrixTests|FullyQualifiedName~DbUpPostgresqlMigrationTests|FullyQualifiedName~LocalModuleServiceTests|FullyQualifiedName~AzureBlobModuleServiceUploadTests|FullyQualifiedName~S3ModuleServiceUploadTests|FullyQualifiedName~S3ModuleServicePurgeAndHealthTests|FullyQualifiedName~ModuleExtractionQueueRuntimeTests|FullyQualifiedName~ArchiveWorkspaceFactoryTests|FullyQualifiedName~ModulePublishCoordinatorTests|FullyQualifiedName~ModuleMirrorServiceTests|FullyQualifiedName~ProviderMirrorServiceTests|FullyQualifiedName~MirrorLeaseHeartbeatTests|FullyQualifiedName~MirrorDownloadAdmissionTests|FullyQualifiedName~NamespaceAuthorizationServiceTests|FullyQualifiedName~RbacTests|FullyQualifiedName~ApiKeyExpirationTests|FullyQualifiedName~ArtifactDownloadTokenServiceTests|FullyQualifiedName~TerraformLoginAuthorizationTests|FullyQualifiedName~LlmHandlersCancellationTests|FullyQualifiedName~ModuleHandlersPaginationTests|FullyQualifiedName~S3ModuleServiceDelegationTests|FullyQualifiedName~SqlitePaginationScaleEvidenceTests' \
  --blame-hang \
  --blame-hang-timeout 5m

printf 'Fault and load certification gate passed.\n'
