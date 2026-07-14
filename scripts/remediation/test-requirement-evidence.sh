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
  -e "s#| Candidate image digest | REQUIRED |#| Candidate image digest | $digest |#" \
  -e "s#| Candidate revision | REQUIRED |#| Candidate revision | $revision |#" \
  -e 's#| Verification run URL | REQUIRED |#| Verification run URL | https://github.com/matty/terraform-registry/actions/runs/999 |#' \
  -e 's#| Terraform backend matrix result | REQUIRED |#| Terraform backend matrix result | PASS |#' \
  -e 's#| Fault and load result | REQUIRED |#| Fault and load result | PASS |#' \
  -e 's#| Operability gate result | REQUIRED |#| Operability gate result | PASS |#' \
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
  -e "s#| Candidate image digest | REQUIRED |#| Candidate image digest | $digest |#" \
  -e "s#| Candidate revision | REQUIRED |#| Candidate revision | $(printf '%040d' 0) |#" \
  -e 's#| Verification run URL | REQUIRED |#| Verification run URL | https://github.com/matty/terraform-registry/actions/runs/999 |#' \
  -e 's#| Terraform backend matrix result | REQUIRED |#| Terraform backend matrix result | PASS |#' \
  -e 's#| Fault and load result | REQUIRED |#| Fault and load result | PASS |#' \
  -e 's#| Operability gate result | REQUIRED |#| Operability gate result | PASS |#' \
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
  -e "s#| Candidate image digest | REQUIRED |#| Candidate image digest | $digest |#" \
  -e "s#| Candidate revision | REQUIRED |#| Candidate revision | $revision |#" \
  -e 's#| Verification run URL | REQUIRED |#| Verification run URL | https://github.com/matty/terraform-registry/actions/runs/999 |#' \
  -e 's#| Terraform backend matrix result | REQUIRED |#| Terraform backend matrix result | PASS |#' \
  -e 's#| Fault and load result | REQUIRED |#| Fault and load result | PASS |#' \
  -e 's#| Operability gate result | REQUIRED |#| Operability gate result | PASS |#' \
  "$copy/docs/release-candidate-evidence.md"
fake_gh="$copy/fake-gh"
printf '%s\n' \
  '#!/usr/bin/env bash' \
  'case "${*: -1}" in' \
  "  .conclusion) printf '%s\\n' success ;;" \
  "  .head_sha) printf '%s\\n' \"\${FAKE_HEAD_SHA:?}\" ;;" \
  "  .name) printf '%s\\n' CI ;;" \
  '  *) exit 2 ;;' \
  'esac' > "$fake_gh"
chmod +x "$fake_gh"
GH_BIN="$fake_gh" FAKE_HEAD_SHA="$revision" "$copy/scripts/remediation/validate-requirement-evidence.sh" --check

echo 'Requirement evidence validator tests passed.'
