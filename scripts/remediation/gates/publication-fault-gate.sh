#!/usr/bin/env bash
set -euo pipefail

# Portable transactional-publication evidence. These suites deliberately exercise
# the same create/replace/purge and durable-extraction contracts for local, Azure
# Blob, and S3-compatible storage without requiring cloud credentials.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

ASPNETCORE_ENVIRONMENT=Test dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj \
  --configuration Release \
  --filter 'FullyQualifiedName~LocalModuleServiceTests|FullyQualifiedName~AzureBlobModuleServiceUploadTests|FullyQualifiedName~S3ModuleServiceUploadTests|FullyQualifiedName~S3ModuleServicePurgeAndHealthTests|FullyQualifiedName~ModuleExtractionQueueRuntimeTests|FullyQualifiedName~SqliteDatabaseServiceTests|FullyQualifiedName~MirrorRepositoryTests|FullyQualifiedName~UploadModuleExtractionTests' \
  --blame-hang \
  --blame-hang-timeout 5m

printf 'Transactional publication fault gate passed.\n'
