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
  [[ "$run_head_sha" == "$candidate_revision" ]] || fail "candidate verification run does not verify the candidate revision"
  [[ "$run_name" == CI ]] || fail "candidate verification run is not the CI workflow"
  run_jobs="$("$GH_BIN" api "repos/matty/terraform-registry/actions/runs/$run_id/jobs" --paginate --jq '.jobs[] | [.name, .conclusion] | @tsv' 2>/dev/null)" || fail "candidate verification jobs cannot be read"
  for required_job in 'Pre-publication candidate verification'; do
    grep -Fqx "$required_job"$'\t''success' <<<"$run_jobs" || fail "candidate verification lacks successful '$required_job' evidence"
  done
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
must instead be tied to one successful CI run for its exact revision, with the
combined `Pre-publication candidate verification` job successful.

## Requirement evidence

| Requirement | State | Pull request | Merge record | Automation gate | Checked-in evidence |
|---|---|---|---|---|---|
HEADER
  while IFS=$'\t' read -r id state pull_request merge_record gate evidence _; do
    [[ -z "$id" || "$id" == \#* ]] && continue
    printf '| `%s` | %s | [#%s](https://github.com/matty/terraform-registry/pull/%s) | `%s` | `%s` | `%s` |\n' "$id" "$state" "$pull_request" "$pull_request" "$merge_record" "$gate" "$evidence"
  done < "$MANIFEST"
  cat <<'CANDIDATE'

## Current release candidate

Final certification is **pending** while the candidate evidence uses `REQUIRED`.
The required fields are declared, rather than fabricated, in
[`release-candidate-evidence.md`](release-candidate-evidence.md): image digest,
source revision, verification-run URL, Terraform backend matrix, fault/load, and
operability results. The validator rejects absent fields and rejects a mixture of
pending and completed values.
CANDIDATE
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
