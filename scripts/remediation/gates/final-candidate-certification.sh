#!/usr/bin/env bash
set -euo pipefail

# Runs the portable P5/P6 release evidence for a labelled final-candidate PR
# or merge-queue candidate.  This script deliberately records a null digest:
# certification builds local disposable images but does not publish an image,
# so no immutable registry digest exists at this point.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

EVIDENCE_PATH="${FINAL_CANDIDATE_EVIDENCE_PATH:-$ROOT/final-candidate-certification-evidence.json}"
MATRIX_HOME="${FINAL_CANDIDATE_MATRIX_HOME:-${RUNNER_TEMP:-/tmp}/terraform-registry-final-candidate-matrix}"
CANDIDATE_SHA="${GITHUB_SHA:-$(git rev-parse HEAD)}"
CANDIDATE_REF="${GITHUB_REF:-$(git rev-parse --abbrev-ref HEAD)}"
EVENT_NAME="${GITHUB_EVENT_NAME:-local}"
CANDIDATE_VERSION="${FINAL_CANDIDATE_VERSION:?FINAL_CANDIDATE_VERSION must be resolved before certification starts}"
DOTNET_SDK_VERSION="$(dotnet --version)"
readonly TERRAFORM_VERSIONS='["1.12.0", "1.14.2"]'

declare -a gate_names=()
declare -a gate_results=()
status=0

record_gate() {
  local name="$1"
  shift

  if "$@"; then
    gate_names+=("$name")
    gate_results+=("passed")
  else
    gate_names+=("$name")
    gate_results+=("failed")
    status=1
  fi
}

write_evidence() {
  local gates_json='[]'
  local index

  for index in "${!gate_names[@]}"; do
    gates_json="$(jq --arg name "${gate_names[$index]}" --arg result "${gate_results[$index]}" \
      '. + [{name: $name, result: $result}]' <<<"$gates_json")"
  done

  mkdir -p "$(dirname "$EVIDENCE_PATH")"
  jq -n \
    --arg candidate_sha "$CANDIDATE_SHA" \
    --arg candidate_ref "$CANDIDATE_REF" \
    --arg candidate_version "$CANDIDATE_VERSION" \
    --arg event_name "$EVENT_NAME" \
    --arg dotnet_sdk_version "$DOTNET_SDK_VERSION" \
    --arg image_digest_status 'not-published-by-certification' \
    --argjson terraform_versions "$TERRAFORM_VERSIONS" \
    --argjson gates "$gates_json" \
    --argjson passed "$([[ "$status" -eq 0 ]] && echo true || echo false)" \
    '{
      schema_version: 1,
      candidate_sha: $candidate_sha,
      candidate_ref: $candidate_ref,
      candidate_version: $candidate_version,
      event_name: $event_name,
      dotnet_sdk_version: $dotnet_sdk_version,
      terraform_versions: $terraform_versions,
      gates: $gates,
      passed: $passed,
      image_digest: null,
      image_digest_status: $image_digest_status,
      image_digest_note: "Certification does not publish an image. image_digest remains null until the registry reports an immutable sha256 digest for this exact candidate.",
      generated_at_utc: (now | todate)
    }' > "$EVIDENCE_PATH"
}

cleanup_matrix() {
  bash scripts/remediation/storage-emulators/storage-emulators.sh clean \
    --home "$MATRIX_HOME/terraform-1.12.0" >/dev/null 2>&1 || true
  bash scripts/remediation/storage-emulators/storage-emulators.sh clean \
    --home "$MATRIX_HOME/terraform-1.14.2" >/dev/null 2>&1 || true
}

trap 'cleanup_matrix; write_evidence' EXIT

record_gate operability-contract bash scripts/remediation/gates/test-operability-certification.sh
record_gate operability bash scripts/remediation/gates/operability-certification.sh
record_gate fault-load-contract bash scripts/remediation/gates/test-fault-load-certification.sh
record_gate fault-load bash scripts/remediation/gates/fault-load-certification.sh
record_gate terraform-backend-contract bash scripts/remediation/test-terraform-backend-matrix.sh
record_gate terraform-backend-matrix bash scripts/remediation/terraform-backend-matrix.sh --home "$MATRIX_HOME"
record_gate release-runbooks bash scripts/remediation/gates/release-runbooks.sh
record_gate release-runbooks-contract bash scripts/remediation/gates/test-release-runbooks-gate.sh

if [[ "$status" -ne 0 ]]; then
  echo 'Final-candidate certification failed; inspect the evidence artifact for every gate result.' >&2
  exit "$status"
fi

printf 'Final-candidate certification passed. Evidence: %s\n' "$EVIDENCE_PATH"
