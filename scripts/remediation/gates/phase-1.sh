#!/usr/bin/env bash
set -euo pipefail

# Phase 1 release-interoperability gate. This runs the portable evidence on every
# host with Docker. The managed-identity portion is deliberately strict only in
# a protected Azure environment: an emulator cannot issue a user-delegation key.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
IMAGE="terraform-registry-phase-1-gate:$(git -C "$ROOT" rev-parse --short HEAD)"

cleanup() {
  docker image rm -f "$IMAGE" >/dev/null 2>&1 || true
}
trap cleanup EXIT

cd "$ROOT"

ASPNETCORE_ENVIRONMENT=Test dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj \
  --configuration Release \
  --filter 'FullyQualifiedName~ModuleMirrorServiceTests|FullyQualifiedName~ApiKeyExpirationTests|FullyQualifiedName~RbacTests|FullyQualifiedName~TerraformLoginTokenTests|FullyQualifiedName~SemVerValidatorTests|FullyQualifiedName~SqliteDatabaseServiceTests|FullyQualifiedName~AzureBlobModuleServiceDownloadTests|FullyQualifiedName~AzureBlobProviderArtifactStorageTests'

bash scripts/remediation/phase-1-local-terraform-smoke.sh
bash scripts/remediation/phase-1-storage-emulator-terraform-smoke.sh --provider all

marker="phase-1-gate-$(git rev-parse --short HEAD)"
docker build --build-arg "FRONTEND_BUILD_MARKER=$marker" -t "$IMAGE" .
test "$(docker image inspect --format '{{.Config.User}}' "$IMAGE")" = app
docker run --rm --entrypoint /bin/sh "$IMAGE" -c 'test -w /app/modules && test -w /app/providers && test -w /data'
test "$(docker run --rm --entrypoint cat "$IMAGE" /app/web/.build-marker)" = "$marker"

if [[ "${TF_REGISTRY_REQUIRE_REAL_AZURE:-0}" = 1 ]]; then
  : "${AZURE_STORAGE_ACCOUNT_NAME:?AZURE_STORAGE_ACCOUNT_NAME is required for the real Azure gate}"
  : "${AZURE_STORAGE_CONTAINER:?AZURE_STORAGE_CONTAINER is required for the real Azure gate}"
  command -v az >/dev/null
  az account show --output none
  az storage container show \
    --account-name "$AZURE_STORAGE_ACCOUNT_NAME" \
    --name "$AZURE_STORAGE_CONTAINER" \
    --auth-mode login \
    --output none
  printf 'Real Azure identity was able to read the configured storage container.\n'
else
  printf 'Real Azure managed-identity SAS gate not run; set TF_REGISTRY_REQUIRE_REAL_AZURE=1 in the protected Azure environment.\n'
fi

printf 'Phase 1 portable deployment gate passed.\n'
