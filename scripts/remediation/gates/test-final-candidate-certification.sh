#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
GATE="${FINAL_CANDIDATE_GATE:-$ROOT/scripts/remediation/gates/final-candidate-certification.sh}"
WORKFLOW="${FINAL_CANDIDATE_WORKFLOW:-$ROOT/.github/workflows/ci.yaml}"
RUNBOOK="$ROOT/docs/release-operations-runbook.md"

test -x "$GATE"

# The certificate must run the executable P5/P6 evidence rather than merely
# describe the historical certification jobs.
for gate in \
  'test-operability-certification.sh' \
  'operability-certification.sh' \
  'test-fault-load-certification.sh' \
  'fault-load-certification.sh' \
  'test-terraform-backend-matrix.sh' \
  'terraform-backend-matrix.sh' \
  'release-runbooks.sh' \
  'test-release-runbooks-gate.sh'; do
  grep -Fq "$gate" "$GATE"
done

grep -Fq 'FINAL_CANDIDATE_EVIDENCE_PATH' "$GATE"
grep -Fq 'FINAL_CANDIDATE_VERSION' "$GATE"
grep -Fq 'candidate_sha' "$GATE"
grep -Fq 'candidate_version' "$GATE"
grep -Fq 'image_digest' "$GATE"
grep -Fq 'pre-publication-verification' "$GATE"
grep -Fq 'incomplete-requires-immutable-registry-digest' "$GATE"
grep -Fq 'release_certification_complete: false,' "$GATE"
grep -Fq 'post_publication_evidence_required: true,' "$GATE"
grep -Fq 'immutable-registry-digest' "$GATE"
grep -Fq 'Pre-publication candidate verification' "$RUNBOOK"
grep -Fq 'not release certification' "$RUNBOOK"

job_body() {
  awk '
    $0 == "  final-candidate-certification:" { in_job = 1 }
    in_job && $0 ~ /^  [[:alnum:]_-]+:$/ && $0 != "  final-candidate-certification:" { exit }
    in_job { print }
  ' "$WORKFLOW"
}

job="$(job_body)"
test -n "$job"

# Candidates are selected by a durable release label or the merge queue. Do
# not couple this final gate to an old one-off remediation branch name.
grep -Fq 'types: [opened, synchronize, reopened, labeled]' "$WORKFLOW"
grep -Fq "'final-candidate'" <<<"$job"
grep -Fq "github.event_name == 'merge_group'" <<<"$job"
grep -Fq 'test-final-candidate-certification.sh' <<<"$job"
grep -Fq 'fetch-depth: 0' <<<"$job"
grep -Fq '.github/scripts/resolve-version.sh' <<<"$job"
grep -Fq 'final-candidate-certification.sh' <<<"$job"
grep -Fq 'actions/upload-artifact@' <<<"$job"
grep -Fq 'Pre-publication candidate verification' <<<"$job"
grep -Fq 'pre-publication-candidate-verification-evidence' <<<"$job"

# Version evidence is meaningful only if the resolver's named step writes a
# checked, nonempty value and the gate consumes that exact output.
grep -Fxq '        id: candidate-version' <<<"$job"
grep -Fxq '          test -n "$version"' <<<"$job"
grep -Fxq '          echo "version=$version" >> "$GITHUB_OUTPUT"' <<<"$job"
grep -Fxq '          FINAL_CANDIDATE_VERSION: ${{ steps.candidate-version.outputs.version }}' <<<"$job"

if [[ "${FINAL_CANDIDATE_SKIP_MUTATION:-false}" != true ]]; then
  mutation_root="$(mktemp -d)"
  trap 'rm -rf "$mutation_root"' EXIT

  for mutation in \
    'id: candidate-version' \
    'test -n "$version"' \
    'echo "version=$version" >> "$GITHUB_OUTPUT"' \
    'FINAL_CANDIDATE_VERSION: ${{ steps.candidate-version.outputs.version }}'; do
    mutated_workflow="$mutation_root/ci.yaml"
    sed "0,/$mutation/s//$mutation removed/" "$WORKFLOW" > "$mutated_workflow"
    if FINAL_CANDIDATE_WORKFLOW="$mutated_workflow" FINAL_CANDIDATE_SKIP_MUTATION=true "$0" >/dev/null 2>&1; then
      echo "Final-candidate version-handoff mutation was accepted: $mutation" >&2
      exit 1
    fi
  done

  for mutation in \
    'release_certification_complete: false,' \
    'post_publication_evidence_required: true,' \
    'incomplete-requires-immutable-registry-digest'; do
    mutated_gate="$mutation_root/final-candidate-certification.sh"
    sed "0,/$mutation/s//MUTATED_RELEASE_COMPLETION_INVARIANT/" "$GATE" > "$mutated_gate"
    chmod +x "$mutated_gate"
    if FINAL_CANDIDATE_GATE="$mutated_gate" FINAL_CANDIDATE_SKIP_MUTATION=true "$0" >/dev/null 2>&1; then
      echo "Final-candidate release-completion mutation was accepted: $mutation" >&2
      exit 1
    fi
  done
fi
