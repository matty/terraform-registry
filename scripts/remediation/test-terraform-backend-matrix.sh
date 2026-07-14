#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MATRIX="$ROOT/scripts/remediation/terraform-backend-matrix.sh"
WORKFLOW="$ROOT/.github/workflows/ci.yaml"
BUILD_INPUTS="$ROOT/docs/build-inputs.md"
EMULATOR_COMPOSE="$ROOT/scripts/remediation/storage-emulators/compose.yaml"

test -x "$MATRIX"
grep -Fq 'hashicorp/terraform:1.12.0@sha256:be40b1de9a0f97b1e859235aca824d1bac4cf5c0dd715074aa45595ea055aa8b' "$MATRIX"
grep -Fq 'hashicorp/terraform:1.14.2@sha256:eee2f7d5725bfcfd734dfc9fe5a3df4b58b00eb8cc874993458108d8943265cf' "$MATRIX"
grep -Fq 'Terraform CLI support window' "$BUILD_INPUTS"
grep -Fq 'hashicorp/terraform@sha256:be40b1de9a0f97b1e859235aca824d1bac4cf5c0dd715074aa45595ea055aa8b' "$BUILD_INPUTS"
grep -Fq 'phase-1-local-terraform-smoke.sh' "$MATRIX"
grep -Fq 'phase-1-storage-emulator-terraform-smoke.sh' "$MATRIX"
grep -Fq 'run_emulator_provider azure' "$MATRIX"
grep -Fq 'run_emulator_provider s3' "$MATRIX"
grep -Fq 'provider-registry-terraform-smoke-test.sh' "$MATRIX"
test -f "$ROOT/scripts/remediation/terraform-provider-smoke.Dockerfile"
grep -Fq 'USER app' "$ROOT/scripts/remediation/terraform-provider-smoke.Dockerfile"
grep -Fq 'TF_REG_SMOKE_REMOTE_APP_BASE_URL' "$MATRIX"
grep -Fq 'TF_REG_SMOKE_REMOTE_APP_BASE_URL' "$ROOT/devutils/provider-registry-terraform-smoke-test.sh"
grep -Fq 'chmod 755 "$terraform_dir"' "$MATRIX"
grep -Fq 'caddy-root.crt' "$MATRIX"
grep -Fq 'terraform-provider-smoke build inputs' "$BUILD_INPUTS"
grep -Fq "docker image inspect --format '{{.Config.User}}'" "$MATRIX"
grep -Eq '^  terraform-backend-matrix:' "$WORKFLOW"
grep -Fq 'scripts/remediation/test-terraform-backend-matrix.sh' "$WORKFLOW"
grep -Fq 'scripts/remediation/terraform-backend-matrix.sh' "$WORKFLOW"

# The matrix starts the emulator compose stack for each provider. Validate its
# rendered YAML so an overlapping remediation change cannot introduce duplicate
# mapping keys that stop the certification before either backend is exercised.
REGISTRY_ROOT="$ROOT" STORAGE_PROVIDER=azure docker compose -f "$EMULATOR_COMPOSE" config -q
