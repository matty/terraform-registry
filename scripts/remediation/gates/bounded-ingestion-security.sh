#!/usr/bin/env bash
set -euo pipefail

# Acceptance evidence for bounded archive/provider/webhook ingestion and the
# identity controls that authorize it. Keep the filter explicit so the
# production security contracts remain covered.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

ASPNETCORE_ENVIRONMENT=Test dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj \
  --configuration Release \
  --filter 'FullyQualifiedName~ArchiveWorkspaceFactoryTests|FullyQualifiedName~ModulePublishCoordinatorTests|FullyQualifiedName~ProviderRegistryServiceTests|FullyQualifiedName~VcsWebhookHandlersTests|FullyQualifiedName~VcsSourceTests|FullyQualifiedName~UserAdmissionOptionsTests|FullyQualifiedName~ApiKeyServiceSecurityTests|FullyQualifiedName~ApiKeyExpirationTests|FullyQualifiedName~RbacTests|FullyQualifiedName~NamespaceAuthorizationServiceTests|FullyQualifiedName~ArtifactDownloadTokenServiceTests|FullyQualifiedName~UploadModuleTests|FullyQualifiedName~TerraformAuthorizationCodeStoreTests|FullyQualifiedName~SqliteTerraformAuthorizationCodeStoreTests|FullyQualifiedName~RateLimitOptionsTests|FullyQualifiedName~ProviderMirrorEndpointTests'

printf 'Bounded ingestion and identity security gate passed.\n'
