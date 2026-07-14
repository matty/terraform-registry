#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VALIDATOR="$ROOT/scripts/remediation/validate-requirement-evidence.sh"

"$VALIDATOR" --check

copy="$(mktemp -d)"
trap 'rm -rf "$copy"' EXIT
cp -a "$ROOT/." "$copy"

sed -i '0,/^DB-001\t/s//DB-000\t/' "$copy/scripts/remediation/requirement-evidence.tsv"
if "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted a manifest that omits a specification requirement' >&2
  exit 1
fi

rm -rf "$copy"
copy="$(mktemp -d)"
cp -a "$ROOT/." "$copy"
sed -i '0,/^DB-002\t/s/\t[0-9a-f]\{40\}\t/\tmissing-merge-record\t/' "$copy/scripts/remediation/requirement-evidence.tsv"
if "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted a requirement without a merge record' >&2
  exit 1
fi

rm -rf "$copy"
copy="$(mktemp -d)"
cp -a "$ROOT/." "$copy"
sed -i '0,/^DB-001\t/s/\t53\t/\t999\t/' "$copy/scripts/remediation/requirement-evidence.tsv"
if "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted a pull request unrelated to its merge record' >&2
  exit 1
fi

rm -rf "$copy"
copy="$(mktemp -d)"
cp -a "$ROOT/." "$copy"
sed -i '0,/^DB-003\t/s#scripts/remediation/gates/phase-0.sh#scripts/remediation/gates/missing.sh#' "$copy/scripts/remediation/requirement-evidence.tsv"
if "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted a requirement without an automation gate' >&2
  exit 1
fi

rm -rf "$copy"
copy="$(mktemp -d)"
cp -a "$ROOT/." "$copy"
sed -i '0,/^DB-003\t/s#scripts/remediation/gates/phase-0.sh#scripts/remediation/gates/test-phase-1.sh#' "$copy/scripts/remediation/requirement-evidence.tsv"
if "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted an automation gate that CI does not run' >&2
  exit 1
fi

rm -rf "$copy"
copy="$(mktemp -d)"
cp -a "$ROOT/." "$copy"
sed -i '/| Fault and load result |/d' "$copy/docs/release-candidate-evidence.md"
if "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted missing current-candidate evidence' >&2
  exit 1
fi

rm -rf "$copy"
copy="$(mktemp -d)"
cp -a "$ROOT/." "$copy"
revision="$(git -C "$copy" rev-parse HEAD)"
digest="sha256:$(printf '%064d' 0)"
sed -i \
  -e "s#^| Candidate image digest | .* |\$#| Candidate image digest | $digest |#" \
  -e "s#^| Candidate revision | .* |\$#| Candidate revision | $revision |#" \
  -e 's#^| Verification run URL | .* |$#| Verification run URL | https://github.com/matty/terraform-registry/actions/runs/999 |#' \
  -e 's#^| Terraform backend matrix result | .* |$#| Terraform backend matrix result | PASS |#' \
  -e 's#^| Fault and load result | .* |$#| Fault and load result | PASS |#' \
  -e 's#^| Operability gate result | .* |$#| Operability gate result | PASS |#' \
  "$copy/docs/release-candidate-evidence.md"
fake_gh="$copy/fake-gh"
printf '%s\n' '#!/usr/bin/env bash' 'printf "{\\\"conclusion\\\":\\\"failure\\\",\\\"head_sha\\\":\\\"%s\\\"}\\n" "$FAKE_HEAD_SHA"' > "$fake_gh"
chmod +x "$fake_gh"
if GH_BIN="$fake_gh" FAKE_HEAD_SHA="$revision" "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted a syntactically valid but unsuccessful verification run' >&2
  exit 1
fi

rm -rf "$copy"
copy="$(mktemp -d)"
cp -a "$ROOT/." "$copy"
digest="sha256:$(printf '%064d' 0)"
sed -i \
  -e "s#^| Candidate image digest | .* |\$#| Candidate image digest | $digest |#" \
  -e "s#^| Candidate revision | .* |\$#| Candidate revision | $(printf '%040d' 0) |#" \
  -e 's#^| Verification run URL | .* |$#| Verification run URL | https://github.com/matty/terraform-registry/actions/runs/999 |#' \
  -e 's#^| Terraform backend matrix result | .* |$#| Terraform backend matrix result | PASS |#' \
  -e 's#^| Fault and load result | .* |$#| Fault and load result | PASS |#' \
  -e 's#^| Operability gate result | .* |$#| Operability gate result | PASS |#' \
  "$copy/docs/release-candidate-evidence.md"
if "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted a non-existent candidate revision' >&2
  exit 1
fi

rm -rf "$copy"
copy="$(mktemp -d)"
cp -a "$ROOT/." "$copy"
revision="$(git -C "$copy" rev-parse HEAD)"
digest="sha256:$(printf '%064d' 0)"
sed -i \
  -e "s#^| Candidate image digest | .* |\$#| Candidate image digest | $digest |#" \
  -e "s#^| Candidate revision | .* |\$#| Candidate revision | $revision |#" \
  -e 's#^| Verification run URL | .* |$#| Verification run URL | https://github.com/matty/terraform-registry/actions/runs/999 |#' \
  -e 's#^| Terraform backend matrix result | .* |$#| Terraform backend matrix result | PASS |#' \
  -e 's#^| Fault and load result | .* |$#| Fault and load result | PASS |#' \
  -e 's#^| Operability gate result | .* |$#| Operability gate result | PASS |#' \
  "$copy/docs/release-candidate-evidence.md"

create_evidence_artifact() {
  local archive="$1"
  local artifact_sha="$2"
  local artifact_ref="$3"
  local gates_json="$4"
  local pre_publication_passed="${5:-true}"
  local artifact_dir
  artifact_dir="$(mktemp -d)"
  jq -n \
    --arg candidate_sha "$artifact_sha" \
    --arg candidate_ref "$artifact_ref" \
    --argjson gates "$gates_json" \
    --argjson pre_publication_passed "$pre_publication_passed" \
    '{
      schema_version: 1,
      candidate_sha: $candidate_sha,
      candidate_ref: $candidate_ref,
      candidate_version: "0.0.0-test",
      event_name: "workflow_dispatch",
      dotnet_sdk_version: "10.0.301",
      terraform_versions: ["1.12.0", "1.14.2"],
      gates: $gates,
      pre_publication_verification_passed: $pre_publication_passed,
      verification_status: "pre-publication-verification",
      release_certification_complete: false,
      release_certification_status: "incomplete-requires-immutable-registry-digest",
      image_digest: null,
      required_post_publication_evidence: true,
      required_post_publication_evidence_kinds: ["immutable-registry-digest"],
      generated_at_utc: "2026-07-14T00:00:00Z"
    }' > "$artifact_dir/final-candidate-certification-evidence.json"
  # GitHub exposes action artifacts as ZIP archives. The portable test fixture
  # uses the Python standard library because the development image supplies
  # unzip (needed by the validator) but not the optional zip CLI.
  python3 - "$artifact_dir" "$archive" <<'PY'
import sys
import zipfile

source_dir, archive = sys.argv[1:]
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as output:
    output.write(
        f"{source_dir}/final-candidate-certification-evidence.json",
        "final-candidate-certification-evidence.json",
    )
PY
  rm -rf "$artifact_dir"
}

required_gates='[
  {"name":"operability-contract","result":"passed"},
  {"name":"operability","result":"passed"},
  {"name":"fault-load-contract","result":"passed"},
  {"name":"fault-load","result":"passed"},
  {"name":"terraform-backend-contract","result":"passed"},
  {"name":"terraform-backend-matrix","result":"passed"},
  {"name":"release-runbooks","result":"passed"},
  {"name":"release-runbooks-contract","result":"passed"}
]'

fake_gh="$copy/fake-gh"
printf '%s\n' \
  '#!/usr/bin/env bash' \
  'if [[ "$*" == *"/artifacts/"*"/zip"* ]]; then' \
  '  cat "${FAKE_ARTIFACT_ARCHIVE:?}"' \
  '  exit 0' \
  'fi' \
  'if [[ "$*" == *"/artifacts"* ]]; then' \
  '  printf "%s\\n" "${FAKE_ARTIFACT_ID-123}"' \
  '  exit 0' \
  'fi' \
  'if [[ "$*" == *"/jobs"* ]]; then' \
  "  printf '%s\\n' $'Pre-publication candidate verification\\tsuccess'" \
  '  exit 0' \
  'fi' \
  'case "${*: -1}" in' \
  "  .conclusion) printf '%s\\n' success ;;" \
  "  .head_sha) printf '%s\\n' \"\${FAKE_HEAD_SHA:?}\" ;;" \
  "  .name) printf '%s\\n' \"\${FAKE_RUN_NAME:-CI}\" ;;" \
  '  *) exit 2 ;;' \
  'esac' > "$fake_gh"
chmod +x "$fake_gh"
fake_oci="$copy/fake-oci"
printf '%s\n' '#!/usr/bin/env bash' 'printf "%s\\t%s\\n" "$1" "${FAKE_OCI_REVISION:?}"' > "$fake_oci"
chmod +x "$fake_oci"
artifact_archive="$copy/evidence.zip"
create_evidence_artifact "$artifact_archive" "$revision" refs/heads/develop "$required_gates"

# A workflow_dispatch run is recorded at the workflow ref (develop), while its
# uploaded evidence binds the explicitly checked-out release candidate. The
# validator must accept that authoritative, bound artifact.
GH_BIN="$fake_gh" OCI_INSPECT_BIN="$fake_oci" FAKE_HEAD_SHA="$(printf '%040d' 0)" FAKE_OCI_REVISION="$revision" FAKE_ARTIFACT_ARCHIVE="$artifact_archive" "$copy/scripts/remediation/validate-requirement-evidence.sh" --check

mismatched_artifact="$copy/mismatched-evidence.zip"
create_evidence_artifact "$mismatched_artifact" "$(printf '%040d' 0)" refs/heads/develop "$required_gates"
if GH_BIN="$fake_gh" OCI_INSPECT_BIN="$fake_oci" FAKE_HEAD_SHA="$(printf '%040d' 0)" FAKE_OCI_REVISION="$revision" FAKE_ARTIFACT_ARCHIVE="$mismatched_artifact" "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted evidence for a different candidate revision' >&2
  exit 1
fi

if GH_BIN="$fake_gh" OCI_INSPECT_BIN="$fake_oci" FAKE_HEAD_SHA="$(printf '%040d' 0)" FAKE_OCI_REVISION="$revision" FAKE_ARTIFACT_ID= FAKE_ARTIFACT_ARCHIVE="$artifact_archive" "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted a missing candidate evidence artifact' >&2
  exit 1
fi

if GH_BIN="$fake_gh" OCI_INSPECT_BIN="$fake_oci" FAKE_HEAD_SHA="$(printf '%040d' 0)" FAKE_OCI_REVISION="$revision" FAKE_ARTIFACT_ARCHIVE="$copy/unreadable-evidence.zip" "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted an unreadable candidate evidence artifact' >&2
  exit 1
fi

missing_gate_artifact="$copy/missing-gate-evidence.zip"
missing_gate_json='[
  {"name":"operability-contract","result":"passed"},
  {"name":"operability","result":"passed"},
  {"name":"fault-load-contract","result":"passed"},
  {"name":"fault-load","result":"passed"},
  {"name":"terraform-backend-contract","result":"passed"},
  {"name":"terraform-backend-matrix","result":"passed"},
  {"name":"release-runbooks","result":"passed"}
]'
create_evidence_artifact "$missing_gate_artifact" "$revision" refs/heads/develop "$missing_gate_json"
if GH_BIN="$fake_gh" OCI_INSPECT_BIN="$fake_oci" FAKE_HEAD_SHA="$(printf '%040d' 0)" FAKE_OCI_REVISION="$revision" FAKE_ARTIFACT_ARCHIVE="$missing_gate_artifact" "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted evidence with a missing required gate' >&2
  exit 1
fi

failed_verification_artifact="$copy/failed-verification-evidence.zip"
create_evidence_artifact "$failed_verification_artifact" "$revision" refs/heads/develop "$required_gates" false
if GH_BIN="$fake_gh" OCI_INSPECT_BIN="$fake_oci" FAKE_HEAD_SHA="$(printf '%040d' 0)" FAKE_OCI_REVISION="$revision" FAKE_ARTIFACT_ARCHIVE="$failed_verification_artifact" "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted evidence whose pre-publication verification failed' >&2
  exit 1
fi

noncanonical_ref_artifact="$copy/noncanonical-ref-evidence.zip"
create_evidence_artifact "$noncanonical_ref_artifact" "$revision" refs/pull/1/head "$required_gates"
if GH_BIN="$fake_gh" OCI_INSPECT_BIN="$fake_oci" FAKE_HEAD_SHA="$(printf '%040d' 0)" FAKE_OCI_REVISION="$revision" FAKE_ARTIFACT_ARCHIVE="$noncanonical_ref_artifact" "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted evidence with a non-canonical candidate reference' >&2
  exit 1
fi

if GH_BIN="$fake_gh" OCI_INSPECT_BIN="$fake_oci" FAKE_HEAD_SHA="$(printf '%040d' 0)" FAKE_OCI_REVISION="$revision" FAKE_ARTIFACT_ARCHIVE="$artifact_archive" FAKE_RUN_NAME=other "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted a non-CI workflow as candidate verification' >&2
  exit 1
fi
if GH_BIN="$fake_gh" OCI_INSPECT_BIN="$fake_oci" FAKE_HEAD_SHA="$(printf '%040d' 0)" FAKE_OCI_REVISION="$(printf '%040d' 0)" FAKE_ARTIFACT_ARCHIVE="$artifact_archive" "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted a digest whose OCI revision label is unrelated' >&2
  exit 1
fi

echo 'Requirement evidence validator tests passed.'
