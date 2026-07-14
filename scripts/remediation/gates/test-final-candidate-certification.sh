#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
GATE="${FINAL_CANDIDATE_GATE:-$ROOT/scripts/remediation/gates/final-candidate-certification.sh}"
WORKFLOW="${FINAL_CANDIDATE_WORKFLOW:-$ROOT/.github/workflows/ci.yaml}"
RUNBOOK="${FINAL_CANDIDATE_RUNBOOK:-$ROOT/docs/release-operations-runbook.md}"

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
grep -Fq 'FINAL_CANDIDATE_SHA' "$GATE"
grep -Fq 'FINAL_CANDIDATE_REF' "$GATE"
if grep -Fq 'CANDIDATE_SHA="${GITHUB_SHA:-' "$GATE"; then
  echo 'Final-candidate gate falls back to the event SHA.' >&2
  exit 1
fi
grep -Fq 'candidate_sha' "$GATE"
grep -Fq 'candidate_version' "$GATE"
grep -Fq 'image_digest' "$GATE"
grep -Fq 'pre-publication-verification' "$GATE"
grep -Fq 'incomplete-requires-immutable-registry-digest' "$GATE"
grep -Fq 'release_certification_complete: false,' "$GATE"
grep -Fq 'image_digest: null,' "$GATE"
grep -Fq 'required_post_publication_evidence: true,' "$GATE"
grep -Fq 'required_post_publication_evidence_kinds: ["immutable-registry-digest"],' "$GATE"
grep -Fq 'immutable-registry-digest' "$GATE"
grep -Fq 'Pre-publication candidate verification' "$RUNBOOK"
grep -Fq 'not release certification until immutable post-publication digest evidence is recorded' "$RUNBOOK"

job_body() {
  awk '
    $0 == "  final-candidate-certification:" { in_job = 1 }
    in_job && $0 ~ /^  [[:alnum:]_-]+:$/ && $0 != "  final-candidate-certification:" { exit }
    in_job { print }
  ' "$WORKFLOW"
}

upload_step_body() {
  awk '
    $0 == "      - name: Upload pre-publication candidate verification evidence" { in_step = 1 }
    in_step && $0 ~ /^      - name: / && $0 != "      - name: Upload pre-publication candidate verification evidence" { exit }
    in_step && $0 ~ /^  [[:alnum:]_-]+:$/ { exit }
    in_step { print }
  ' "$WORKFLOW"
}

workflow_dispatch_body() {
  awk '
    $0 == "  workflow_dispatch:" { in_dispatch = 1 }
    in_dispatch && /^[^[:space:]]/ { exit }
    in_dispatch && $0 ~ /^  [[:alnum:]_-]+:$/ && $0 != "  workflow_dispatch:" { exit }
    in_dispatch { print }
  ' "$WORKFLOW"
}

job="$(job_body)"
test -n "$job"
upload_step="$(upload_step_body)"
test -n "$upload_step"
workflow_dispatch="$(workflow_dispatch_body)"
test -n "$workflow_dispatch"

# Candidates are selected only by a durable same-repository release label or
# explicit immutable dispatch inputs. Do not couple this final gate to an old
# one-off remediation branch name.
grep -Fq 'types: [opened, synchronize, reopened, labeled]' "$WORKFLOW"
grep -Fq "'final-candidate'" <<<"$job"
if grep -Fq "github.event_name == 'merge_group'" <<<"$job"; then
  echo 'Final-candidate certification must not be eligible for merge groups.' >&2
  exit 1
fi
grep -Fq "github.event_name == 'workflow_dispatch'" <<<"$job"
grep -Fxq '       github.event.pull_request.head.repo.full_name == github.repository &&' <<<"$job"
grep -Fq 'test-final-candidate-certification.sh' <<<"$job"
grep -Fq 'fetch-depth: 0' <<<"$job"
grep -Fxq '      candidate_sha:' <<<"$workflow_dispatch"
grep -Fxq '      candidate_ref:' <<<"$workflow_dispatch"
grep -Fxq '        id: candidate' <<<"$job"
grep -Fxq '          CANDIDATE_SHA: ${{ inputs.candidate_sha || github.event.pull_request.head.sha }}' <<<"$job"
grep -Fxq "          CANDIDATE_REF: \${{ inputs.candidate_ref || (github.event_name == 'pull_request' && format('refs/heads/{0}', github.event.pull_request.head.ref)) }}" <<<"$job"
if grep -Fq 'github.sha' <<<"$job"; then
  echo 'Final-candidate certification must not use a synthetic GitHub SHA.' >&2
  exit 1
fi
if grep -Fq 'github.ref' <<<"$job"; then
  echo 'Final-candidate certification must not use a synthetic GitHub ref.' >&2
  exit 1
fi
grep -Fxq '          echo "sha=$candidate_sha" >> "$GITHUB_OUTPUT"' <<<"$job"
grep -Fxq '          echo "ref=$candidate_ref" >> "$GITHUB_OUTPUT"' <<<"$job"
grep -Fxq '          ref: ${{ steps.candidate.outputs.sha }}' <<<"$job"
grep -Fxq '      - name: Validate candidate reference provenance' <<<"$job"
grep -Fxq '          git check-ref-format "$CANDIDATE_REF"' <<<"$job"
case_pattern_count="$(grep -Fxc '            refs/heads/*|refs/tags/*) ;;' <<<"$job" || true)"
[[ "$case_pattern_count" == 2 ]] || {
  echo 'Final-candidate certification must validate canonical candidate refs before fetch and evidence binding.' >&2
  exit 1
}
grep -Fxq '          git fetch --no-tags origin "$CANDIDATE_REF"' <<<"$job"
grep -Fxq "          resolved_ref_sha=\"\$(git rev-parse 'FETCH_HEAD^{commit}')\"" <<<"$job"
grep -Fxq "            echo 'candidate_ref does not resolve to the requested immutable candidate SHA.' >&2" <<<"$job"
grep -Fq 'git rev-parse HEAD' <<<"$job"
grep -Fxq "            echo 'checkout did not resolve the requested immutable candidate SHA.' >&2" <<<"$job"
grep -Fxq '          env -u GITHUB_SHA -u GITHUB_REF \' <<<"$job"
grep -Fq '.github/scripts/resolve-version.sh' <<<"$job"
grep -Fq 'final-candidate-certification.sh' <<<"$job"
grep -Fq 'actions/upload-artifact@' <<<"$job"
if grep -Fxq '        if: always()' <<<"$upload_step"; then
  echo 'Unbound pre-publication evidence must not upload after a failed provenance check.' >&2
  exit 1
fi
grep -Fq 'Pre-publication candidate verification' <<<"$job"
grep -Fq 'pre-publication-candidate-verification-evidence' <<<"$job"

# Version evidence is meaningful only if the resolver's named step writes a
# checked, nonempty value and the gate consumes that exact output.
grep -Fxq '        id: candidate-version' <<<"$job"
grep -Fxq '          test -n "$version"' <<<"$job"
grep -Fxq '          echo "version=$version" >> "$GITHUB_OUTPUT"' <<<"$job"
grep -Fxq '          FINAL_CANDIDATE_VERSION: ${{ steps.candidate-version.outputs.version }}' <<<"$job"
grep -Fxq '          FINAL_CANDIDATE_SHA: ${{ steps.candidate.outputs.sha }}' <<<"$job"
grep -Fxq '          FINAL_CANDIDATE_REF: ${{ steps.candidate.outputs.ref }}' <<<"$job"

# The candidate checkout can contain an earlier version of the gate. Bind the
# artifact generated by that checkout to the workflow's already-validated,
# immutable identity before it is uploaded. In particular, a detached checkout
# must not leak the synthetic `HEAD` ref into release evidence.
grep -Fxq '      - name: Verify and bind pre-publication evidence provenance' <<<"$job"
grep -Fxq '          CANDIDATE_SHA: ${{ steps.candidate.outputs.sha }}' <<<"$job"
grep -Fxq '          CANDIDATE_REF: ${{ steps.candidate.outputs.ref }}' <<<"$job"
grep -Fxq '          EVIDENCE_PATH: ${{ runner.temp }}/final-candidate-certification-evidence.json' <<<"$job"
grep -Fxq '          artifact_sha="$(jq -er '\''.candidate_sha | strings'\'' "$EVIDENCE_PATH")"' <<<"$job"
grep -Fxq "            echo 'Pre-publication evidence candidate_sha does not match the explicit candidate SHA.' >&2" <<<"$job"
grep -Fxq '          stamped_evidence="$(mktemp "${EVIDENCE_PATH}.XXXXXX")"' <<<"$job"
grep -Fxq '          jq --arg candidate_ref "$CANDIDATE_REF" '\''.candidate_ref = $candidate_ref'\'' "$EVIDENCE_PATH" > "$stamped_evidence"' <<<"$job"
grep -Fxq '          artifact_ref="$(jq -er '\''.candidate_ref | strings'\'' "$EVIDENCE_PATH")"' <<<"$job"
grep -Fxq "            echo 'Pre-publication evidence candidate_ref does not match the explicit canonical candidate ref.' >&2" <<<"$job"
grep -Fq 'actions/upload-artifact@' <<<"$job"

verify_step_line="$(grep -nF '      - name: Verify and bind pre-publication evidence provenance' "$WORKFLOW" | cut -d: -f1)"
upload_step_line="$(grep -nF '      - name: Upload pre-publication candidate verification evidence' "$WORKFLOW" | cut -d: -f1)"
[[ "$verify_step_line" =~ ^[0-9]+$ && "$upload_step_line" =~ ^[0-9]+$ && "$verify_step_line" -lt "$upload_step_line" ]] || {
  echo 'Pre-publication evidence provenance must be bound before upload.' >&2
  exit 1
}

if [[ "${FINAL_CANDIDATE_SKIP_MUTATION:-false}" != true ]]; then
  mutation_root="$(mktemp -d)"
  trap 'rm -rf "$mutation_root"' EXIT

  for mutation in \
    'candidate_sha:' \
    'candidate_ref:' \
    'id: candidate' \
    'echo "sha=$candidate_sha" >> "$GITHUB_OUTPUT"' \
    'echo "ref=$candidate_ref" >> "$GITHUB_OUTPUT"' \
    'ref: ${{ steps.candidate.outputs.sha }}' \
    'Validate candidate reference provenance' \
    'git check-ref-format "$CANDIDATE_REF"' \
    'refs/heads/*|refs/tags/*)' \
    'git fetch --no-tags origin "$CANDIDATE_REF"' \
    "resolved_ref_sha=\"\$(git rev-parse 'FETCH_HEAD^{commit}')\"" \
    'candidate_ref does not resolve to the requested immutable candidate SHA' \
    'checkout did not resolve the requested immutable candidate SHA' \
    'env -u GITHUB_SHA -u GITHUB_REF' \
    'FINAL_CANDIDATE_SHA: ${{ steps.candidate.outputs.sha }}' \
    'FINAL_CANDIDATE_REF: ${{ steps.candidate.outputs.ref }}' \
    'Verify and bind pre-publication evidence provenance' \
    'artifact_sha="$(jq -er '\''.candidate_sha | strings'\'' "$EVIDENCE_PATH")"' \
    'Pre-publication evidence candidate_sha does not match the explicit candidate SHA.' \
    'stamped_evidence="$(mktemp "${EVIDENCE_PATH}.XXXXXX")"' \
    'jq --arg candidate_ref "$CANDIDATE_REF" '\''.candidate_ref = $candidate_ref'\'' "$EVIDENCE_PATH" > "$stamped_evidence"' \
    'artifact_ref="$(jq -er '\''.candidate_ref | strings'\'' "$EVIDENCE_PATH")"' \
    'Pre-publication evidence candidate_ref does not match the explicit canonical candidate ref.' \
    'id: candidate-version' \
    'test -n "$version"' \
    'echo "version=$version" >> "$GITHUB_OUTPUT"' \
    'FINAL_CANDIDATE_VERSION: ${{ steps.candidate-version.outputs.version }}'; do
    mutated_workflow="$mutation_root/ci.yaml"
    MUTATION="$mutation" perl -0pe 's/\Q$ENV{MUTATION}\E/$ENV{MUTATION} . q( removed)/e' \
      "$WORKFLOW" > "$mutated_workflow"
    if FINAL_CANDIDATE_WORKFLOW="$mutated_workflow" FINAL_CANDIDATE_SKIP_MUTATION=true "$0" >/dev/null 2>&1; then
      echo "Final-candidate version-handoff mutation was accepted: $mutation" >&2
      exit 1
    fi
  done

  for source_mutation in \
    'github.event.pull_request.head.sha' \
    'github.event.pull_request.head.ref' \
    'github.event.pull_request.head.repo.full_name == github.repository'; do
    mutated_workflow="$mutation_root/ci-source.yaml"
    SOURCE_MUTATION="$source_mutation" perl -0pe 's/\Q$ENV{SOURCE_MUTATION}\E/github.sha/' \
      "$WORKFLOW" > "$mutated_workflow"
    if FINAL_CANDIDATE_WORKFLOW="$mutated_workflow" FINAL_CANDIDATE_SKIP_MUTATION=true "$0" >/dev/null 2>&1; then
      echo "Final-candidate event-source mutation was accepted: $source_mutation" >&2
      exit 1
    fi
  done

  merge_group_workflow="$mutation_root/ci-merge-group.yaml"
  perl -0pe "s/github\.event_name == 'workflow_dispatch' \|\|/github.event_name == 'workflow_dispatch' ||\n      github.event_name == 'merge_group' ||/" \
    "$WORKFLOW" > "$merge_group_workflow"
  if FINAL_CANDIDATE_WORKFLOW="$merge_group_workflow" FINAL_CANDIDATE_SKIP_MUTATION=true "$0" >/dev/null 2>&1; then
    echo 'Final-candidate certification accepted a merge-group eligibility path.' >&2
    exit 1
  fi

  dispatch_root_escape_workflow="$mutation_root/dispatch-root-escape.yaml"
  perl -0pe 's/^      candidate_sha:$/      candidate_sha removed/m' "$WORKFLOW" > "$dispatch_root_escape_workflow"
  printf '\nsentinel:\n      candidate_sha:\n' >> "$dispatch_root_escape_workflow"
  if FINAL_CANDIDATE_WORKFLOW="$dispatch_root_escape_workflow" FINAL_CANDIDATE_SKIP_MUTATION=true "$0" >/dev/null 2>&1; then
    echo 'Final-candidate workflow-dispatch parser accepted a root-level escape.' >&2
    exit 1
  fi

  upload_before_binding_workflow="$mutation_root/upload-before-binding.yaml"
  perl -0pe 's{(\n      - name: Verify and bind pre-publication evidence provenance\n.*?)(\n      - name: Upload pre-publication candidate verification evidence\n.*?)(\n  docker:)}{$2$1$3}s' \
    "$WORKFLOW" > "$upload_before_binding_workflow"
  if cmp -s "$WORKFLOW" "$upload_before_binding_workflow"; then
    echo 'Final-candidate upload-before-binding mutation did not change the workflow.' >&2
    exit 1
  fi
  if FINAL_CANDIDATE_WORKFLOW="$upload_before_binding_workflow" FINAL_CANDIDATE_SKIP_MUTATION=true "$0" >/dev/null 2>&1; then
    echo 'Final-candidate certification accepted evidence upload before provenance binding.' >&2
    exit 1
  fi

  for mutation in \
    'release_certification_complete: false,' \
    'verification_status '\''pre-publication-verification'\''' \
    'incomplete-requires-immutable-registry-digest' \
    'image_digest: null,' \
    'required_post_publication_evidence: true,' \
    'required_post_publication_evidence_kinds: ["immutable-registry-digest"],'; do
    mutated_gate="$mutation_root/final-candidate-certification.sh"
    MUTATION="$mutation" perl -0pe 's/\Q$ENV{MUTATION}\E/MUTATED_RELEASE_COMPLETION_INVARIANT/' "$GATE" > "$mutated_gate"
    chmod +x "$mutated_gate"
    if FINAL_CANDIDATE_GATE="$mutated_gate" FINAL_CANDIDATE_SKIP_MUTATION=true "$0" >/dev/null 2>&1; then
      echo "Final-candidate release-completion mutation was accepted: $mutation" >&2
      exit 1
    fi
  done

  non_null_digest_gate="$mutation_root/non-null-image-digest.sh"
  sed '0,/image_digest: null,/s//image_digest: "sha256:not-a-registry-digest",/' "$GATE" > "$non_null_digest_gate"
  chmod +x "$non_null_digest_gate"
  if FINAL_CANDIDATE_GATE="$non_null_digest_gate" FINAL_CANDIDATE_SKIP_MUTATION=true "$0" >/dev/null 2>&1; then
    echo 'Final-candidate non-null image-digest mutation was accepted.' >&2
    exit 1
  fi

  weakened_runbook="$mutation_root/release-operations-runbook.md"
  sed '0,/not release certification until immutable post-publication digest evidence is recorded/s//release certification is complete/' "$RUNBOOK" > "$weakened_runbook"
  if FINAL_CANDIDATE_RUNBOOK="$weakened_runbook" FINAL_CANDIDATE_SKIP_MUTATION=true "$0" >/dev/null 2>&1; then
    echo 'Final-candidate weakened runbook release-certification wording was accepted.' >&2
    exit 1
  fi
fi
