#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SPEC="$ROOT/docs/remediation-specification.md"
MANIFEST="$ROOT/scripts/remediation/requirement-evidence.tsv"
STATUS="$ROOT/docs/remediation-status.md"
CANDIDATE="$ROOT/docs/release-candidate-evidence.md"
GH_BIN="${GH_BIN:-gh}"
OCI_INSPECT_BIN="${OCI_INSPECT_BIN:-$ROOT/scripts/remediation/inspect-oci-candidate.sh}"
MODE="${1:---check}"

case "$MODE" in
  --check|--write-status) ;;
  *) echo "Usage: $0 [--check|--write-status]" >&2; exit 2 ;;
esac

fail() {
  echo "requirement-evidence: $*" >&2
  return 1
}

verify_candidate_evidence_artifact() {
  local run_id="$1"
  local candidate_revision="$2"
  local artifact_ids artifact_id artifact_dir artifact_archive artifact_evidence artifact_ref
  local required_gate gate_count

  mapfile -t artifact_ids < <("$GH_BIN" api "repos/matty/terraform-registry/actions/runs/$run_id/artifacts" --paginate --jq \
    '.artifacts[] | select(.name == "pre-publication-candidate-verification-evidence" and .expired == false) | .id' 2>/dev/null) || {
    fail "candidate verification artifacts cannot be read"
    return 1
  }
  (( ${#artifact_ids[@]} == 1 )) || {
    fail "candidate verification must have exactly one readable pre-publication evidence artifact"
    return 1
  }
  artifact_id="${artifact_ids[0]}"
  [[ "$artifact_id" =~ ^[1-9][0-9]*$ ]] || {
    fail "candidate verification evidence artifact has an invalid identifier"
    return 1
  }

  artifact_dir="$(mktemp -d)" || {
    fail "candidate verification evidence cannot be staged"
    return 1
  }
  artifact_archive="$artifact_dir/evidence.zip"
  artifact_evidence="$artifact_dir/final-candidate-certification-evidence.json"
  if ! "$GH_BIN" api "repos/matty/terraform-registry/actions/artifacts/$artifact_id/zip" > "$artifact_archive" 2>/dev/null; then
    rm -rf "$artifact_dir"
    fail "candidate verification evidence artifact cannot be downloaded"
    return 1
  fi
  if [[ "$(unzip -Z1 "$artifact_archive" 2>/dev/null)" != 'final-candidate-certification-evidence.json' ]] ||
     ! unzip -p "$artifact_archive" final-candidate-certification-evidence.json > "$artifact_evidence" 2>/dev/null; then
    rm -rf "$artifact_dir"
    fail "candidate verification evidence artifact is unreadable or has an unexpected layout"
    return 1
  fi

  if ! jq -e --arg candidate_sha "$candidate_revision" '
      .schema_version == 1 and
      (.candidate_sha == $candidate_sha) and
      (.candidate_ref | type == "string" and length > 0) and
      (.candidate_version | type == "string" and length > 0) and
      (.event_name | type == "string" and (. == "workflow_dispatch" or . == "pull_request")) and
      (.dotnet_sdk_version | type == "string" and length > 0) and
      (.terraform_versions == ["1.12.0", "1.14.2"]) and
      (.gates | type == "array" and length > 0 and all(.[]; type == "object" and (.name | type == "string" and length > 0) and (.result | type == "string"))) and
      (.pre_publication_verification_passed == true) and
      (.verification_status == "pre-publication-verification") and
      (.release_certification_complete == false) and
      (.release_certification_status == "incomplete-requires-immutable-registry-digest") and
      (.image_digest == null) and
      (.required_post_publication_evidence == true) and
      (.required_post_publication_evidence_kinds | type == "array" and index("immutable-registry-digest")) and
      (.generated_at_utc | type == "string" and length > 0)
    ' "$artifact_evidence" >/dev/null 2>&1; then
    rm -rf "$artifact_dir"
    fail "candidate verification evidence artifact has an invalid schema, identity, or status"
    return 1
  fi

  artifact_ref="$(jq -er '.candidate_ref | strings' "$artifact_evidence" 2>/dev/null)" || {
    rm -rf "$artifact_dir"
    fail "candidate verification evidence artifact lacks a canonical candidate reference"
    return 1
  }
  case "$artifact_ref" in refs/heads/*|refs/tags/*) ;; *)
    rm -rf "$artifact_dir"
    fail "candidate verification evidence artifact has a non-canonical candidate reference"
    return 1
  esac
  if ! git check-ref-format "$artifact_ref"; then
    rm -rf "$artifact_dir"
    fail "candidate verification evidence artifact has an invalid candidate reference"
    return 1
  fi

  for required_gate in \
    operability-contract operability fault-load-contract fault-load \
    terraform-backend-contract terraform-backend-matrix \
    release-runbooks release-runbooks-contract; do
    gate_count="$(jq -er --arg name "$required_gate" '[.gates[] | select(.name == $name and .result == "passed")] | length' "$artifact_evidence" 2>/dev/null)" || {
      rm -rf "$artifact_dir"
      fail "candidate verification evidence artifact has unreadable gate results"
      return 1
    }
    [[ "$gate_count" == 1 ]] || {
      rm -rf "$artifact_dir"
      fail "candidate verification evidence artifact lacks one successful '$required_gate' gate result"
      return 1
    }
  done

  rm -rf "$artifact_dir"
}

test -f "$SPEC" || fail "missing specification: $SPEC"
test -f "$MANIFEST" || fail "missing manifest: $MANIFEST"
test -f "$CANDIDATE" || fail "missing candidate evidence: $CANDIDATE"

mapfile -t expected_ids < <(grep -oE '`[A-Z]+-[0-9]{3}`' "$SPEC" | tr -d '`' | sort -u)
(( ${#expected_ids[@]} > 0 )) || fail "no requirement IDs found in specification"

declare -A expected=()
declare -A seen=()
for id in "${expected_ids[@]}"; do expected["$id"]=1; done

while IFS=$'\t' read -r id state pull_request merge_record gate evidence extra; do
  [[ -z "$id" || "$id" == \#* ]] && continue
  [[ -z "${extra:-}" ]] || fail "$id has more than six manifest fields"
  [[ -n "${expected[$id]:-}" ]] || fail "$id is not a requirement in $SPEC"
  [[ -z "${seen[$id]:-}" ]] || fail "$id is duplicated in $MANIFEST"
  seen["$id"]=1
  [[ "$state" == MERGED ]] || fail "$id is not explicitly MERGED"
  [[ "$pull_request" =~ ^[1-9][0-9]*$ ]] || fail "$id has no pull request record"
  [[ "$merge_record" =~ ^[0-9a-f]{40}$ ]] || fail "$id has no immutable merge record"
  git -C "$ROOT" cat-file -e "$merge_record^{commit}" 2>/dev/null || fail "$id merge record is not a commit"
  git -C "$ROOT" merge-base --is-ancestor "$merge_record" HEAD || fail "$id merge record is not reachable from the current candidate"
  commit_message="$(git -C "$ROOT" show -s --format=%B "$merge_record")"
  grep -Eq "Merge pull request #${pull_request}([[:space:]]|$)|\\(#${pull_request}\\)" <<<"$commit_message" || fail "$id pull request #$pull_request is not bound to its merge record"
  [[ -x "$ROOT/$gate" ]] || fail "$id gate is absent or not executable: $gate"
  grep -R --include='*.yaml' --include='*.yml' --fixed-strings --quiet -- "$gate" "$ROOT/.github/workflows" || fail "$id gate is not wired into CI: $gate"
  [[ -e "$ROOT/$evidence" ]] || fail "$id evidence is absent: $evidence"
done < "$MANIFEST"

for id in "${expected_ids[@]}"; do
  [[ -n "${seen[$id]:-}" ]] || fail "$id has no manifest record"
done

candidate_values=()
while IFS= read -r field; do
  value="$(sed -n "s/^| $field | \(.*\) |$/\1/p" "$CANDIDATE")"
  [[ -n "$value" ]] || fail "candidate evidence lacks '$field'"
  candidate_values+=("$value")
done <<'FIELDS'
Candidate image digest
Candidate revision
Verification run URL
Terraform backend matrix result
Fault and load result
Operability gate result
FIELDS

pending_count=0
for value in "${candidate_values[@]}"; do [[ "$value" == REQUIRED ]] && ((pending_count += 1)); done
if (( pending_count != 0 && pending_count != ${#candidate_values[@]} )); then
  fail "candidate evidence mixes REQUIRED and completed fields"
fi
if (( pending_count == 0 )); then
  [[ "${candidate_values[0]}" =~ ^sha256:[0-9a-f]{64}$ ]] || fail "candidate digest is not immutable"
  [[ "${candidate_values[1]}" =~ ^[0-9a-f]{40}$ ]] || fail "candidate revision is not a commit SHA"
  candidate_revision="${candidate_values[1]}"
  git -C "$ROOT" cat-file -e "$candidate_revision^{commit}" 2>/dev/null || fail "candidate revision is not an existing commit"
  git -C "$ROOT" merge-base --is-ancestor "$candidate_revision" HEAD || fail "candidate revision is not reachable from the current target"
  oci_evidence="$("$OCI_INSPECT_BIN" "${candidate_values[0]}" 2>/dev/null)" || fail "candidate digest cannot be verified from published OCI evidence"
  IFS=$'\t' read -r oci_digest oci_revision <<<"$oci_evidence"
  [[ "$oci_digest" == "${candidate_values[0]}" ]] || fail "OCI evidence does not identify the candidate digest"
  [[ "$oci_revision" == "$candidate_revision" ]] || fail "OCI image revision label does not match the candidate revision"
  [[ "${candidate_values[2]}" =~ ^https://github\.com/matty/terraform-registry/actions/runs/([1-9][0-9]*)$ ]] || fail "candidate verification is not an authoritative repository run URL"
  run_id="${BASH_REMATCH[1]}"
  run_conclusion="$("$GH_BIN" api "repos/matty/terraform-registry/actions/runs/$run_id" --jq '.conclusion' 2>/dev/null)" || fail "candidate verification run cannot be read"
  run_head_sha="$("$GH_BIN" api "repos/matty/terraform-registry/actions/runs/$run_id" --jq '.head_sha' 2>/dev/null)" || fail "candidate verification run head cannot be read"
  run_name="$("$GH_BIN" api "repos/matty/terraform-registry/actions/runs/$run_id" --jq '.name' 2>/dev/null)" || fail "candidate verification run name cannot be read"
  [[ "$run_conclusion" == success ]] || fail "candidate verification run did not succeed"
  [[ "$run_head_sha" =~ ^[0-9a-f]{40}$ ]] || fail "candidate verification run does not identify an immutable workflow revision"
  [[ "$run_name" == CI ]] || fail "candidate verification run is not the CI workflow"
  run_jobs="$("$GH_BIN" api "repos/matty/terraform-registry/actions/runs/$run_id/jobs" --paginate --jq '.jobs[] | [.name, .conclusion] | @tsv' 2>/dev/null)" || fail "candidate verification jobs cannot be read"
  for required_job in 'Pre-publication candidate verification'; do
    grep -Fqx "$required_job"$'\t''success' <<<"$run_jobs" || fail "candidate verification lacks successful '$required_job' evidence"
  done
  verify_candidate_evidence_artifact "$run_id" "$candidate_revision"
  for index in 3 4 5; do [[ "${candidate_values[$index]}" == PASS ]] || fail "candidate gate result must be PASS"; done
fi

render_status() {
  cat <<'HEADER'
# Terraform Registry remediation status

> Generated from `scripts/remediation/requirement-evidence.tsv` by
> `scripts/remediation/validate-requirement-evidence.sh --write-status`.
> Do not edit this ledger by hand.

The validator confirms every requirement in the specification has an immutable
merge record reachable from the checked-out candidate, an executable automation
gate, and a checked-in evidence path. It intentionally does not infer a release
certification from historical records.

The listed gates document merged-work automation; their branch-specific CI
invocations are not treated as current-candidate evidence. A completed candidate
must instead be tied to one successful CI run with the combined
`Pre-publication candidate verification` job successful. Its immutable candidate
identity is verified from that run's bound pre-publication evidence artifact,
because a workflow-dispatch run itself is recorded at the workflow ref.

## Requirement evidence

| Requirement | State | Pull request | Merge record | Automation gate | Checked-in evidence |
|---|---|---|---|---|---|
HEADER
  while IFS=$'\t' read -r id state pull_request merge_record gate evidence _; do
    [[ -z "$id" || "$id" == \#* ]] && continue
    printf '| `%s` | %s | [#%s](https://github.com/matty/terraform-registry/pull/%s) | `%s` | `%s` | `%s` |\n' "$id" "$state" "$pull_request" "$pull_request" "$merge_record" "$gate" "$evidence"
  done < "$MANIFEST"
  if (( pending_count != 0 )); then
    cat <<'PENDING_CANDIDATE'

## Current release candidate

Final certification is **pending** while the candidate evidence uses `REQUIRED`.
The required fields are declared, rather than fabricated, in
[`release-candidate-evidence.md`](release-candidate-evidence.md): image digest,
source revision, verification-run URL, Terraform backend matrix, fault/load, and
operability results. The validator rejects absent fields and rejects a mixture of
pending and completed values.
PENDING_CANDIDATE
  else
    cat <<'CERTIFIED_CANDIDATE'

## Current release candidate

Final certification is **certified** with complete candidate evidence.
The image digest, source revision, verification-run URL, Terraform backend matrix,
fault/load, and operability results have been validated against published evidence.
CERTIFIED_CANDIDATE
  fi
}

if [[ "$MODE" == --write-status ]]; then
  render_status > "$STATUS"
else
  generated="$(mktemp)"
  trap 'rm -f "$generated"' EXIT
  render_status > "$generated"
  cmp -s "$generated" "$STATUS" || fail "$STATUS is stale; run $0 --write-status"
fi

echo "Requirement evidence validated (${#expected_ids[@]} requirements; candidate $([[ $pending_count -eq 0 ]] && echo certified || echo pending))."
