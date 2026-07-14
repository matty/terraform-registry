#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
GATE="$ROOT/scripts/remediation/gates/final-candidate-certification.sh"
WORKFLOW="$ROOT/.github/workflows/ci.yaml"

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
grep -Fq 'candidate_sha' "$GATE"
grep -Fq 'image_digest' "$GATE"
grep -Fq 'not-published-by-certification' "$GATE"

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
grep -Fq "'final-candidate'" <<<"$job"
grep -Fq "github.event_name == 'merge_group'" <<<"$job"
grep -Fq 'test-final-candidate-certification.sh' <<<"$job"
grep -Fq 'final-candidate-certification.sh' <<<"$job"
grep -Fq 'actions/upload-artifact@' <<<"$job"
grep -Fq 'final-candidate-certification-evidence' <<<"$job"
