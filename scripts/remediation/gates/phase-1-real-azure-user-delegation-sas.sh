#!/usr/bin/env bash
set -euo pipefail

# Runs only in the protected Azure environment after azure/login OIDC has
# authenticated the intended workload identity. It proves the principal can
# obtain and consume a real Entra user-delegation SAS without account keys.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
: "${AZURE_STORAGE_ACCOUNT_NAME:?AZURE_STORAGE_ACCOUNT_NAME is required}"
: "${AZURE_STORAGE_CONTAINER:?AZURE_STORAGE_CONTAINER is required}"
command -v az >/dev/null
command -v curl >/dev/null

blob="phase-1-user-delegation-$(date +%s)-$RANDOM.zip"
expiry="$(date -u -d '+15 minutes' '+%Y-%m-%dT%H:%MZ')"

cleanup() {
  az storage blob delete \
    --account-name "$AZURE_STORAGE_ACCOUNT_NAME" \
    --container-name "$AZURE_STORAGE_CONTAINER" \
    --name "$blob" \
    --auth-mode login \
    --only-show-errors >/dev/null 2>&1 || true
}
trap cleanup EXIT

az account show --output none
az storage container show \
  --account-name "$AZURE_STORAGE_ACCOUNT_NAME" \
  --name "$AZURE_STORAGE_CONTAINER" \
  --auth-mode login \
  --only-show-errors >/dev/null
az storage blob upload \
  --account-name "$AZURE_STORAGE_ACCOUNT_NAME" \
  --container-name "$AZURE_STORAGE_CONTAINER" \
  --name "$blob" \
  --file "$ROOT/TerraformRegistry.Tests/TestData/test-module.zip" \
  --auth-mode login \
  --overwrite true \
  --only-show-errors >/dev/null

user_delegation_url="$(az storage blob generate-sas \
  --account-name "$AZURE_STORAGE_ACCOUNT_NAME" \
  --container-name "$AZURE_STORAGE_CONTAINER" \
  --name "$blob" \
  --permissions r \
  --expiry "$expiry" \
  --as-user \
  --auth-mode login \
  --full-uri \
  --only-show-errors -o tsv)"

curl --fail --silent --show-error "$user_delegation_url" -o /dev/null
printf 'Real Azure user-delegation SAS gate passed.\n'
